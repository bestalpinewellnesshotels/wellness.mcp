namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Vollständige Darstellung eines System-Prompts für API-Antworten.
/// </summary>
public class SystemPromptDto
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string ContentDe { get; set; }
    public required string Content { get; set; }
    public string? Language { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Payload zum Anlegen eines neuen System-Prompts.
/// </summary>
public class CreateSystemPromptDto
{
    /// <summary>
    /// Eindeutiger Schlüssel (z. B. "chat.system.custom"). Darf kein Leerzeichen enthalten.
    /// </summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Prompt-Text auf Deutsch (zur Verwaltung im CMS). Platzhalter wie {hotelName} werden zur Laufzeit ersetzt.
    /// </summary>
    public required string ContentDe { get; set; }

    /// <summary>
    /// Prompt-Text auf Englisch (wird an das LLM gesendet). Platzhalter wie {hotelName} werden zur Laufzeit ersetzt.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Sprache des Prompts ("de", "en") oder null für sprachunabhängige Prompts.
    /// </summary>
    public string? Language { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Payload zum Aktualisieren eines bestehenden System-Prompts.
/// Der Key kann nicht geändert werden.
/// </summary>
public class UpdateSystemPromptDto
{
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Prompt-Text auf Deutsch (zur Verwaltung im CMS). Platzhalter wie {hotelName} werden zur Laufzeit ersetzt.
    /// </summary>
    public required string ContentDe { get; set; }

    /// <summary>
    /// Prompt-Text auf Englisch (wird an das LLM gesendet). Platzhalter wie {hotelName} werden zur Laufzeit ersetzt.
    /// </summary>
    public required string Content { get; set; }

    public string? Language { get; set; }

    public bool IsActive { get; set; } = true;
}
