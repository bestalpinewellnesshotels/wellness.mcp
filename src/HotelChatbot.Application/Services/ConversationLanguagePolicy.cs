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
/// ChatGPT hat keine zuverlässige Sprachauswahl-UI — bei Mehrdeutigkeit wird nicht nachgefragt.
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

    /// <summary>Sprachen, denen der n-Gramm-Classifier auf Hotelphrasen vertrauen darf.</summary>
    private static readonly HashSet<string> CoreLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "en", "nl", "it", "fr"
    };

    /// <summary>
    /// Wörter, die in EN/NL nicht vorkommen und die Phrase als Deutsch markieren.
    /// Nicht in <see cref="InternationalWords"/> — sonst würde z. B. „Hotels mit Spa“ als Polyglot gelten.
    /// </summary>
    private static readonly HashSet<string> GermanOnlyMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mit", "und", "bei", "für", "fur", "auch", "nicht", "im",
        "einen", "einem", "einer", "ich", "suche", "gibt",
        "welche", "welches", "welcher", "ohne", "über", "ueber"
    };

    private static readonly HashSet<string> InternationalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hotel", "hotels", "hôtel", "spa", "wellness", "pool", "sauna",
        "salzburg", "tirol", "tyrol", "kärnten", "karnten", "carinthia",
        "steiermark", "vorarlberg", "wien", "vienna", "bayern", "bavaria",
        "in", "with", "and", "the", "a", "an", "near",
        "adults", "only", "dogs", "family"
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
        string userText,
        string? languageHint = null)
    {
        var state = Clone(existing);
        var text = userText ?? string.Empty;
        var hint = NormalizeHint(languageHint);

        if (state.PendingCodes is { Count: > 0 })
            return ResolvePending(detection, state, text, hint);

        if (detection.IsUnrecognizedScript)
            return KeepGoing(UnrecognizedLanguage, state, text, "unrecognized_en", lockLanguage: false);

        if (HasLock(state))
            return ResolveFollowUp(detection, state, text);

        return ResolveFirstTurn(detection, state, text, hint);
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
        string text,
        string? hint)
    {
        if (HasGermanOnlyMarker(text))
        {
            Lock(state, FallbackLanguage, firstTurnUnambiguous: true);
            return KeepGoing(FallbackLanguage, state, text, "german_marker", lockLanguage: true);
        }

        var named = LanguageClarification.TryMatchAnyChoice(text);
        if (named != null && !LooksLikeHotelQuery(text))
        {
            Lock(state, named, firstTurnUnambiguous: true);
            return KeepGoing(named, state, text, "choice", lockLanguage: true);
        }

        if (IsSharedVocabularyPhrase(text))
            return PickPreferred(state, text, hint, "shared_vocab");

        var close = CloseLanguages(detection.Ranked);
        var coreClose = close.Where(score => IsCore(score.Code)).ToList();
        var topIsCore = detection.TopCode != null && IsCore(detection.TopCode);

        if (LooksLikeHotelQuery(text) && !topIsCore)
            return PickPreferred(state, text, hint, "hotel_fallback");

        if (coreClose.Count >= 2)
            return PickPreferred(state, text, hint, "ambiguous_core");

        if (close.Count >= 2 && !topIsCore)
            return PickPreferred(state, text, hint, "ambiguous_fallback");

        if (!detection.IsUnknown &&
            !detection.IsAmbiguous &&
            topIsCore)
        {
            Lock(state, detection.TopCode!, firstTurnUnambiguous: true);
            return KeepGoing(detection.TopCode!, state, text, "detected", lockLanguage: true);
        }

        if (!detection.IsUnknown &&
            !string.IsNullOrWhiteSpace(detection.TopCode) &&
            topIsCore)
        {
            Lock(state, detection.TopCode, firstTurnUnambiguous: true);
            return KeepGoing(detection.TopCode, state, text, "detected", lockLanguage: true);
        }

        return PickPreferred(state, text, hint, "fallback_de");
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
            if (!IsCore(detection.TopCode) && LooksLikeHotelQuery(text))
                return KeepGoing(locked, state, text, "sticky", lockLanguage: true);

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
        string text,
        string? hint)
    {
        var pendingQuery = state.PendingQuery;

        if (detection.IsUnrecognizedScript)
        {
            ClearPending(state);
            return KeepGoing(UnrecognizedLanguage, state, pendingQuery ?? text, "unrecognized_en", lockLanguage: false);
        }

        var named = LanguageClarification.TryMatchAnyChoice(text);
        if (named != null)
        {
            var query = LooksLikeHotelQuery(text) ? text : (pendingQuery ?? text);
            Lock(state, named, firstTurnUnambiguous: false);
            return KeepGoing(named, state, query, "choice", lockLanguage: true);
        }

        if (LanguageClarification.IsAffirmative(text) || !LooksLikeHotelQuery(text))
        {
            var language = hint ?? FallbackLanguage;
            Lock(state, language, firstTurnUnambiguous: false);
            return KeepGoing(language, state, pendingQuery ?? text, "choice_default", lockLanguage: true);
        }

        ClearPending(state);
        return ResolveFirstTurn(detection, state, text, hint);
    }

    /// <summary>
    /// Mehrdeutige Hotel-/Kurzphrasen: Client-Hinweis (ChatGPT-Gesprächssprache) oder Deutsch.
    /// Softmax (oft nl/ca/rm) wird hier nicht verwendet.
    /// </summary>
    private static ConversationLanguageDecision PickPreferred(
        ConversationLanguageState state,
        string text,
        string? hint,
        string source)
    {
        var language = hint ?? FallbackLanguage;
        var lockIt = hint != null;
        if (lockIt)
            Lock(state, language, firstTurnUnambiguous: false);
        return KeepGoing(language, state, text, source, lockLanguage: lockIt);
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

    internal static bool HasGermanOnlyMarker(string text) =>
        ExtractWords(text).Any(GermanOnlyMarkers.Contains);

    internal static bool IsSharedVocabularyPhrase(string text)
    {
        var words = ExtractWords(text);
        if (words.Count is < 1 or > 8)
            return false;
        if (words.Any(GermanOnlyMarkers.Contains))
            return false;
        if (!words.Any(w =>
                w.StartsWith("hotel", StringComparison.OrdinalIgnoreCase) ||
                w.Equals("spa", StringComparison.OrdinalIgnoreCase) ||
                w.Equals("wellness", StringComparison.OrdinalIgnoreCase) ||
                w.Equals("sauna", StringComparison.OrdinalIgnoreCase)))
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

    private static bool LooksLikeHotelQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var lower = text.ToLowerInvariant();
        return HotelCues.Any(cue => lower.Contains(cue, StringComparison.Ordinal));
    }

    private static bool IsCore(string code) =>
        CoreLanguages.Contains(code);

    internal static string? NormalizeHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return null;
        var code = hint.Trim().ToLowerInvariant();
        if (code.Length >= 2)
            code = code[..2];
        return LanguageClarification.AllCodes.Contains(code) ? code : null;
    }
}
