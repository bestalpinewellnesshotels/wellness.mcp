using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers.Language;

public static class CorpusLoader
{
    public static IReadOnlyList<LabeledText> LoadTraining() => Load("training.tsv");

    public static IReadOnlyList<LabeledText> LoadTest() => Load("test.tsv");

    private static IReadOnlyList<LabeledText> Load(string fileName)
    {
        var path = ResolveDataPath(fileName);
        if (path is null)
        {
            throw new FileNotFoundException(
                $"Korpusdatei fehlt: {fileName}. Erwartet unter Classifiers/Language/Data/.");
        }

        var items = new List<LabeledText>();
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.Equals("code\ttext", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                throw new InvalidDataException($"Ungültige Korpuszeile: {line}");
            }

            var code = line[..tab].Trim();
            var text = line[(tab + 1)..].Trim();
            if (!LanguageCatalog.ByCode.ContainsKey(code))
            {
                throw new InvalidDataException($"Unbekannter Sprachcode '{code}' in {fileName}.");
            }

            var weight = fileName.Equals("training.tsv", StringComparison.OrdinalIgnoreCase)
                ? ExampleWeight(text)
                : 1.0;
            items.Add(new LabeledText(code, text, weight));
        }

        if (items.Count == 0)
        {
            throw new InvalidDataException($"Korpusdatei {fileName} enthält keine Sätze.");
        }

        var missing = LanguageCatalog.All
            .Select(language => language.Code)
            .Except(items.Select(item => item.Code), StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"In {fileName} fehlen Sprachen: {string.Join(", ", missing)}");
        }

        return items;
    }

    internal static string? ResolveDataPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Classifiers", "Language", "Data", fileName),
            Path.Combine(AppContext.BaseDirectory, "Data", "Language", fileName),
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var asmDir = Path.GetDirectoryName(typeof(CorpusLoader).Assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            var walk = new DirectoryInfo(asmDir);
            for (var i = 0; i < 8 && walk is not null; i++, walk = walk.Parent)
            {
                var nested = Path.Combine(walk.FullName, "Classifiers", "Language", "Data", fileName);
                if (File.Exists(nested))
                    return nested;
            }
        }

        return null;
    }

    private static double ExampleWeight(string text)
    {
        if (Greetings.IsKnown(text))
        {
            return 1.0;
        }

        var letters = CharNGramExtractor.LetterCount(text);
        if (letters < 12)
        {
            return 0.2;
        }

        var last = text[^1];
        var complete = last is '.' or '?' or '!' or '։' or '…';
        if (complete && letters >= 25)
        {
            return 1.0;
        }

        return complete ? 0.7 : 0.35;
    }
}
