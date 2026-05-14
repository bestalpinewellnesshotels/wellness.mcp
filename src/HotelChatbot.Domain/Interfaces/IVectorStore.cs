namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Vector Store Operationen.
/// Implementierungen können z.B. Qdrant, Pinecone, Weaviate nutzen.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Sucht ähnliche Content-Chunks basierend auf einer Query.
    /// </summary>
    /// <param name="hotelId">Hotel-ID für die Suche</param>
    /// <param name="query">Such-Query</param>
    /// <param name="topK">Anzahl der zurückzugebenden Ergebnisse</param>
    /// <param name="minScore">Minimaler Similarity Score (0.0 - 1.0)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Liste der relevanten Content-Chunks mit Scores</returns>
    Task<List<(Entities.ContentChunk Chunk, double Score)>> SearchAsync(
        string hotelId,
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sucht ähnliche Content-Chunks über ALLE Hotels hinweg.
    /// </summary>
    /// <param name="query">Such-Query</param>
    /// <param name="topK">Anzahl der zurückzugebenden Ergebnisse pro Hotel</param>
    /// <param name="minScore">Minimaler Similarity Score (0.0 - 1.0)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Dictionary mit Hotel-ID als Key und Liste der relevanten Content-Chunks mit Scores</returns>
    Task<Dictionary<string, List<(Entities.ContentChunk Chunk, double Score)>>> SearchAllHotelsAsync(
        string query,
        int topK = 5,
        double minScore = 0.7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fügt einen Content-Chunk zum Vector Store hinzu.
    /// </summary>
    Task AddChunkAsync(
        Entities.ContentChunk chunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fügt mehrere Content-Chunks zum Vector Store hinzu.
    /// </summary>
    Task AddChunksAsync(
        IEnumerable<Entities.ContentChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Löscht alle Chunks eines Hotels.
    /// </summary>
    Task DeleteHotelChunksAsync(
        string hotelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gibt die Anzahl der Content-Chunks für ein Hotel zurück.
    /// </summary>
    Task<int> GetChunkCountAsync(
        string hotelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gibt alle bereits indexierten Source-URLs für ein Hotel zurück.
    /// </summary>
    Task<HashSet<string>> GetExistingUrlsAsync(
        string hotelId,
        CancellationToken cancellationToken = default);
}
