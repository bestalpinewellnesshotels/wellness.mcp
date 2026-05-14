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
    private bool _isInitialized = false;

    public PostgreSQLVectorStore(
        IConfiguration configuration,
        IEmbeddingService embeddingService,
        ILogger<PostgreSQLVectorStore> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL") 
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>
    /// Initialisiert die Datenbank-Struktur (Tabellen ohne pgvector).
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Tabelle erstellen (Embeddings als JSONB gespeichert)
        var createTableSql = @"
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

        await using (var cmd = new NpgsqlCommand(createTableSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _isInitialized = true;
        _logger.LogInformation("PostgreSQL VectorStore initialisiert (ohne pgvector)");
    }

    public async Task<List<(ContentChunk Chunk, double Score)>> SearchAsync(
        string hotelId,
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Query Embedding generieren
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Native pgvector query mit <-> Operator (Cosine Distance)
        // Cosine Distance = 1 - Cosine Similarity
        var sql = @"
            SELECT 
                chunk_id, hotel_id, source_url, title, content, language, crawled_at,
                1 - (embedding_vector <=> $1::vector) as similarity
            FROM content_chunks
            WHERE hotel_id = $2
              AND is_active = TRUE
              AND embedding_vector IS NOT NULL
              AND (1 - (embedding_vector <=> $1::vector)) >= $3
            ORDER BY embedding_vector <=> $1::vector
            LIMIT $4;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(queryEmbedding);
        cmd.Parameters.AddWithValue(hotelId);
        cmd.Parameters.AddWithValue(minScore);
        cmd.Parameters.AddWithValue(topK);

        var results = new List<(ContentChunk Chunk, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
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

            var similarity = reader.GetDouble(7);
            results.Add((chunk, similarity));
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

        // Query Embedding generieren
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Native pgvector query mit <=> Operator (Cosine Distance)
        // Verwende DISTINCT ON um Top-K pro Hotel zu bekommen
        var sql = @"
            WITH ranked_chunks AS (
                SELECT 
                    chunk_id, hotel_id, source_url, title, content, language, crawled_at,
                    1 - (embedding_vector <=> $1::vector) as similarity,
                    ROW_NUMBER() OVER (PARTITION BY hotel_id ORDER BY embedding_vector <=> $1::vector) as rank
                FROM content_chunks
                WHERE is_active = TRUE
                  AND embedding_vector IS NOT NULL
                  AND (1 - (embedding_vector <=> $1::vector)) >= $2
            )
            SELECT chunk_id, hotel_id, source_url, title, content, language, crawled_at, similarity
            FROM ranked_chunks
            WHERE rank <= $3
            ORDER BY hotel_id, similarity DESC;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(queryEmbedding);
        cmd.Parameters.AddWithValue(minScore);
        cmd.Parameters.AddWithValue(topK);

        var resultsByHotel = new Dictionary<string, List<(ContentChunk Chunk, double Score)>>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
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

            var similarity = reader.GetDouble(7);

            if (!resultsByHotel.ContainsKey(chunk.HotelId))
            {
                resultsByHotel[chunk.HotelId] = new List<(ContentChunk, double)>();
            }

            resultsByHotel[chunk.HotelId].Add((chunk, similarity));
        }

        var totalResults = resultsByHotel.Values.Sum(list => list.Count);

        _logger.LogInformation(
            "Vector Search über alle Hotels: {HotelCount} Hotels durchsucht, {TotalResults} Ergebnisse gefunden (pgvector HNSW)",
            resultsByHotel.Count, totalResults);

        return resultsByHotel;
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

        // Updated SQL to include embedding_vector column
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

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(chunk.ChunkId);
            cmd.Parameters.AddWithValue(chunk.HotelId);
            cmd.Parameters.AddWithValue(chunk.SourceUrl);
            cmd.Parameters.AddWithValue(chunk.Title ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(chunk.Content);
            cmd.Parameters.AddWithValue(chunk.Language);
            cmd.Parameters.AddWithValue(chunk.IsActive);
            cmd.Parameters.AddWithValue(chunk.CrawledAt);
            cmd.Parameters.AddWithValue(embeddingJson);
            cmd.Parameters.AddWithValue(embedding); // Vector column

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("{Count} Chunks in PostgreSQL gespeichert (mit pgvector)", chunkList.Count);
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
