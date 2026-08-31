using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Collections.Generic;
using System.Text.Json;

namespace HotelChatbot.Infrastructure.VectorStore;

/// <summary>
/// PostgreSQL Vector Store Implementierung mit pgvector Extension.
/// Bietet persistente Speicherung von Embeddings mit GPU-beschleunigter HNSW-Index Similarity Search.
/// </summary>
public class PostgreSQLVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<PostgreSQLVectorStore> _logger;
    private readonly int _embeddingDimensions;
    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PostgreSQLVectorStore(
        IConfiguration configuration,
        IEmbeddingService embeddingService,
        ILogger<PostgreSQLVectorStore> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL") 
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
        _embeddingService = embeddingService;
        _logger = logger;
        _embeddingDimensions = configuration.GetValue("OpenAI:EmbeddingDimensions", 1536);
    }

    /// <summary>
    /// Initialisiert Tabellen, pgvector-Spalte und HNSW-Index.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Basis-Schema
            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS content_chunks (
                    chunk_id TEXT PRIMARY KEY,
                    hotel_id TEXT NOT NULL,
                    source_url TEXT NOT NULL,
                    title TEXT,
                    content TEXT NOT NULL,
                    language TEXT NOT NULL DEFAULT 'de',
                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    crawled_at TIMESTAMP NOT NULL,
                    embedding JSONB,
                    embedding_vector vector({_embeddingDimensions})
                );

                CREATE INDEX IF NOT EXISTS idx_hotel_id ON content_chunks(hotel_id);
                CREATE INDEX IF NOT EXISTS idx_is_active ON content_chunks(is_active);
                CREATE INDEX IF NOT EXISTS idx_hotel_active ON content_chunks(hotel_id, is_active) WHERE is_active = TRUE;
            ";

            try
            {
                await using var extCmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
                await extCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "pgvector Extension konnte nicht erstellt werden (evtl. bereits vorhanden / fehlende Rechte)");
            }

            await using (var cmd = new NpgsqlCommand(createTableSql, conn))
            {
                try
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Tabelle ohne vector-Typ anlegen, Spalte später ergänzen
                    _logger.LogWarning(ex, "Schema-Anlage mit vector-Spalte fehlgeschlagen – Fallback ohne Typ");
                    var fallbackSql = @"
                        CREATE TABLE IF NOT EXISTS content_chunks (
                            chunk_id TEXT PRIMARY KEY,
                            hotel_id TEXT NOT NULL,
                            source_url TEXT NOT NULL,
                            title TEXT,
                            content TEXT NOT NULL,
                            language TEXT NOT NULL DEFAULT 'de',
                            is_active BOOLEAN NOT NULL DEFAULT TRUE,
                            crawled_at TIMESTAMP NOT NULL,
                            embedding JSONB
                        );
                        CREATE INDEX IF NOT EXISTS idx_hotel_id ON content_chunks(hotel_id);
                        CREATE INDEX IF NOT EXISTS idx_is_active ON content_chunks(is_active);
                        CREATE INDEX IF NOT EXISTS idx_hotel_active ON content_chunks(hotel_id, is_active) WHERE is_active = TRUE;
                    ";
                    await using var fb = new NpgsqlCommand(fallbackSql, conn);
                    await fb.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            try
            {
                await using var alterCmd = new NpgsqlCommand(
                    $"ALTER TABLE content_chunks ADD COLUMN IF NOT EXISTS embedding_vector vector({_embeddingDimensions});", conn);
                await alterCmd.ExecuteNonQueryAsync(cancellationToken);

                await using var idxCmd = new NpgsqlCommand(
                    @"CREATE INDEX IF NOT EXISTS content_chunks_embedding_vector_idx
                      ON content_chunks USING hnsw (embedding_vector vector_cosine_ops);", conn);
                await idxCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HNSW-Index konnte nicht erstellt werden");
            }

            _isInitialized = true;
            _logger.LogInformation("PostgreSQL VectorStore initialisiert (pgvector + HNSW)");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<(ContentChunk Chunk, double Score)>> SearchAsync(
        string hotelId,
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // HNSW: erst Top-K per Distanz, Score-Filter in C# (kein Filter in WHERE)
        var fetchLimit = Math.Max(topK * 3, topK);
        var sql = @"
            SELECT 
                chunk_id, hotel_id, source_url, title, content, language, crawled_at,
                1 - (embedding_vector <=> $1::vector) as similarity
            FROM content_chunks
            WHERE hotel_id = $2
              AND is_active = TRUE
              AND embedding_vector IS NOT NULL
            ORDER BY embedding_vector <=> $1::vector
            LIMIT $3;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(queryEmbedding);
        cmd.Parameters.AddWithValue(hotelId);
        cmd.Parameters.AddWithValue(fetchLimit);

        var results = new List<(ContentChunk Chunk, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var similarity = reader.GetDouble(7);
            if (similarity < minScore)
                continue;

            var chunk = new ContentChunk
            {
                ChunkId = reader.GetString(0),
                HotelId = reader.GetString(1),
                SourceUrl = reader.GetString(2),
                Title = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = reader.GetString(4),
                Language = reader.GetString(5),
                CrawledAt = reader.GetDateTime(6),
                IsActive = true
            };

            results.Add((chunk, similarity));
            if (results.Count >= topK)
                break;
        }

        _logger.LogInformation(
            "Vector Search für HotelId={HotelId}: {ResultCount} Ergebnisse gefunden (pgvector HNSW)",
            hotelId, results.Count);

        return results;
    }

    public async Task<Dictionary<string, List<(ContentChunk Chunk, double Score)>>> SearchAllHotelsAsync(
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Breite HNSW-Kandidaten ohne Score-Filter in WHERE, dann pro Hotel topK in C#
        var candidateLimit = Math.Max(topK * 80, 200);
        var sql = @"
            SELECT 
                chunk_id, hotel_id, source_url, title, content, language, crawled_at,
                1 - (embedding_vector <=> $1::vector) as similarity
            FROM content_chunks
            WHERE is_active = TRUE
              AND embedding_vector IS NOT NULL
            ORDER BY embedding_vector <=> $1::vector
            LIMIT $2;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(queryEmbedding);
        cmd.Parameters.AddWithValue(candidateLimit);

        var resultsByHotel = new Dictionary<string, List<(ContentChunk Chunk, double Score)>>(StringComparer.Ordinal);

        // Reader muss geschlossen sein, bevor dieselbe Connection eine zweite Query ausführt.
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var similarity = reader.GetDouble(7);
                if (similarity < minScore)
                    continue;

                var hotelId = reader.GetString(1);
                if (!resultsByHotel.TryGetValue(hotelId, out var list))
                {
                    list = new List<(ContentChunk, double)>(topK);
                    resultsByHotel[hotelId] = list;
                }

                if (list.Count >= topK)
                    continue;

                list.Add((new ContentChunk
                {
                    ChunkId = reader.GetString(0),
                    HotelId = hotelId,
                    SourceUrl = reader.GetString(2),
                    Title = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Content = reader.GetString(4),
                    Language = reader.GetString(5),
                    CrawledAt = reader.GetDateTime(6),
                    IsActive = true
                }, similarity));
            }
        }

        // Hotels ohne Treffer in der globalen Top-N: gezielte Nachfüllung (max. 20 Hotels)
        var coveredHotels = resultsByHotel.Keys.ToHashSet(StringComparer.Ordinal);
        var missingHotelIds = await LoadActiveHotelIdsMissingAsync(conn, coveredHotels, cancellationToken);
        foreach (var missingId in missingHotelIds.Take(20))
        {
            var hotelHits = await SearchHotelCandidatesAsync(
                conn, queryEmbedding, missingId, topK, minScore, cancellationToken);
            if (hotelHits.Count > 0)
                resultsByHotel[missingId] = hotelHits;
        }

        var totalResults = resultsByHotel.Values.Sum(list => list.Count);

        _logger.LogInformation(
            "Vector Search über alle Hotels: {HotelCount} Hotels, {TotalResults} Ergebnisse (HNSW + per-hotel fill)",
            resultsByHotel.Count, totalResults);

        return resultsByHotel;
    }

    private static async Task<List<string>> LoadActiveHotelIdsMissingAsync(
        NpgsqlConnection conn,
        HashSet<string> alreadyCovered,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT DISTINCT hotel_id
            FROM content_chunks
            WHERE is_active = TRUE AND embedding_vector IS NOT NULL;
        ";
        var missing = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!alreadyCovered.Contains(id))
                missing.Add(id);
        }

        return missing;
    }

    private static async Task<List<(ContentChunk Chunk, double Score)>> SearchHotelCandidatesAsync(
        NpgsqlConnection conn,
        float[] queryEmbedding,
        string hotelId,
        int topK,
        double minScore,
        CancellationToken cancellationToken)
    {
        var fetchLimit = Math.Max(topK * 3, topK);
        var sql = @"
            SELECT 
                chunk_id, hotel_id, source_url, title, content, language, crawled_at,
                1 - (embedding_vector <=> $1::vector) as similarity
            FROM content_chunks
            WHERE hotel_id = $2
              AND is_active = TRUE
              AND embedding_vector IS NOT NULL
            ORDER BY embedding_vector <=> $1::vector
            LIMIT $3;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(queryEmbedding);
        cmd.Parameters.AddWithValue(hotelId);
        cmd.Parameters.AddWithValue(fetchLimit);

        var results = new List<(ContentChunk Chunk, double Score)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var similarity = reader.GetDouble(7);
            if (similarity < minScore)
                continue;

            results.Add((new ContentChunk
            {
                ChunkId = reader.GetString(0),
                HotelId = reader.GetString(1),
                SourceUrl = reader.GetString(2),
                Title = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = reader.GetString(4),
                Language = reader.GetString(5),
                CrawledAt = reader.GetDateTime(6),
                IsActive = true
            }, similarity));

            if (results.Count >= topK)
                break;
        }

        return results;
    }

    public async Task AddChunkAsync(
        ContentChunk chunk,
        CancellationToken cancellationToken = default)
    {
        await AddChunksAsync(new[] { chunk }, cancellationToken);
    }

    public async Task AddChunksAsync(
        IEnumerable<ContentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var chunkList = chunks.ToList();
        if (!chunkList.Any())
            return;

        // Embeddings generieren
        var texts = chunkList.Select(c => c.Content).ToList();
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        var sql = @"
            INSERT INTO content_chunks 
                (chunk_id, hotel_id, source_url, title, content, language, is_active, crawled_at, embedding, embedding_vector)
            VALUES 
                ($1, $2, $3, $4, $5, $6, $7, $8, $9::jsonb, $10::vector)
            ON CONFLICT (chunk_id) 
            DO UPDATE SET
                content = EXCLUDED.content,
                title = EXCLUDED.title,
                embedding = EXCLUDED.embedding,
                embedding_vector = EXCLUDED.embedding_vector,
                crawled_at = EXCLUDED.crawled_at;
        ";

        for (int i = 0; i < chunkList.Count; i++)
        {
            var chunk = chunkList[i];
            var embedding = embeddings[i];
            var embeddingJson = JsonSerializer.Serialize(embedding);

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue(chunk.ChunkId);
            cmd.Parameters.AddWithValue(chunk.HotelId);
            cmd.Parameters.AddWithValue(chunk.SourceUrl);
            cmd.Parameters.AddWithValue(chunk.Title ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(chunk.Content);
            cmd.Parameters.AddWithValue(chunk.Language);
            cmd.Parameters.AddWithValue(chunk.IsActive);
            cmd.Parameters.AddWithValue(chunk.CrawledAt);
            cmd.Parameters.AddWithValue(embeddingJson);
            cmd.Parameters.AddWithValue(embedding);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("{Count} Chunks in PostgreSQL gespeichert (Batch-TX + pgvector)", chunkList.Count);
    }

    public async Task DeleteHotelChunksAsync(
        string hotelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "DELETE FROM content_chunks WHERE hotel_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        var deletedCount = await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "{Count} Chunks für HotelId={HotelId} gelöscht",
            deletedCount, hotelId);
    }

    public async Task<int> GetChunkCountAsync(
        string hotelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT COUNT(*) FROM content_chunks WHERE hotel_id = $1 AND is_active = true;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        var count = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count ?? 0);
    }

    public async Task<HashSet<string>> GetExistingUrlsAsync(
        string hotelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT DISTINCT source_url FROM content_chunks WHERE hotel_id = $1 AND is_active = true;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var url = reader.GetString(0);
            urls.Add(url);
        }

        _logger.LogInformation("Gefunden: {Count} existierende URLs für Hotel {HotelId}", urls.Count, hotelId);
        return urls;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL Health Check fehlgeschlagen");
            return false;
        }
    }
}
