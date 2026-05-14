namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Text-to-Voice Anfragen.
/// </summary>
public class TextToVoiceRequestDto
{
    /// <summary>
    /// Zu sprechender Text
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Sprache (z.B. "de-DE", "en-US")
    /// </summary>
    public required string Language { get; set; }

    /// <summary>
    /// Voice-ID (optional)
    /// </summary>
    public string? Voice { get; set; }
}
