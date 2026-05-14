namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Voice-to-Text Request.
/// </summary>
public class VoiceRequestDto
{
    /// <summary>
    /// Hotel-ID
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Session-ID
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Audio-Format (z.B. "audio/wav", "audio/webm")
    /// </summary>
    public string? AudioFormat { get; set; }

    /// <summary>
    /// Sprache (optional, für bessere STT-Ergebnisse)
    /// </summary>
    public string? Language { get; set; }
}
