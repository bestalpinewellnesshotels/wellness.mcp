namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Voice-to-Text Anfragen.
/// </summary>
public class VoiceToTextRequestDto
{
    /// <summary>
    /// Hotel-ID
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Session-ID (optional)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Audio-Daten als Base64
    /// </summary>
    public required string AudioDataBase64 { get; set; }

    /// <summary>
    /// Audio-Format (z.B. "audio/webm", "audio/wav")
    /// </summary>
    public required string AudioFormat { get; set; }

    /// <summary>
    /// Sprache (optional, für bessere Erkennung)
    /// </summary>
    public string? Language { get; set; }
}
