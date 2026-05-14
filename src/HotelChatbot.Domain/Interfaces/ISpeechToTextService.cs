namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Speech-to-Text Service.
/// Konvertiert Audio-Streams in Text.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>
    /// Konvertiert einen Audio-Stream in Text.
    /// </summary>
    /// <param name="audioStream">Audio-Daten (z.B. WAV, MP3)</param>
    /// <param name="language">Sprache des Audios (optional, falls Auto-Detection nicht verfügbar)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Transkribierter Text und erkannte Sprache</returns>
    Task<(string Text, string? DetectedLanguage)> TranscribeAsync(
        Stream audioStream,
        string? language = null,
        CancellationToken cancellationToken = default);
}
