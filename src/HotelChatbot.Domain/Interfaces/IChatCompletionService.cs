namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Chat-Completion mit LLM (z.B. OpenAI, Azure OpenAI).
/// Alle API-Keys werden serverseitig verwaltet.
/// Language/Ethical/Intent laufen über lokale Klassifizierer (ILanguageDetector, IEthicalClassifier, IIntentClassifier).
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Generiert eine Antwort basierend auf dem Kontext und der User-Query.
    /// </summary>
    Task<string> GenerateResponseAsync(
        string systemPrompt,
        string context,
        string userQuery,
        List<(string Role, string Content)> conversationHistory,
        string? targetLanguage = null,
        string? userMessageTemplate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SearchAgent: Übersetzt die Anfrage ins Deutsche für die Vektordatenbank-Suche.
    /// </summary>
    Task<string> TranslateToGermanAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RelevanceAgent: Prüft ob die Vektordatenbank-Ergebnisse die Benutzeranfrage inhaltlich beantworten können.
    /// </summary>
    Task<bool> IsContextRelevantAsync(
        string query,
        string context,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);
}
