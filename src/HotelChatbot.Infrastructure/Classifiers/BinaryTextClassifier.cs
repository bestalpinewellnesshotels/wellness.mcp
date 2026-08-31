using HotelChatbot.Infrastructure.Classifiers.Language;
using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers;

/// <summary>
/// Binärer Naive-Bayes-Klassifizierer auf Zeichen-N-Grammen (für Intent/Ethical).
/// </summary>
public sealed class BinaryTextClassifier
{
    private const double Smoothing = 0.5;
    private readonly string _positiveLabel;
    private readonly string _negativeLabel;
    private readonly double _positiveLogPrior;
    private readonly double _negativeLogPrior;
    private readonly Dictionary<string, float> _positiveLikelihood;
    private readonly Dictionary<string, float> _negativeLikelihood;
    private readonly double _positiveUnseen;
    private readonly double _negativeUnseen;
    private readonly double _minPositiveProbability;

    private BinaryTextClassifier(
        string positiveLabel,
        string negativeLabel,
        double positiveLogPrior,
        double negativeLogPrior,
        Dictionary<string, float> positiveLikelihood,
        Dictionary<string, float> negativeLikelihood,
        double positiveUnseen,
        double negativeUnseen,
        double minPositiveProbability)
    {
        _positiveLabel = positiveLabel;
        _negativeLabel = negativeLabel;
        _positiveLogPrior = positiveLogPrior;
        _negativeLogPrior = negativeLogPrior;
        _positiveLikelihood = positiveLikelihood;
        _negativeLikelihood = negativeLikelihood;
        _positiveUnseen = positiveUnseen;
        _negativeUnseen = negativeUnseen;
        _minPositiveProbability = minPositiveProbability;
    }

    public static BinaryTextClassifier Train(
        IEnumerable<(string Label, string Text)> samples,
        string positiveLabel,
        string negativeLabel,
        double minPositiveProbability = 0.45)
    {
        var list = samples.ToList();
        var pos = list.Where(s => s.Label.Equals(positiveLabel, StringComparison.OrdinalIgnoreCase)).ToList();
        var neg = list.Where(s => s.Label.Equals(negativeLabel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pos.Count == 0 || neg.Count == 0)
            throw new InvalidOperationException($"Training braucht beide Labels: {positiveLabel} und {negativeLabel}.");

        var posCounts = new Dictionary<string, double>(StringComparer.Ordinal);
        var negCounts = new Dictionary<string, double>(StringComparer.Ordinal);
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);
        double posTokens = 0, negTokens = 0;

        foreach (var sample in pos)
        {
            foreach (var (ngram, count) in CharNGramExtractor.Extract(sample.Text))
            {
                vocabulary.Add(ngram);
                posCounts[ngram] = posCounts.GetValueOrDefault(ngram) + count;
                posTokens += count;
            }
        }

        foreach (var sample in neg)
        {
            foreach (var (ngram, count) in CharNGramExtractor.Extract(sample.Text))
            {
                vocabulary.Add(ngram);
                negCounts[ngram] = negCounts.GetValueOrDefault(ngram) + count;
                negTokens += count;
            }
        }

        var vocabSize = Math.Max(vocabulary.Count, 1);
        var totalDocs = pos.Count + neg.Count;
        var posLogPrior = Math.Log((pos.Count + Smoothing) / (totalDocs + Smoothing * 2));
        var negLogPrior = Math.Log((neg.Count + Smoothing) / (totalDocs + Smoothing * 2));
        var posDenom = posTokens + Smoothing * vocabSize;
        var negDenom = negTokens + Smoothing * vocabSize;

        var posTable = new Dictionary<string, float>(posCounts.Count, StringComparer.Ordinal);
        foreach (var (ngram, count) in posCounts)
            posTable[ngram] = (float)Math.Log((count + Smoothing) / posDenom);

        var negTable = new Dictionary<string, float>(negCounts.Count, StringComparer.Ordinal);
        foreach (var (ngram, count) in negCounts)
            negTable[ngram] = (float)Math.Log((count + Smoothing) / negDenom);

        return new BinaryTextClassifier(
            positiveLabel,
            negativeLabel,
            posLogPrior,
            negLogPrior,
            posTable,
            negTable,
            Math.Log(Smoothing / posDenom),
            Math.Log(Smoothing / negDenom),
            minPositiveProbability);
    }

    public static BinaryTextClassifier TrainFromTsv(
        string path,
        string positiveLabel,
        string negativeLabel,
        double minPositiveProbability = 0.45)
    {
        var samples = new List<(string Label, string Text)>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.Equals("label\ttext", StringComparison.OrdinalIgnoreCase))
                continue;

            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
                continue;

            samples.Add((line[..tab].Trim(), line[(tab + 1)..].Trim()));
        }

        return Train(samples, positiveLabel, negativeLabel, minPositiveProbability);
    }

    /// <summary>
    /// true wenn die positive Klasse wahrscheinlicher ist und über dem Schwellenwert liegt.
    /// </summary>
    public bool IsPositive(string text)
    {
        var (label, probability) = Classify(text);
        return label.Equals(_positiveLabel, StringComparison.OrdinalIgnoreCase)
               && probability >= _minPositiveProbability;
    }

    public (string Label, double Probability) Classify(string text)
    {
        // Fail-closed: zu wenig Text → negative Klasse (nicht durchlassen)
        if (string.IsNullOrWhiteSpace(text) || CharNGramExtractor.LetterCount(text) < 2)
            return (_negativeLabel, 1.0);

        var features = CharNGramExtractor.Extract(text);
        if (features.Count == 0)
            return (_negativeLabel, 1.0);

        var posScore = _positiveLogPrior;
        var negScore = _negativeLogPrior;
        foreach (var (ngram, count) in features)
        {
            posScore += count * (_positiveLikelihood.TryGetValue(ngram, out var pl) ? pl : _positiveUnseen);
            negScore += count * (_negativeLikelihood.TryGetValue(ngram, out var nl) ? nl : _negativeUnseen);
        }

        var max = Math.Max(posScore, negScore);
        var posProb = Math.Exp(posScore - max);
        var negProb = Math.Exp(negScore - max);
        var sum = posProb + negProb;
        posProb /= sum;
        negProb /= sum;

        return posProb >= negProb
            ? (_positiveLabel, posProb)
            : (_negativeLabel, negProb);
    }
}
