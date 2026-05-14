using HotelChatbot.Application.DTOs;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Service für Voice-Funktionalität (Speech-to-Text und Text-to-Speech).
/// </summary>
public class VoiceService
{
    private readonly ISpeechToTextService _sttService;
    private readonly ITextToSpeechService _ttsService;
    private readonly ILogger<VoiceService> _logger;

    public VoiceService(
        ISpeechToTextService sttService,
        ITextToSpeechService ttsService,
        ILogger<VoiceService> logger)
    {
        _sttService = sttService;
        _ttsService = ttsService;
        _logger = logger;
    }

    /// <summary>
    /// Konvertiert Audio in Text (Speech-to-Text).
    /// </summary>
    public async Task<(string Text, string? Language)> TranscribeAudioAsync(
        Stream audioStream,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starte Audio-Transkription");
            var result = await _sttService.TranscribeAsync(audioStream, language, cancellationToken);
            _logger.LogInformation("Audio-Transkription erfolgreich: {Text}", result.Text);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Audio-Transkription");
            throw;
        }
    }

    /// <summary>
    /// Konvertiert Text in Audio (Text-to-Speech).
    /// </summary>
    public async Task<Stream> SynthesizeSpeechAsync(
        string text,
        string language,
        string? voice = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starte Text-to-Speech für Text: {Text}", text);
            var audioStream = await _ttsService.SynthesizeAsync(text, language, voice, cancellationToken);
            _logger.LogInformation("Text-to-Speech erfolgreich");
            return audioStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Text-to-Speech");
            throw;
        }
    }
}
