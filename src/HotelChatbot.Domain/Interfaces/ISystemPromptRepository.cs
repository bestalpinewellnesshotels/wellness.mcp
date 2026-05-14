using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Repository-Interface für System-Prompts.
/// Enthält CRUD-Operationen und eine Methode zum Abrufen per Schlüssel.
/// </summary>
public interface ISystemPromptRepository
{
    /// <summary>
    /// Liefert alle System-Prompts (aktive und inaktive).
    /// </summary>
    Task<List<SystemPrompt>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Liefert einen System-Prompt anhand seines eindeutigen Schlüssels.
    /// </summary>
    Task<SystemPrompt?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liefert einen System-Prompt anhand seiner Datenbank-ID.
    /// </summary>
    Task<SystemPrompt?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erstellt einen neuen System-Prompt.
    /// </summary>
    Task<SystemPrompt> CreateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualisiert einen bestehenden System-Prompt.
    /// </summary>
    Task<SystemPrompt?> UpdateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Löscht einen System-Prompt anhand seiner ID.
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
