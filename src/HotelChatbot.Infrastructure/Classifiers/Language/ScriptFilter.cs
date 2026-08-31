using System.Globalization;
using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers.Language;

public enum LetterScript
{
    Latin,
    Cyrillic,
    Greek,
    Georgian,
    Armenian,
    Other,
}

public static class ScriptFilter
{
    private static readonly HashSet<string> CyrillicLanguages = new(StringComparer.Ordinal)
    {
        "be", "bg", "mk", "sr", "uk",
    };

    public static IReadOnlySet<string>? RestrictTo(string text)
    {
        var counts = new Dictionary<LetterScript, int>();
        var letters = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            letters++;
            var script = Classify(rune);
            counts[script] = counts.GetValueOrDefault(script) + 1;
        }

        if (letters < 4)
        {
            return null;
        }

        var dominant = counts.OrderByDescending(pair => pair.Value).First();
        if (dominant.Value < letters * 0.7)
        {
            return null;
        }

        return dominant.Key switch
        {
            LetterScript.Greek => new HashSet<string>(StringComparer.Ordinal) { "el" },
            LetterScript.Georgian => new HashSet<string>(StringComparer.Ordinal) { "ka" },
            LetterScript.Armenian => new HashSet<string>(StringComparer.Ordinal) { "hy" },
            LetterScript.Cyrillic => CyrillicLanguages,
            _ => null,
        };
    }

    private static LetterScript Classify(Rune rune)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
        {
            return LetterScript.Other;
        }

        var value = rune.Value;
        if (value is >= 0x0400 and <= 0x052F or >= 0x2DE0 and <= 0x2DFF or >= 0xA640 and <= 0xA69F)
        {
            return LetterScript.Cyrillic;
        }

        if (value is >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF)
        {
            return LetterScript.Greek;
        }

        if (value is >= 0x10A0 and <= 0x10FF or >= 0x2D00 and <= 0x2D2F)
        {
            return LetterScript.Georgian;
        }

        if (value is >= 0x0530 and <= 0x058F)
        {
            return LetterScript.Armenian;
        }

        if (value <= 0x024F || value is >= 0x1E00 and <= 0x1EFF)
        {
            return LetterScript.Latin;
        }

        return LetterScript.Other;
    }
}
