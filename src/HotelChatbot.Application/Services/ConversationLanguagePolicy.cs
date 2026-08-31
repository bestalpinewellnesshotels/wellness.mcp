using System.Text.Json;
using HotelChatbot.Domain.Language;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Session-Zustand der Sprachwahl (erste eindeutige Sprache, offene Nachfrage).
/// </summary>
public sealed class ConversationLanguageState
{
    public string? LockedLanguage { get; set; }
    public bool FirstTurnUnambiguous { get; set; }
    public List<string>? PendingCodes { get; set; }
    public string? PendingQuery { get; set; }
}

/// <summary>Ergebnis der Sprachregeln für eine User-Eingabe.</summary>
public sealed class ConversationLanguageDecision
{
    public required string Language { get; init; }
    public bool NeedsClarification { get; init; }
    public string? ClarificationMessage { get; init; }
    public required ConversationLanguageState State { get; init; }
    public required string Source { get; init; }
    public required string TextToProcess { get; init; }
}

/// <summary>
/// Gesprächsregeln für die lokale Spracherkennung.
/// Der Klassifizierer läuft nach jeder Eingabe; die Session hält die erste eindeutige Sprache.
/// </summary>
public static class ConversationLanguagePolicy
{
    public const string FallbackLanguage = "de";
    public const string UnrecognizedLanguage = "en";
    public const string SessionPrefix = "__conversation_language__:";

    internal const double MinCandidateProbability = 0.10;
    internal const double CloseAbsoluteMargin = 0.15;
    internal const double CloseRelativeRatio = 0.50;
    internal const double StickyMinProbability = 0.12;
    internal const int MaxClarificationLanguages = 4;

    private static readonly string[] HotelCues =
    [
        "hotel", "hotels", "wellness", "spa", "sauna", "pool", "zimmer", "room",
        "ski", "piste", "urlaub", "vacation", "holiday", "resort"
    ];

    private static readonly string[] PolyglotCodes = ["de", "en", "nl"];

    private static readonly HashSet<string> InternationalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hotel", "hotels", "hôtel", "spa", "wellness", "pool", "sauna",
        "salzburg", "tirol", "tyrol", "kärnten", "karnten", "carinthia",
        "steiermark", "vorarlberg", "wien", "vienna", "bayern", "bavaria",
        "in", "mit", "with", "und", "and", "the", "a", "an", "bei", "near",
        "adults", "only"
    };

    public static ConversationLanguageState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ConversationLanguageState>(json);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(ConversationLanguageState state) =>
        JsonSerializer.Serialize(state);

    public static ConversationLanguageDecision Resolve(
        LanguageDetectionDetails detection,
        ConversationLanguageState? existing,
        string userText)
    {
        var state = Clone(existing);
        var text = userText ?? string.Empty;

        if (state.PendingCodes is { Count: > 0 })
            return ResolvePending(detection, state, text);

        if (detection.IsUnrecognizedScript)
            return KeepGoing(UnrecognizedLanguage, state, text, "unrecognized_en", lockLanguage: false);

        if (HasLock(state))
            return ResolveFollowUp(detection, state, text);

        return ResolveFirstTurn(detection, state, text);
    }

    public static IReadOnlyList<LanguageScore> CloseLanguages(IReadOnlyList<LanguageScore> ranked)
    {
        if (ranked.Count == 0)
            return [];

        var top = ranked[0].Probability;
        return ranked
            .Where(score => score.Probability >= MinCandidateProbability)
            .Where(score =>
                top - score.Probability <= CloseAbsoluteMargin ||
                score.Probability >= top * CloseRelativeRatio)
            .Take(MaxClarificationLanguages)
            .ToList();
    }

    private static ConversationLanguageDecision ResolveFirstTurn(
        LanguageDetectionDetails detection,
        ConversationLanguageState state,
        string text)
    {
        if (IsSharedVocabularyPhrase(text))
            return Ask(state, text, PolyglotCandidates(detection));

        var close = CloseLanguages(detection.Ranked);
        if (close.Count >= 2)
            return Ask(state, text, close);

        if (!detection.IsUnknown &&
            !detection.IsAmbiguous &&
            !string.IsNullOrWhiteSpace(detection.TopCode))
        {
            state.LockedLanguage = detection.TopCode;
            state.FirstTurnUnambiguous = true;
            return KeepGoing(detection.TopCode, state, text, "detected", lockLanguage: true);
        }

        return KeepGoing(FallbackLanguage, state, text, "fallback_de", lockLanguage: false);
    }

    private static ConversationLanguageDecision ResolveFollowUp(
        LanguageDetectionDetails detection,
        ConversationLanguageState state,
        string text)
    {
        var locked = state.LockedLanguage!;

        if (detection.IsUnrecognizedScript)
            return KeepGoing(UnrecognizedLanguage, state, text, "unrecognized_en", lockLanguage: false);

        if (IsSharedVocabularyPhrase(text) || IsStickyHigh(detection, locked))
            return KeepGoing(locked, state, text, "sticky", lockLanguage: true);

        if (!detection.IsUnknown &&
            !string.IsNullOrWhiteSpace(detection.TopCode) &&
            !detection.TopCode.Equals(locked, StringComparison.OrdinalIgnoreCase))
        {
            state.LockedLanguage = detection.TopCode;
            state.FirstTurnUnambiguous = true;
            return KeepGoing(detection.TopCode, state, text, "switched", lockLanguage: true);
        }

        if (!string.IsNullOrWhiteSpace(detection.TopCode) && !detection.IsUnknown)
            return KeepGoing(detection.TopCode, state, text, "detected", lockLanguage: true);

        return KeepGoing(locked, state, text, "sticky", lockLanguage: true);
    }

    private static ConversationLanguageDecision ResolvePending(
        LanguageDetectionDetails detection,
        ConversationLanguageState state,
        string text)
    {
        var pending = state.PendingCodes!;
        var pendingQuery = state.PendingQuery;

        if (detection.IsUnrecognizedScript)
        {
            ClearPending(state);
            return KeepGoing(UnrecognizedLanguage, state, pendingQuery ?? text, "unrecognized_en", lockLanguage: false);
        }

        var named = LanguageClarification.TryMatchChoice(text, pending);
        if (named != null)
        {
            var query = LooksLikeHotelQuery(text) ? text : (pendingQuery ?? text);
            Lock(state, named, firstTurnUnambiguous: false);
            return KeepGoing(named, state, query, "choice", lockLanguage: true);
        }

        var close = CloseLanguages(detection.Ranked)
            .Where(score => pending.Contains(score.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (!detection.IsUnknown &&
            !detection.IsAmbiguous &&
            detection.TopCode != null &&
            pending.Contains(detection.TopCode, StringComparer.OrdinalIgnoreCase))
        {
            Lock(state, detection.TopCode, firstTurnUnambiguous: false);
            return KeepGoing(detection.TopCode, state, text, "detected", lockLanguage: true);
        }

        if (close.Count == 1)
        {
            Lock(state, close[0].Code, firstTurnUnambiguous: false);
            return KeepGoing(close[0].Code, state, text, "detected", lockLanguage: true);
        }

        if (close.Count >= 2)
        {
            state.PendingCodes = close.Select(score => score.Code).ToList();
            return Ask(state, pendingQuery ?? text, close);
        }

        var remaining = detection.Ranked
            .Where(s => pending.Contains(s.Code, StringComparer.OrdinalIgnoreCase))
            .Take(MaxClarificationLanguages)
            .ToList();
        if (remaining.Count == 0)
        {
            remaining = detection.Ranked
                .Take(Math.Min(Math.Max(pending.Count, 1), MaxClarificationLanguages))
                .ToList();
        }

        return Ask(state, pendingQuery ?? text, remaining);
    }

    private static ConversationLanguageDecision Ask(
        ConversationLanguageState state,
        string pendingQuery,
        IReadOnlyList<LanguageScore> close)
    {
        if (close.Count == 0)
            return KeepGoing(FallbackLanguage, state, pendingQuery, "fallback_de", lockLanguage: false);

        state.PendingCodes = close.Select(score => score.Code).ToList();
        state.PendingQuery = pendingQuery;
        state.LockedLanguage = null;
        state.FirstTurnUnambiguous = false;
        return new ConversationLanguageDecision
        {
            Language = close[0].Code,
            NeedsClarification = true,
            ClarificationMessage = LanguageClarification.Build(close),
            State = state,
            Source = "clarify",
            TextToProcess = pendingQuery
        };
    }

    private static ConversationLanguageDecision KeepGoing(
        string language,
        ConversationLanguageState state,
        string text,
        string source,
        bool lockLanguage)
    {
        if (lockLanguage)
            state.LockedLanguage = language;
        ClearPending(state);
        return new ConversationLanguageDecision
        {
            Language = language,
            NeedsClarification = false,
            ClarificationMessage = null,
            State = state,
            Source = source,
            TextToProcess = text
        };
    }

    private static bool IsStickyHigh(LanguageDetectionDetails detection, string locked)
    {
        var score = detection.Ranked.FirstOrDefault(item =>
            item.Code.Equals(locked, StringComparison.OrdinalIgnoreCase));
        if (score is null)
            return false;
        if (score.Probability >= StickyMinProbability)
            return true;
        return CloseLanguages(detection.Ranked)
            .Any(item => item.Code.Equals(locked, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasLock(ConversationLanguageState state) =>
        !string.IsNullOrWhiteSpace(state.LockedLanguage);

    private static void Lock(ConversationLanguageState state, string code, bool firstTurnUnambiguous)
    {
        state.LockedLanguage = code;
        state.FirstTurnUnambiguous = firstTurnUnambiguous;
        ClearPending(state);
    }

    private static void ClearPending(ConversationLanguageState state)
    {
        state.PendingCodes = null;
        state.PendingQuery = null;
    }

    private static ConversationLanguageState Clone(ConversationLanguageState? existing) =>
        existing is null
            ? new ConversationLanguageState()
            : new ConversationLanguageState
            {
                LockedLanguage = existing.LockedLanguage,
                FirstTurnUnambiguous = existing.FirstTurnUnambiguous,
                PendingCodes = existing.PendingCodes?.ToList(),
                PendingQuery = existing.PendingQuery
            };

    internal static bool IsSharedVocabularyPhrase(string text)
    {
        var words = ExtractWords(text);
        if (words.Count is < 1 or > 6)
            return false;
        if (!words.Any(w =>
                w.StartsWith("hotel", StringComparison.OrdinalIgnoreCase) ||
                w.Equals("spa", StringComparison.OrdinalIgnoreCase) ||
                w.Equals("wellness", StringComparison.OrdinalIgnoreCase)))
            return false;
        return words.All(w => InternationalWords.Contains(w));
    }

    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return words;
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetter(ch) || ch is 'ä' or 'ö' or 'ü' or 'ß')
            {
                buffer.Append(ch);
                continue;
            }

            if (buffer.Length > 0)
            {
                words.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            words.Add(buffer.ToString());
        return words;
    }

    private static IReadOnlyList<LanguageScore> PolyglotCandidates(LanguageDetectionDetails detection)
    {
        var byCode = detection.Ranked.ToDictionary(
            score => score.Code, StringComparer.OrdinalIgnoreCase);
        var list = new List<LanguageScore>();
        foreach (var code in PolyglotCodes)
        {
            if (byCode.TryGetValue(code, out var score))
                list.Add(score);
            else
                list.Add(new LanguageScore(code, code, 1.0 / PolyglotCodes.Length));
        }

        return list;
    }

    private static bool LooksLikeHotelQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var lower = text.ToLowerInvariant();
        return HotelCues.Any(cue => lower.Contains(cue, StringComparison.Ordinal));
    }
}
