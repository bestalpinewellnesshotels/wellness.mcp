namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Repräsentiert eine Chat-Session zwischen einem User und dem Chatbot.
/// Sessions werden verwendet, um Konversationen zu verfolgen und zu protokollieren.
/// </summary>
public class ChatSession
{
    /// <summary>
    /// Eindeutige Session-ID (GUID)
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Hotel-ID zu der diese Session gehört
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// User-Identifikator (kann anonymisiert sein)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// IP-Adresse des Users (für Rate Limiting und Logging)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User-Agent des Browsers
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Zeitpunkt des Session-Starts
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Zeitpunkt des letzten Updates
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ob die Session noch aktiv ist
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Navigation Property: Chat-Nachrichten dieser Session
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();
}
