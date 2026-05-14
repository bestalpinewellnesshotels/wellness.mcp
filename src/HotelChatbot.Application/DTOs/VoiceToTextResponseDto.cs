namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Voice-to-Text Antworten.
/// </summary>
public class VoiceToTextResponseDto
{
    /// <summary>
    /// Transkribierter Text
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Erkannte Sprache
    /// </summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>
    /// Confidence Score (0.0 - 1.0)
    /// </summary>
    public double? ConfidenceScore { get; set; }
}
