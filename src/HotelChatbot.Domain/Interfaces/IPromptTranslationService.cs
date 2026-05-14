namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Übersetzt deutsche System-Prompts ins Englische.
/// LLM-Aufrufe mit englischen Prompts liefern zuverlässigere Ergebnisse.
/// </summary>
public interface IPromptTranslationService
{
    /// <summary>
    /// Übersetzt einen deutschen Prompt-Text ins Englische.
    /// Einfache Konfigurationswerte (Zahlen, Codes) werden unverändert zurückgegeben.
    /// </summary>
    Task<string> TranslateToEnglishAsync(string germanText, CancellationToken cancellationToken = default);
}
