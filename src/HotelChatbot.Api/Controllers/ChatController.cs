using System.Text.Json;
using System.Text.Json.Serialization;
using HotelChatbot.Application.Diagnostics;
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
    private static readonly JsonSerializerOptions SseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
    /// Recommend mit Live-Pipeline-Trace (SSE) für die ChatGPT-Simulations-UI.
    /// </summary>
    [HttpPost("recommend/stream")]
    public async Task RecommendHotelsStream(
        [FromBody] HotelRecommendationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Requirements))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "Requirements ist erforderlich" }, cancellationToken);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        await WriteSseStreamAsync(
            async (emit, ct) =>
            {
                var collector = new PipelineTraceCollector(step => emit("step", step));
                var response = await _chatService.ProcessHotelRecommendationAsync(request, ct, collector);
                await emit("result", response);
                await emit("done", new { totalMs = collector.ElapsedMs, tool = "get_response" });
            },
            cts.Token);
    }

    /// <summary>
    /// Read-only Detailfrage zu einem Hotel anhand stabiler Hotel-ID.
    /// </summary>
    [HttpPost("hotel-details")]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatResponseDto>> GetHotelDetails(
        [FromBody] ChatRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.HotelId) || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "hotelId and message (question) are required" });
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var response = await _chatService.ProcessHotelDetailsAsync(request, cts.Token);

            if (response.ErrorMessage == "hotel_not_found")
                return NotFound(new { error = response.FinalAnswer ?? response.Message });

            if (!response.Success && response.ResponseType == "error")
                return BadRequest(new { error = response.FinalAnswer ?? response.Message });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Hotel-Details");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Hotel-Details mit Live-Pipeline-Trace (SSE) für die ChatGPT-Simulations-UI.
    /// </summary>
    [HttpPost("hotel-details/stream")]
    public async Task GetHotelDetailsStream(
        [FromBody] ChatRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.HotelId) || string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "hotelId and message (question) are required" }, cancellationToken);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        await WriteSseStreamAsync(
            async (emit, ct) =>
            {
                var collector = new PipelineTraceCollector(step => emit("step", step));
                var response = await _chatService.ProcessHotelDetailsAsync(request, ct, collector);
                await emit("result", response);
                await emit("done", new { totalMs = collector.ElapsedMs, tool = "get_hotel_details" });
            },
            cts.Token);
    }

    /// <summary>
    /// Health Check Endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok" });
    }

    private async Task WriteSseStreamAsync(
        Func<Func<string, object, Task>, CancellationToken, Task> body,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        async Task Emit(string eventName, object payload)
        {
            var json = JsonSerializer.Serialize(payload, SseJson);
            await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            await body(Emit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSE-Stream fehlgeschlagen");
            try
            {
                await Emit("error", new { error = "Interner Serverfehler", message = ex.Message });
            }
            catch
            {
                // ignore write failures after disconnect
            }
        }
    }
}
