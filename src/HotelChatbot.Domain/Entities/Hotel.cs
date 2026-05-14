namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Repräsentiert ein Hotel mit seinen Basis-Informationen.
/// Diese Entity wird verwendet, um Hotel-Konfigurationen zu speichern.
/// </summary>
public class Hotel
{
    /// <summary>
    /// Eindeutige ID des Hotels (z.B. "hotel_123")
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Name des Hotels
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Primäre Domain des Hotels (z.B. "example-hotel.com")
    /// </summary>
    public required string Domain { get; set; }

    /// <summary>
    /// Erlaubte Domains für CORS (z.B. ["example-hotel.com", "www.example-hotel.com"])
    /// </summary>
    public List<string> AllowedDomains { get; set; } = new();

    /// <summary>
    /// API-Schlüssel für dieses Hotel (zur Authentifizierung)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Ob der Chatbot für dieses Hotel aktiv ist
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Erstellungsdatum
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Letzte Aktualisierung
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
