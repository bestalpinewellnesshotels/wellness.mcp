using System.Globalization;
using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers;

/// <summary>
/// Lexikalische Domain-Hinweise für den Intent-Gate.
/// Der Naive-Bayes-Klassifizierer allein ist auf Satzbausteine trainiert;
/// Hotel-, Urlaubs- und alpines Aktivitätsvokabular in beliebiger Formulierung
/// bleibt deshalb zusätzlich über diese Cues in-scope.
/// Entspricht der inklusiven Policy von pipeline.logical_check.
/// </summary>
public static class HotelDomainCues
{
    private static readonly string[] TokenContains =
    [
        "hotel", "wellness", "unterkunft", "хотел",
        "sauna", "hamam", "jacuzzi", "whirlpool",
        "massag", "kulinar", "urlaub", "pauschale",
        "piste", "adults", "halfboard", "halbpension",
        "ferien",
    ];

    private static readonly string[] ExactTokens =
    [
        "spa", "resort", "albergo", "lodging", "accommodation",
        "hike", "hiking", "reiten", "reiter",
    ];

    private static readonly string[] SkiPrefixes =
    [
        "skifahr", "skiurlaub", "skilift", "skipiste", "skigebiet",
        "skihotel", "skisport", "skiin", "skiout", "skiraum",
        "schifahr", "schiurlaub", "schilift", "schipiste", "schigebiet",
        "schihotel", "schisport", "schiraum",
    ];

    private static readonly string[] OutdoorPrefixes =
    [
        "wandern", "wanderung", "wanderweg", "wanderhotel",
        "reiten", "reithalle", "reitstall", "reitsport", "reiturlaub", "reiterhof", "reitangebot",
    ];

    /// <summary>
    /// Distinctive property names from the catalog. Generic tokens such as
    /// "stock" or "engel" are omitted to avoid false positives.
    /// </summary>
    private static readonly string[] KnownProperties =
    [
        "gmachl", "nesslerhof", "krallerhof", "alpbacherhof",
        "waldklause", "hochschober", "wartherhof", "alpenrose",
        "alpenresort", "uebergossenealm", "ubergossenealm",
    ];

    public static bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var word in EnumerateFoldedWords(text))
        {
            if (IsDomainWord(word))
                return true;
        }

        return false;
    }

    private static bool IsDomainWord(string word)
    {
        if (word.Length < 3)
            return false;

        foreach (var stem in TokenContains)
        {
            if (word.Contains(stem, StringComparison.Ordinal))
                return true;
        }

        foreach (var exact in ExactTokens)
        {
            if (word.Equals(exact, StringComparison.Ordinal))
                return true;
        }

        if (word is "ski" or "schi")
            return true;

        foreach (var prefix in SkiPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        foreach (var prefix in OutdoorPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        foreach (var name in KnownProperties)
        {
            if (word.Equals(name, StringComparison.Ordinal) || word.Contains(name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateFoldedWords(string text)
    {
        var folded = Fold(text);
        var buffer = new StringBuilder();
        foreach (var rune in folded.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                buffer.Append(rune.ToString());
                continue;
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    private static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark)
                continue;
            if (rune.Value == 0x00DF)
            {
                sb.Append("ss");
                continue;
            }

            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }
}
