using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Service für den Zugriff auf System-Prompts.
/// Cached alle Prompts im Arbeitsspeicher – nach einer Änderung über das CMS
/// wird der Cache automatisch invalidiert.
/// </summary>
public class SystemPromptService
{
    private readonly ISystemPromptRepository _repository;
    private readonly ILogger<SystemPromptService> _logger;

    private Dictionary<string, SystemPrompt>? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public SystemPromptService(
        ISystemPromptRepository repository,
        ILogger<SystemPromptService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Lesezugriff
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gibt den Prompt-Inhalt für den angegebenen Schlüssel zurück.
    /// Wirft eine Exception wenn der Schlüssel nicht gefunden wird oder inaktiv ist.
    /// </summary>
    public async Task<string> GetContentAsync(string key, CancellationToken cancellationToken = default)
    {
        var prompt = await GetPromptAsync(key, cancellationToken);
        if (prompt == null)
            throw new InvalidOperationException($"System-Prompt '{key}' nicht in Datenbank gefunden oder inaktiv.");
        return prompt.Content;
    }

    /// <summary>
    /// Gibt den Prompt-Inhalt zurück oder null wenn nicht gefunden / inaktiv.
    /// </summary>
    public async Task<string?> GetContentOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var prompt = await GetPromptAsync(key, cancellationToken);
        return prompt?.Content;
    }

    /// <summary>
    /// Gibt die Prompt-Entity zurück oder null wenn nicht gefunden / inaktiv.
    /// </summary>
    public async Task<SystemPrompt?> GetPromptAsync(string key, CancellationToken cancellationToken = default)
    {
        var cache = await GetCacheAsync(cancellationToken);
        cache.TryGetValue(key, out var prompt);
        return prompt?.IsActive == true ? prompt : null;
    }

    /// <summary>
    /// Gibt alle Prompts zurück (aktive und inaktive) — für das CMS.
    /// </summary>
    public async Task<List<SystemPrompt>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _repository.GetAllAsync(cancellationToken);

    // -------------------------------------------------------------------------
    // Schreibzugriff (invalidiert den Cache)
    // -------------------------------------------------------------------------

    public async Task<SystemPrompt> CreateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default)
    {
        var result = await _repository.CreateAsync(prompt, cancellationToken);
        InvalidateCache();
        return result;
    }

    public async Task<SystemPrompt?> UpdateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default)
    {
        var result = await _repository.UpdateAsync(prompt, cancellationToken);
        InvalidateCache();
        return result;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await _repository.DeleteAsync(id, cancellationToken);
        InvalidateCache();
        return result;
    }

    // -------------------------------------------------------------------------
    // Cache
    // -------------------------------------------------------------------------

    /// <summary>
    /// Invalidiert den In-Memory-Cache — der nächste Aufruf liest frisch aus der DB.
    /// </summary>
    public void InvalidateCache()
    {
        _cache = null;
        _logger.LogInformation("SystemPrompt-Cache invalidiert");
    }

    private async Task<Dictionary<string, SystemPrompt>> GetCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache != null) return _cache;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache != null) return _cache;
            var all = await _repository.GetAllAsync(cancellationToken);
            _cache = all.ToDictionary(p => p.Key, p => p);
            _logger.LogDebug("SystemPrompt-Cache befüllt mit {Count} Einträgen", _cache.Count);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
