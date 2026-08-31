namespace HotelChatbot.Domain.Language;

/// <summary>Einzelne Sprache mit Softmax-Wahrscheinlichkeit des Klassifizierers.</summary>
public sealed record LanguageScore(string Code, string Name, double Probability);

/// <summary>
/// Vollständiges Klassifizierer-Ergebnis (nicht nur der Gewinner).
/// Die Gesprächsregeln liegen in der Application-Schicht.
/// </summary>
public sealed class LanguageDetectionDetails
{
    public string? TopCode { get; init; }
    public double Confidence { get; init; }
    public double Coverage { get; init; }
    public bool IsUnknown { get; init; }
    public bool IsAmbiguous { get; init; }
    public bool IsUnrecognizedScript { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<LanguageScore> Ranked { get; init; } = [];

    public static LanguageDetectionDetails Empty(string reason) => new()
    {
        IsUnknown = true,
        Reason = reason
    };
}
