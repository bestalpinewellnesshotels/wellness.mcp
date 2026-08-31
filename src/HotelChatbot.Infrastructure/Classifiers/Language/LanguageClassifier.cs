namespace HotelChatbot.Infrastructure.Classifiers.Language;

public sealed class LanguageClassifier
{
    private const double Smoothing = 0.45;
    private const int MinLetters = 2;
    private const double MinConfidence = 0.36;
    private const double MinCoverage = 0.28;
    private const double MinMargin = 0.04;
    private const double MinWordCoverage = 0.12;
    private const double HighConfidenceWithoutWords = 0.92;
    private const double MinSharedWordCoverage = 0.40;
    private const int MaxSharedLetters = 96;

    private readonly string[] _labels;
    private readonly double[] _logPrior;
    private readonly Dictionary<string, float>[] _logLikelihood;
    private readonly double[] _unseenLogLikelihood;

    private LanguageClassifier(
        string[] labels,
        double[] logPrior,
        Dictionary<string, float>[] logLikelihood,
        double[] unseenLogLikelihood)
    {
        _labels = labels;
        _logPrior = logPrior;
        _logLikelihood = logLikelihood;
        _unseenLogLikelihood = unseenLogLikelihood;
    }

    public IReadOnlyList<string> Labels => _labels;

    public static LanguageClassifier Train(IReadOnlyList<LabeledText> data)
    {
        if (data.Count == 0)
        {
            throw new InvalidOperationException("Leerer Trainingsdatensatz.");
        }

        var labels = data.Select(item => item.Code).Distinct().OrderBy(code => code, StringComparer.Ordinal).ToArray();
        var indexByCode = labels
            .Select((code, index) => (code, index))
            .ToDictionary(pair => pair.code, pair => pair.index, StringComparer.Ordinal);

        var classCount = labels.Length;
        var documentWeights = new double[classCount];
        var tokenTotals = new double[classCount];
        var ngramCounts = Enumerable.Range(0, classCount)
            .Select(_ => new Dictionary<string, double>(StringComparer.Ordinal))
            .ToArray();
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in data)
        {
            var classIndex = indexByCode[item.Code];
            var weight = item.Weight <= 0 ? 1.0 : item.Weight;
            documentWeights[classIndex] += weight;
            foreach (var (ngram, count) in CharNGramExtractor.Extract(item.Text))
            {
                var weighted = count * weight;
                vocabulary.Add(ngram);
                ngramCounts[classIndex][ngram] = ngramCounts[classIndex].GetValueOrDefault(ngram) + weighted;
                tokenTotals[classIndex] += weighted;
            }
        }

        var vocabularySize = Math.Max(vocabulary.Count, 1);
        var logPrior = new double[classCount];
        var logLikelihood = new Dictionary<string, float>[classCount];
        var unseen = new double[classCount];
        var documentTotal = documentWeights.Sum();

        for (var classIndex = 0; classIndex < classCount; classIndex++)
        {
            logPrior[classIndex] = Math.Log((documentWeights[classIndex] + Smoothing) /
                                           (documentTotal + Smoothing * classCount));
            var denominator = tokenTotals[classIndex] + Smoothing * vocabularySize;
            unseen[classIndex] = Math.Log(Smoothing / denominator);
            var table = new Dictionary<string, float>(ngramCounts[classIndex].Count, StringComparer.Ordinal);
            foreach (var (ngram, count) in ngramCounts[classIndex])
            {
                table[ngram] = (float)Math.Log((count + Smoothing) / denominator);
            }

            logLikelihood[classIndex] = table;
        }

        return new LanguageClassifier(labels, logPrior, logLikelihood, unseen);
    }

    public ClassificationResult Classify(string text)
    {
        if (Greetings.TryMatch(text, out var greetingCodes) && greetingCodes.Length > 0)
        {
            return FromGreeting(greetingCodes);
        }

        var letterCount = CharNGramExtractor.LetterCount(text);
        if (letterCount < MinLetters)
        {
            return ClassificationResult.Unknown("Zu wenig Text für eine zuverlässige Erkennung.", []);
        }

        if (ScriptFilter.IsUnsupportedScript(text))
        {
            return ClassificationResult.Unknown(
                "Nicht unterstützte Schrift (keine trainierte Sprache).",
                [],
                isUnrecognizedScript: true);
        }

        var features = CharNGramExtractor.Extract(text);
        if (features.Count == 0)
        {
            return ClassificationResult.Unknown("Keine Buchstaben gefunden.", []);
        }

        var allowed = ScriptFilter.RestrictTo(text);
        var scores = new double[_labels.Length];
        var seenCounts = new int[_labels.Length];
        var wordSeenCounts = new int[_labels.Length];
        var tokenCount = features.Values.Sum();
        var wordTokenCount = 0;

        for (var classIndex = 0; classIndex < _labels.Length; classIndex++)
        {
            if (allowed is not null && !allowed.Contains(_labels[classIndex]))
            {
                scores[classIndex] = double.NegativeInfinity;
                continue;
            }

            var score = _logPrior[classIndex];
            var table = _logLikelihood[classIndex];
            foreach (var (ngram, count) in features)
            {
                var isWord = ngram.StartsWith("w:", StringComparison.Ordinal);
                if (classIndex == 0 && isWord)
                {
                    wordTokenCount += count;
                }

                if (table.TryGetValue(ngram, out var logProbability))
                {
                    score += count * logProbability;
                    seenCounts[classIndex] += count;
                    if (isWord)
                    {
                        wordSeenCounts[classIndex] += count;
                    }
                }
                else
                {
                    score += count * _unseenLogLikelihood[classIndex];
                }
            }

            scores[classIndex] = score;
        }

        if (scores.All(double.IsNegativeInfinity))
        {
            return ClassificationResult.Unknown(
                "Der Text passt zu keiner trainierten europäischen Sprache.",
                []);
        }

        var probabilities = Softmax(scores);
        var ranked = probabilities
            .Select((probability, index) => (Code: _labels[index], Probability: probability, Index: index))
            .OrderByDescending(item => item.Probability)
            .ToArray();

        var top = ranked[0];
        var second = ranked.Length > 1 ? ranked[1].Probability : 0;
        var coverage = tokenCount == 0 ? 0 : (double)seenCounts[top.Index] / tokenCount;
        var wordCoverage = wordTokenCount == 0
            ? 0
            : (double)wordSeenCounts[top.Index] / wordTokenCount;
        var topHits = ranked
            .Take(8)
            .Select(item => (item.Code, LanguageCatalog.NameOf(item.Code), item.Probability))
            .ToArray();

        if (coverage < MinCoverage)
        {
            return ClassificationResult.Unknown(
                "Der Text passt zu keiner trainierten europäischen Sprache.",
                topHits);
        }

        if (wordTokenCount > 0 &&
            wordCoverage < MinWordCoverage &&
            top.Probability < HighConfidenceWithoutWords)
        {
            return ClassificationResult.Unknown(
                "Der Text passt zu keiner trainierten europäischen Sprache.",
                topHits);
        }

        if (wordTokenCount > 0 && letterCount <= MaxSharedLetters)
        {
            var shared = ranked
                .Where(item => (double)wordSeenCounts[item.Index] / wordTokenCount >= MinSharedWordCoverage)
                .Take(8)
                .ToArray();
            if (shared.Length >= 2)
            {
                var weights = shared
                    .Select(item => (double)wordSeenCounts[item.Index] / wordTokenCount)
                    .ToArray();
                var weightSum = weights.Sum();
                var flattened = shared
                    .Select((item, index) => (
                        item.Code,
                        LanguageCatalog.NameOf(item.Code),
                        weightSum <= 0 ? 0 : weights[index] / weightSum))
                    .OrderByDescending(item => item.Item3)
                    .ToArray();
                return new ClassificationResult(
                    flattened[0].Code,
                    flattened[0].Item2,
                    flattened[0].Item3,
                    coverage,
                    IsUnknown: false,
                    Reason: "Mehrere Sprachen teilen denselben Wortschatz.",
                    flattened,
                    IsAmbiguous: true);
            }
        }

        var margin = top.Probability - second;
        if (margin < MinMargin)
        {
            return new ClassificationResult(
                top.Code,
                LanguageCatalog.NameOf(top.Code),
                top.Probability,
                coverage,
                IsUnknown: false,
                Reason: "Mehrere Sprachen liegen nah beieinander.",
                topHits,
                IsAmbiguous: true);
        }

        if (top.Probability < MinConfidence)
        {
            return ClassificationResult.Unknown(
                "Die Konfidenz liegt unter dem Schwellenwert.",
                topHits);
        }

        return new ClassificationResult(
            top.Code,
            LanguageCatalog.NameOf(top.Code),
            top.Probability,
            coverage,
            IsUnknown: false,
            Reason: null,
            topHits);
    }

    private static ClassificationResult FromGreeting(string[] codes)
    {
        var probabilities = new double[codes.Length];
        probabilities[0] = codes.Length == 1 ? 0.97 : 0.78;
        if (codes.Length > 1)
        {
            var rest = (1.0 - probabilities[0]) / (codes.Length - 1);
            for (var i = 1; i < codes.Length; i++)
            {
                probabilities[i] = rest;
            }
        }

        var top = codes
            .Select((code, index) => (code, LanguageCatalog.NameOf(code), probabilities[index]))
            .Take(3)
            .ToArray();

        return new ClassificationResult(
            codes[0],
            LanguageCatalog.NameOf(codes[0]),
            probabilities[0],
            Coverage: 1,
            IsUnknown: false,
            Reason: null,
            top);
    }

    private static double[] Softmax(double[] scores)
    {
        var max = scores.Max();
        var probabilities = new double[scores.Length];
        var sum = 0.0;
        for (var i = 0; i < scores.Length; i++)
        {
            var value = double.IsNegativeInfinity(scores[i]) ? 0.0 : Math.Exp(scores[i] - max);
            probabilities[i] = value;
            sum += value;
        }

        for (var i = 0; i < scores.Length; i++)
        {
            probabilities[i] /= sum;
        }

        return probabilities;
    }
}

public sealed record ClassificationResult(
    string? Code,
    string? Name,
    double Confidence,
    double Coverage,
    bool IsUnknown,
    string? Reason,
    IReadOnlyList<(string Code, string Name, double Probability)> Top,
    bool IsAmbiguous = false,
    bool IsUnrecognizedScript = false)
{
    public static ClassificationResult Unknown(
        string reason,
        IReadOnlyList<(string Code, string Name, double Probability)> top,
        bool isUnrecognizedScript = false) =>
        new(null, null, top.Count > 0 ? top[0].Probability : 0, 0, true, reason, top,
            IsAmbiguous: false, IsUnrecognizedScript: isUnrecognizedScript);
}
