namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Konfigurierbare System-Prompts für den Hotel-Chatbot.
/// Ermöglicht die Verwaltung aller LLM-Anweisungen über das CMS.
/// </summary>
public class SystemPrompt
{
    /// <summary>
    /// Datenbankprimärschlüssel
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Eindeutiger Bezeichner für diesen Prompt (z. B. "chat.system", "intent.hotel_wellness").
    /// Wird vom Code verwendet, um den richtigen Prompt zu laden.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Menschenlesbarer Name für das CMS
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Beschreibung: Wofür wird dieser Prompt verwendet?
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Prompt-Text auf Deutsch (zur Verwaltung im CMS).
    /// Kann Platzhalter enthalten: {hotelName}, {context}, {userQuery}, {requirements}
    /// </summary>
    public required string ContentDe { get; set; }

    /// <summary>
    /// Prompt-Text auf Englisch (wird an das LLM gesendet, erzielt bessere Ergebnisse).
    /// Kann Platzhalter enthalten: {hotelName}, {context}, {userQuery}, {requirements}
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Sprache des Prompts ("de", "en") oder null für sprachunabhängige Prompts
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Ob dieser Prompt aktiv ist (false = eingebauter Fallback wird verwendet)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Erstellungsdatum
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Datum der letzten Änderung
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
