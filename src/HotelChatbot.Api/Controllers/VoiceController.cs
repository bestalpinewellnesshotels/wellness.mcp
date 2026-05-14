using HotelChatbot.Application.DTOs;
using HotelChatbot.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelChatbot.Api.Controllers;

/// <summary>
/// Voice-Controller für Speech-to-Text und Text-to-Speech.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly VoiceService _voiceService;
    private readonly ChatService _chatService;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        VoiceService voiceService,
        ChatService chatService,
        ILogger<VoiceController> logger)
    {
        _voiceService = voiceService;
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Speech-to-Text: Konvertiert Audio in Text und verarbeitet die Anfrage.
    /// </summary>
    [HttpPost("transcribe")]
    [ProducesResponseType(typeof(HotelRecommendationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HotelRecommendationResponseDto>> TranscribeAndProcess(
        [FromForm] VoiceRequestDto voiceRequest,
        IFormFile audioFile,
        CancellationToken cancellationToken)
    {
        try
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return BadRequest(new { error = "Audio-Datei ist erforderlich" });
            }

            _logger.LogInformation("Voice Request: FileSize={Size}", audioFile.Length);

            using var audioStream = audioFile.OpenReadStream();
            var (transcribedText, detectedLanguage) = await _voiceService.TranscribeAudioAsync(
                audioStream,
                voiceRequest.Language,
                cancellationToken);

            _logger.LogInformation("Transkription: {Text}", transcribedText);

            var pipelineRequest = new HotelRecommendationRequestDto
            {
                Requirements = transcribedText,
                Language     = detectedLanguage,
                SessionId    = voiceRequest.SessionId
            };

            // Pipeline macht mehrere OpenAI-Calls sequentiell – 120s Timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            var response = await _chatService.ProcessHotelRecommendationAsync(pipelineRequest, cts.Token);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Voice-Verarbeitung");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Text-to-Speech: Konvertiert Text in Audio.
    /// </summary>
    [HttpPost("synthesize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SynthesizeSpeech(
        [FromBody] SynthesizeRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { error = "Text ist erforderlich" });
            }

            var audioStream = await _voiceService.SynthesizeSpeechAsync(
                request.Text,
                request.Language ?? "de-DE",
                request.Voice,
                cancellationToken);

            return File(audioStream, "audio/mpeg", "speech.mp3");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei TTS");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }
}

/// <summary>
/// DTO für Text-to-Speech Anfrage.
/// </summary>
public class SynthesizeRequestDto
{
    public required string Text { get; set; }
    public string? Language { get; set; }
    public string? Voice { get; set; }
}
