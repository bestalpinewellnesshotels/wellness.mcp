using HotelChatbot.Application.DTOs;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Service für Voice-Funktionalität (Speech-to-Text).
/// </summary>
public class VoiceService
{
    private readonly ISpeechToTextService _sttService;
    private readonly ILogger<VoiceService> _logger;

    public VoiceService(
        ISpeechToTextService sttService,
        ILogger<VoiceService> logger)
    {
        _sttService = sttService;
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
}
