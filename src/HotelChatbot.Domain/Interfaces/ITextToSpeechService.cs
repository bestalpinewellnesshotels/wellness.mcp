namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Text-to-Speech Service.
/// Konvertiert Text in Audio.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Konvertiert Text in einen Audio-Stream.
    /// </summary>
    /// <param name="text">Zu sprechender Text</param>
    /// <param name="language">Sprache (z.B. "de-DE", "en-US")</param>
    /// <param name="voice">Stimme (optional, verwendet Standard wenn nicht angegeben)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Audio-Stream (z.B. MP3)</returns>
    Task<Stream> SynthesizeAsync(
        string text,
        string language,
        string? voice = null,
        CancellationToken cancellationToken = default);
}
