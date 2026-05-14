namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Chat-Request vom Frontend.
/// </summary>
public class ChatRequestDto
{
    /// <summary>
    /// Hotel-ID (aus data-hotel-id Attribut)
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Session-ID (wird beim ersten Request generiert, danach wiederverwendet)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// User-Nachricht
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Ob die Nachricht per Voice eingegeben wurde
    /// </summary>
    public bool IsVoice { get; set; } = false;

    /// <summary>
    /// Sprache der Nachricht (optional, wird sonst automatisch erkannt)
    /// </summary>
    public string? Language { get; set; }
}
