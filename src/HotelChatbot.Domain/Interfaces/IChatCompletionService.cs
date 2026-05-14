namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Chat-Completion mit LLM (z.B. OpenAI, Azure OpenAI).
/// Alle API-Keys werden serverseitig verwaltet.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Generiert eine Antwort basierend auf dem Kontext und der User-Query.
    /// </summary>
    /// <param name="systemPrompt">System-Prompt mit Anweisungen</param>
    /// <param name="context">Retrieval-Kontext aus dem Vector Store</param>
    /// <param name="userQuery">Frage des Users</param>
    /// <param name="conversationHistory">Bisheriger Gesprächsverlauf</param>
    /// <param name="targetLanguage">Zielsprache für die Antwort</param>
    /// <param name="userMessageTemplate">
    /// Optionales Template für die Benutzernachricht. Platzhalter {context} und {userQuery}
    /// werden zur Laufzeit ersetzt. Wenn null, wird der eingebaute Standard verwendet.
    /// </param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Generierte Antwort</returns>
    Task<string> GenerateResponseAsync(
        string systemPrompt,
        string context,
        string userQuery,
        List<(string Role, string Content)> conversationHistory,
        string? targetLanguage = null,
        string? userMessageTemplate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// LogicAgent: Klassifiziert semantisch ob eine Anfrage hotel/urlaubs/wellness-relevant ist.
    /// </summary>
    Task<bool> IsHotelWellnessQueryAsync(
        string query,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// LanguageAgent: Erkennt die Sprache der Nutzeranfrage und gibt den ISO-639-1 Code zurück (z.B. "de", "en").
    /// </summary>
    Task<string> DetectLanguageAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// EthicalAgent: Prüft ob die Nachricht ethisch vertretbar ist (höflich, nicht diskriminierend).
    /// Gibt true zurück wenn OK, false wenn REJECT.
    /// </summary>
    Task<bool> IsEthicalAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SearchAgent: Übersetzt die Anfrage ins Deutsche für die Vektordatenbank-Suche.
    /// Gibt den originalen Text zurück wenn bereits auf Deutsch.
    /// </summary>
    Task<string> TranslateToGermanAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RelevanceAgent: Prüft ob die Vektordatenbank-Ergebnisse die Benutzeranfrage inhaltlich beantworten können.
    /// Gibt true zurück wenn YES (Kontext ist relevant), false wenn NO (Kontext enthält keine passende Antwort).
    /// </summary>
    Task<bool> IsContextRelevantAsync(
        string query,
        string context,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);
}
