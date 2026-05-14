namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Protokolliert System-Prompt-Aufrufe und deren Ergebnisse in tagesweise rotierende .txt-Dateien
/// unter dem Verzeichnis 'log/' (auf gleicher Ebene wie 'wwwroot/').
/// </summary>
public interface IQueryLogger
{
    /// <summary>
    /// Protokolliert die Intent-Klassifikation (intent.hotel_wellness).
    /// Wird bei jeder Empfehlungsanfrage aufgerufen – egal ob JA oder NEIN.
    /// Bei NEIN (= out_of_scope) wird der Eintrag entsprechend markiert.
    /// </summary>
    Task LogIntentCheckAsync(
        string query,
        string language,
        string promptKey,
        string? promptContent,
        bool isHotelQuery);

    /// <summary>
    /// Protokolliert die Hotel-Bewertungsphase wenn kein Hotel als passend eingestuft wurde.
    /// Enthält für jedes bewertete Hotel den vollständigen Prompt, den Prompt-Key
    /// und das Ergebnis (PASST:JA / PASST:NEIN + Begründung).
    /// </summary>
    Task LogNoMatchEvaluationAsync(
        string query,
        string language,
        string evalPromptKey,
        string? evalPromptContent,
        IList<(string HotelName, bool IsMatch, string EvaluationText)> evaluations);
}
