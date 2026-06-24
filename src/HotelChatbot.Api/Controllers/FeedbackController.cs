using HotelChatbot.Api.Admin;
using HotelChatbot.Application.DTOs;
using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HotelChatbot.Api.Controllers;

[ApiController]
[Route("api/admin/feedback")]
[SwaggerTag("Admin: Feedback")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackRepository _repository;
    private readonly IHotelRepository _hotelRepository;

    public FeedbackController(IFeedbackRepository repository, IHotelRepository hotelRepository)
    {
        _repository = repository;
        _hotelRepository = hotelRepository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FeedbackDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        var hotels = await _hotelRepository.GetAllAsync(cancellationToken);
        var hotelNames = hotels.ToDictionary(h => h.HotelId, h => h.Name);

        return Ok(items.Select(f => ToDto(f, hotelNames)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(FeedbackDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateFeedbackDto dto, CancellationToken cancellationToken)
    {
        var validationError = await ValidateFeedbackFieldsAsync(dto.HotelId, dto.Name, dto.Keyword, dto.Text, cancellationToken);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var role = HttpContext.Items["AdminRole"] as string ?? AdminRoles.Hotel;

        var feedback = new Feedback
        {
            HotelId       = dto.HotelId.Trim(),
            Name          = dto.Name.Trim(),
            Keyword       = dto.Keyword.Trim(),
            Text          = dto.Text.Trim(),
            Status        = string.Empty,
            CreatedByRole = role,
        };

        var created = await _repository.CreateAsync(feedback, cancellationToken);
        var hotel = await _hotelRepository.GetByIdAsync(created.HotelId!, cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { id = created.Id }, ToDto(created, hotel?.Name));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(FeedbackDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFeedbackDto dto, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return NotFound(new { message = $"Feedback {id} nicht gefunden." });

        var validationError = await ValidateFeedbackFieldsAsync(dto.HotelId, dto.Name, dto.Keyword, dto.Text, cancellationToken);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        existing.HotelId = dto.HotelId.Trim();
        existing.Name = dto.Name.Trim();
        existing.Keyword = dto.Keyword.Trim();
        existing.Text = dto.Text.Trim();

        var updated = await _repository.UpdateAsync(existing, cancellationToken);
        if (updated is null)
            return NotFound(new { message = $"Feedback {id} nicht gefunden." });

        var hotel = await _hotelRepository.GetByIdAsync(updated.HotelId!, cancellationToken);
        return Ok(ToDto(updated, hotel?.Name));
    }

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(FeedbackDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateFeedbackStatusDto dto, CancellationToken cancellationToken)
    {
        var role = HttpContext.Items["AdminRole"] as string;
        if (role != AdminRoles.Admin)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Nur Admins dürfen den Status ändern." });

        var updated = await _repository.UpdateStatusAsync(id, dto.Status?.Trim() ?? string.Empty, cancellationToken);
        if (updated is null)
            return NotFound(new { message = $"Feedback {id} nicht gefunden." });

        var hotel = updated.HotelId is not null
            ? await _hotelRepository.GetByIdAsync(updated.HotelId, cancellationToken)
            : null;
        return Ok(ToDto(updated, hotel?.Name));
    }

    private async Task<string?> ValidateFeedbackFieldsAsync(
        string hotelId, string name, string keyword, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hotelId))
            return "Hotel muss ausgewählt werden.";
        if (string.IsNullOrWhiteSpace(name))
            return "Name Mitarbeiter/in darf nicht leer sein.";
        if (string.IsNullOrWhiteSpace(keyword))
            return "Stichwort darf nicht leer sein.";
        if (string.IsNullOrWhiteSpace(text))
            return "Text darf nicht leer sein.";

        var hotel = await _hotelRepository.GetByIdAsync(hotelId.Trim(), cancellationToken);
        if (hotel is null)
            return "Ausgewähltes Hotel wurde nicht gefunden.";

        return null;
    }

    private static FeedbackDto ToDto(Feedback f, IReadOnlyDictionary<string, string> hotelNames) => new()
    {
        Id            = f.Id,
        HotelId       = f.HotelId,
        HotelName     = f.HotelId is not null && hotelNames.TryGetValue(f.HotelId, out var name) ? name : null,
        Name          = f.Name,
        Keyword       = f.Keyword,
        Text          = f.Text,
        Status        = f.Status,
        CreatedByRole = f.CreatedByRole,
        CreatedAt     = f.CreatedAt,
        UpdatedAt     = f.UpdatedAt,
    };

    private static FeedbackDto ToDto(Feedback f, string? hotelName) => new()
    {
        Id            = f.Id,
        HotelId       = f.HotelId,
        HotelName     = hotelName,
        Name          = f.Name,
        Keyword       = f.Keyword,
        Text          = f.Text,
        Status        = f.Status,
        CreatedByRole = f.CreatedByRole,
        CreatedAt     = f.CreatedAt,
        UpdatedAt     = f.UpdatedAt,
    };
}
