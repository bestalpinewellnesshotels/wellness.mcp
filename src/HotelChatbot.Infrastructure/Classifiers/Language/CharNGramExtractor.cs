using System.Globalization;
using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers.Language;

public static class CharNGramExtractor
{
    public const int MinN = 1;
    public const int MaxN = 4;
    public const int MaxWordFeatureLength = 14;

    public static Dictionary<string, int> Extract(string text)
    {
        var counts = new Dictionary<string, int>(256, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return counts;
        }

        var normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        foreach (var word in EnumerateWords(normalized))
        {
            if (word.Length <= MaxWordFeatureLength)
            {
                Increment(counts, "w:" + word);
            }

            var padded = string.Concat(" ", word, " ");
            for (var n = MinN; n <= MaxN; n++)
            {
                var last = padded.Length - n;
                for (var i = 0; i <= last; i++)
                {
                    Increment(counts, padded.Substring(i, n));
                }
            }
        }

        return counts;
    }

    public static int LetterCount(string text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> EnumerateWords(string text)
    {
        var buffer = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || IsCombiningMark(rune))
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
        {
            yield return buffer.ToString();
        }
    }

    private static bool IsCombiningMark(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
