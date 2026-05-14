using HotelChatbot.Domain.Interfaces;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Speech-to-Text Service mit Azure Cognitive Services.
/// </summary>
public class SpeechToTextService : ISpeechToTextService
{
    private readonly ILogger<SpeechToTextService> _logger;
    private readonly SpeechConfig _speechConfig;

    public SpeechToTextService(
        IConfiguration configuration,
        ILogger<SpeechToTextService> logger)
    {
        _logger = logger;

        var subscriptionKey = configuration["SpeechService:SubscriptionKey"]
            ?? throw new InvalidOperationException("SpeechService:SubscriptionKey nicht konfiguriert");
        var serviceRegion = configuration["SpeechService:ServiceRegion"]
            ?? throw new InvalidOperationException("SpeechService:ServiceRegion nicht konfiguriert");

        _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, serviceRegion);
        
        // Auto-Spracherkennung aktivieren
        _speechConfig.SpeechRecognitionLanguage = "de-DE";
    }

    public async Task<(string Text, string? DetectedLanguage)> TranscribeAsync(
        Stream audioStream,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("STT Service: Starte Transkription");

            // Sprache setzen falls angegeben
            if (!string.IsNullOrEmpty(language))
            {
                _speechConfig.SpeechRecognitionLanguage = language;
            }

            // Audio-Stream für Azure Speech vorbereiten
            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Temporäre WAV-Datei erstellen (Azure Speech benötigt Dateipfad)
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempFile, memoryStream.ToArray(), cancellationToken);

                // Audio-Input konfigurieren
                using var audioConfig = AudioConfig.FromWavFileInput(tempFile);
                using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

                // Transkription durchführen
                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    _logger.LogInformation("STT erfolgreich: {Text}", result.Text);
                    var detectedLang = result.Properties.GetProperty(PropertyId.SpeechServiceConnection_AutoDetectSourceLanguageResult);
                    return (result.Text, detectedLang ?? language);
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogWarning("STT: Keine Sprache erkannt");
                    return (string.Empty, language);
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    _logger.LogError("STT Fehler: {Reason} - {ErrorDetails}", 
                        cancellation.Reason, cancellation.ErrorDetails);
                    throw new Exception($"Speech recognition cancelled: {cancellation.ErrorDetails}");
                }

                return (string.Empty, language);
            }
            finally
            {
                // Temporäre Datei aufräumen
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Speech-to-Text");
            throw;
        }
    }
}
