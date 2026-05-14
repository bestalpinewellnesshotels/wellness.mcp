using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Text-to-Speech Service mit ElevenLabs.
/// </summary>
public class TextToSpeechService : ITextToSpeechService
{
    private readonly ILogger<TextToSpeechService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _voiceId;
    private readonly string _baseUrl;

    public TextToSpeechService(
        IConfiguration configuration,
        ILogger<TextToSpeechService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        _apiKey = configuration["ElevenLabs:ApiKey"]
            ?? throw new InvalidOperationException("ElevenLabs:ApiKey nicht konfiguriert");
        _voiceId = configuration["ElevenLabs:VoiceId"]
            ?? throw new InvalidOperationException("ElevenLabs:VoiceId nicht konfiguriert");
        _baseUrl = configuration["ElevenLabs:BaseUrl"]
            ?? "https://api.elevenlabs.io/v1/text-to-speech/";

        // HTTP Client konfigurieren
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("xi-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
    }

    public async Task<Stream> SynthesizeAsync(
        string text,
        string language,
        string? voice = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("TTS Service: Generiere Audio für Text (Länge: {Length})", text.Length);

            var url = $"{_baseUrl}{voice ?? _voiceId}/stream";

            var payload = new
            {
                model_id = "eleven_turbo_v2_5",
                voice_settings = new
                {
                    stability = 0.3,
                    similarity_boost = 0.95,
                    style = 0.5,
                    use_speaker_boost = true
                },
                text = text,
                use_audio_tags = true
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                _logger.LogInformation("TTS erfolgreich: {Size} bytes", audioBytes.Length);
                return new MemoryStream(audioBytes);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("TTS Fehler: {Status} - {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"ElevenLabs API Error: {response.StatusCode} - {errorBody}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Text-to-Speech");
            throw;
        }
    }
}
