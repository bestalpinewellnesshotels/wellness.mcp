using HotelChatbot.Application.DTOs;
using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HotelChatbot.Api.Controllers;

/// <summary>
/// CMS-Endpunkte für die Verwaltung von System-Prompts.
/// Ermöglicht das Lesen, Erstellen, Aktualisieren und Löschen aller LLM-Anweisungen.
/// </summary>
[ApiController]
[Route("api/admin/system-prompts")]
[SwaggerTag("CMS: System-Prompts verwalten")]
public class SystemPromptsController : ControllerBase
{
    private readonly SystemPromptService _service;
    private readonly IPromptTranslationService _translation;
    private readonly ILogger<SystemPromptsController> _logger;

    public SystemPromptsController(
        SystemPromptService service,
        IPromptTranslationService translation,
        ILogger<SystemPromptsController> logger)
    {
        _service = service;
        _translation = translation;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // GET /api/admin/system-prompts
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gibt alle System-Prompts zurück (aktive und inaktive).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SystemPromptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var prompts = await _service.GetAllAsync(cancellationToken);
        return Ok(prompts.Select(ToDto));
    }

    // -------------------------------------------------------------------------
    // GET /api/admin/system-prompts/{key}
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gibt einen einzelnen System-Prompt anhand seines Schlüssels zurück.
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(SystemPromptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByKey(string key, CancellationToken cancellationToken)
    {
        var prompt = await _service.GetPromptAsync(key, cancellationToken)
                     // GetPromptAsync filtert inaktive heraus – für CMS auch inaktive zeigen
                     ?? (await _service.GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Key == key);

        if (prompt == null)
            return NotFound(new { message = $"System-Prompt '{key}' nicht gefunden." });

        return Ok(ToDto(prompt));
    }

    // -------------------------------------------------------------------------
    // POST /api/admin/system-prompts
    // -------------------------------------------------------------------------

    /// <summary>
    /// Erstellt einen neuen System-Prompt.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SystemPromptDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSystemPromptDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Key) || dto.Key.Contains(' '))
            return BadRequest(new { message = "Key darf nicht leer sein und keine Leerzeichen enthalten." });

        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Content darf nicht leer sein." });

        // Doppelten Key prüfen
        var existing = (await _service.GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Key == dto.Key);
        if (existing != null)
            return Conflict(new { message = $"Ein System-Prompt mit Key '{dto.Key}' existiert bereits." });

        var prompt = new SystemPrompt
        {
            Key         = dto.Key.Trim(),
            Name        = dto.Name,
            Description = dto.Description,
            Content     = dto.Content,
            ContentDe   = dto.ContentDe,
            Language    = dto.Language,
            IsActive    = dto.IsActive,
        };

        var created = await _service.CreateAsync(prompt, cancellationToken);
        _logger.LogInformation("System-Prompt erstellt: Key={Key}", created.Key);

        return CreatedAtAction(nameof(GetByKey), new { key = created.Key }, ToDto(created));
    }

    // -------------------------------------------------------------------------
    // PUT /api/admin/system-prompts/{key}
    // -------------------------------------------------------------------------

    /// <summary>
    /// Aktualisiert einen bestehenden System-Prompt.
    /// Der Key kann nicht geändert werden.
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(SystemPromptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateSystemPromptDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Content darf nicht leer sein." });

        var all = await _service.GetAllAsync(cancellationToken);
        var existing = all.FirstOrDefault(p => p.Key == key);
        if (existing == null)
            return NotFound(new { message = $"System-Prompt '{key}' nicht gefunden." });

        existing.Name        = dto.Name;
        existing.Description = dto.Description;
        existing.Content     = dto.Content;
        existing.ContentDe   = dto.ContentDe;
        existing.Language    = dto.Language;
        existing.IsActive    = dto.IsActive;

        var updated = await _service.UpdateAsync(existing, cancellationToken);
        _logger.LogInformation("System-Prompt aktualisiert: Key={Key}", key);

        return Ok(ToDto(updated!));
    }

    // -------------------------------------------------------------------------
    // DELETE /api/admin/system-prompts/{id}
    // -------------------------------------------------------------------------

    /// <summary>
    /// Löscht einen System-Prompt anhand seiner ID.
    /// Standard-Prompts können gelöscht werden — beim nächsten Neustart werden sie neu angelegt.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound(new { message = $"System-Prompt mit ID {id} nicht gefunden." });

        _logger.LogInformation("System-Prompt gelöscht: Id={Id}", id);
        return NoContent();
    }

    // -------------------------------------------------------------------------
    // POST /api/admin/system-prompts/translate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Übersetzt einen deutschen Prompt-Text ins Englische.
    /// Wird vom Admin-CMS aufgerufen, bevor ein Datensatz gespeichert wird.
    /// </summary>
    [HttpPost("translate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Translate([FromBody] TranslateRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { message = "Text darf nicht leer sein." });

        _logger.LogInformation("Translation requested: {Preview}... ({Length} chars)", 
            dto.Text.Length > 100 ? dto.Text.Substring(0, 100) : dto.Text, 
            dto.Text.Length);

        var translated = await _translation.TranslateToEnglishAsync(dto.Text, cancellationToken);
        
        _logger.LogInformation("Translation result: {Preview}... ({Length} chars)", 
            translated.Length > 100 ? translated.Substring(0, 100) : translated, 
            translated.Length);

        return Ok(new { translation = translated });
    }

    // -------------------------------------------------------------------------
    // POST /api/admin/system-prompts/cache/invalidate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Invalidiert den In-Memory-Cache manuell.
    /// Normalerweise passiert das automatisch nach jeder Änderung.
    /// </summary>
    [HttpPost("cache/invalidate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult InvalidateCache()
    {
        _service.InvalidateCache();
        return Ok(new { message = "Cache erfolgreich invalidiert." });
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    private static SystemPromptDto ToDto(SystemPrompt p) => new()
    {
        Id          = p.Id,
        Key         = p.Key,
        Name        = p.Name,
        Description = p.Description,
        Content     = p.Content,
        ContentDe   = p.ContentDe,
        Language    = p.Language,
        IsActive    = p.IsActive,
        CreatedAt   = p.CreatedAt,
        UpdatedAt   = p.UpdatedAt,
    };
}

/// <summary>Request-Body für den Übersetzungs-Endpunkt.</summary>
public record TranslateRequestDto(string Text);
