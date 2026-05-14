namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Repräsentiert eine einzelne Nachricht in einer Chat-Session.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Eindeutige Message-ID
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// Session-ID zu der diese Nachricht gehört
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Rolle des Absenders (user, assistant, system)
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Inhalt der Nachricht
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Zeitpunkt der Nachricht
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ob die Nachricht per Voice-Input eingegeben wurde
    /// </summary>
    public bool IsVoiceInput { get; set; } = false;

    /// <summary>
    /// Sprache der Nachricht (ISO 639-1 Code, z.B. "de", "en")
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// IDs der verwendeten Retrieval-Chunks (für Nachvollziehbarkeit)
    /// </summary>
    public List<string> RetrievalChunkIds { get; set; } = new();

    /// <summary>
    /// Confidence Score der Antwort (0.0 - 1.0)
    /// Falls unter Schwellwert, sollte keine Antwort generiert werden
    /// </summary>
    public double? ConfidenceScore { get; set; }
}
