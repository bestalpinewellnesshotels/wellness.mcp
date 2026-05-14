using HotelChatbot.Application.DTOs;
using HotelChatbot.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelChatbot.Api.Controllers;

/// <summary>
/// Chat-Controller für Text-basierte Konversationen.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ChatService chatService,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Empfiehlt Hotels basierend auf Nutzeranforderungen (RAG über alle Hotels).
    /// </summary>
    /// <param name="request">Hotel Recommendation Request DTO</param>
    /// <returns>Hotel Recommendation Response mit Empfehlungen</returns>
    [HttpPost("recommend")]
    [ProducesResponseType(typeof(HotelRecommendationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<HotelRecommendationResponseDto>> RecommendHotels(
        [FromBody] HotelRecommendationRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Requirements))
            {
                return BadRequest(new { error = "Requirements ist erforderlich" });
            }

            _logger.LogInformation(
                "Hotel Recommendation Request: Requirements={Requirements}, MaxResults={MaxResults}",
                request.Requirements, request.MaxResults);

            // Pipeline macht mehrere OpenAI-Calls sequentiell – 120s Timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            var response = await _chatService.ProcessHotelRecommendationAsync(request, cts.Token);

            if (!response.Success && !string.IsNullOrEmpty(response.ErrorMessage))
            {
                return BadRequest(new { error = response.ErrorMessage });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Hotel-Empfehlung");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Health Check Endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
