using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HotelChatbot.Infrastructure.VectorStore;

/// <summary>
/// In-Memory Vector Store Implementierung für Development/Testing.
/// Für Production sollte ein echter Vector Store wie Qdrant, Pinecone oder Weaviate verwendet werden.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, List<ContentChunk>> _storage = new();
    private readonly ILogger<InMemoryVectorStore> _logger;

    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sucht ähnliche Content-Chunks.
    /// Vereinfachte Implementierung mit Keyword-Matching (für Production durch Embedding-basierte Suche ersetzen).
    /// </summary>
    public Task<List<(ContentChunk Chunk, double Score)>> SearchAsync(
        string hotelId,
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        if (!_storage.TryGetValue(hotelId, out var chunks))
        {
            _logger.LogWarning("Keine Chunks gefunden für HotelId={HotelId}", hotelId);
            return Task.FromResult(new List<(ContentChunk, double)>());
        }

        var queryTerms = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = chunks
            .Where(c => c.IsActive)
            .Select(chunk =>
            {
                var content = chunk.Content.ToLowerInvariant();
                var title = chunk.Title?.ToLowerInvariant() ?? string.Empty;

                // Einfacher Scoring-Algorithmus: Zähle Keyword-Matches
                var matches = queryTerms.Count(term => 
                    content.Contains(term) || title.Contains(term));

                var score = (double)matches / queryTerms.Length;
                
                // Bonus für Title-Matches
                if (queryTerms.Any(term => title.Contains(term)))
                {
                    score += 0.2;
                }

                return (Chunk: chunk, Score: Math.Min(1.0, score));
            })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogInformation(
            "Vector Search für HotelId={HotelId}: {ResultCount} Ergebnisse gefunden",
            hotelId, results.Count);

        return Task.FromResult(results);
    }

    /// <summary>
    /// Sucht ähnliche Content-Chunks über ALLE Hotels hinweg.
    /// </summary>
    public Task<Dictionary<string, List<(ContentChunk Chunk, double Score)>>> SearchAllHotelsAsync(
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        var queryTerms = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var resultsByHotel = new Dictionary<string, List<(ContentChunk Chunk, double Score)>>();

        foreach (var (hotelId, chunks) in _storage)
        {
            var results = chunks
                .Where(c => c.IsActive)
                .Select(chunk =>
                {
                    var content = chunk.Content.ToLowerInvariant();
                    var title = chunk.Title?.ToLowerInvariant() ?? string.Empty;

                    // Einfacher Scoring-Algorithmus: Zähle Keyword-Matches
                    var matches = queryTerms.Count(term => 
                        content.Contains(term) || title.Contains(term));

                    var score = (double)matches / queryTerms.Length;
                    
                    // Bonus für Title-Matches
                    if (queryTerms.Any(term => title.Contains(term)))
                    {
                        score += 0.2;
                    }

                    return (Chunk: chunk, Score: Math.Min(1.0, score));
                })
                .Where(r => r.Score >= minScore)
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            if (results.Any())
            {
                resultsByHotel[hotelId] = results;
            }
        }

        _logger.LogInformation(
            "Vector Search über alle Hotels: {HotelCount} Hotels durchsucht, {TotalResults} Ergebnisse gefunden",
            resultsByHotel.Count, resultsByHotel.Sum(x => x.Value.Count));

        return Task.FromResult(resultsByHotel);
    }

    public Task AddChunkAsync(
        ContentChunk chunk,
        CancellationToken cancellationToken = default)
    {
        _storage.AddOrUpdate(
            chunk.HotelId,
            new List<ContentChunk> { chunk },
            (key, existing) =>
            {
                existing.Add(chunk);
                return existing;
            });

        _logger.LogInformation(
            "Chunk hinzugefügt: HotelId={HotelId}, ChunkId={ChunkId}",
            chunk.HotelId, chunk.ChunkId);

        return Task.CompletedTask;
    }

    public Task AddChunksAsync(
        IEnumerable<ContentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            _storage.AddOrUpdate(
                chunk.HotelId,
                new List<ContentChunk> { chunk },
                (key, existing) =>
                {
                    existing.Add(chunk);
                    return existing;
                });
        }

        _logger.LogInformation("{Count} Chunks hinzugefügt", chunks.Count());
        return Task.CompletedTask;
    }

    public Task DeleteHotelChunksAsync(
        string hotelId,
        CancellationToken cancellationToken = default)
    {
        _storage.TryRemove(hotelId, out _);
        _logger.LogInformation("Alle Chunks für HotelId={HotelId} gelöscht", hotelId);
        return Task.CompletedTask;
    }

    public Task<int> GetChunkCountAsync(
        string hotelId,
        CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(hotelId, out var chunks))
        {
            return Task.FromResult(chunks.Count(c => c.IsActive));
        }
        return Task.FromResult(0);
    }
}
