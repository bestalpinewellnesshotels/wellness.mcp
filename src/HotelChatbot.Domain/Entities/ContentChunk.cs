namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Repräsentiert einen gecrawlten Content-Chunk aus der Hotel-Webseite.
/// Diese Chunks werden vektorisiert und im Vector Store gespeichert.
/// </summary>
public class ContentChunk
{
    /// <summary>
    /// Eindeutige Chunk-ID
    /// </summary>
    public required string ChunkId { get; set; }

    /// <summary>
    /// Hotel-ID zu dem dieser Content gehört
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Quell-URL von der der Content gecrawlt wurde
    /// </summary>
    public required string SourceUrl { get; set; }

    /// <summary>
    /// Textinhalt des Chunks
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Titel oder Überschrift des Abschnitts
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Vektor-Embedding des Contents (wird vom Vector Store generiert)
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Metadaten (z.B. Seitenstruktur, Kategorie)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Sprache des Contents (ISO 639-1 Code)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Zeitpunkt des Crawlings
    /// </summary>
    public DateTime CrawledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ob dieser Chunk noch aktuell ist
    /// </summary>
    public bool IsActive { get; set; } = true;
}
