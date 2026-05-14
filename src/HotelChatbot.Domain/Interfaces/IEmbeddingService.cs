namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Embedding-Generierung.
/// Konvertiert Text in Vektor-Repräsentationen für Similarity-Search.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generiert Embedding-Vektor für einen einzelnen Text.
    /// </summary>
    /// <param name="text">Text der vektorisiert werden soll</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Embedding-Vektor als float-Array</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generiert Embedding-Vektoren für mehrere Texte (Batch).
    /// </summary>
    /// <param name="texts">Liste von Texten</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Liste von Embedding-Vektoren</returns>
    Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);
}
