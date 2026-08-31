using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Classifiers;

public sealed class EthicalClassifierService : IEthicalClassifier
{
    private readonly BinaryTextClassifier _classifier;
    private readonly ILogger<EthicalClassifierService> _logger;

    /// <summary>
    /// Reject nur bei Beleidigungs-Hinweis + klarer Reject-Wahrscheinlichkeit (fail-open).
    /// Harmlose Hotel-/Sachfragen sollen nie am Ethical-Gate scheitern.
    /// </summary>
    private const double MinRejectConfidence = 0.55;

    private static readonly string[] HardRejectMarkers =
    [
        "kill yourself", "kys", "nigger", "nigga", "faggot", "hurensohn", "fotze",
        "arschloch", "wichser", "schwuchtel", "drecksau", "vergas", "heil hitler",
        "you should die", "i hope you die"
    ];

    /// <summary>
    /// Weiche Insult-Marker: ohne Treffer → immer OK (Classifier allein zu unscharf).
    /// </summary>
    private static readonly string[] SoftInsultMarkers =
    [
        "idiot", "stupid", "dumbass", "dumb ", "useless", "fuck you", "fuck off", "shut up",
        "nutzlos", "dummkopf", "vollidiot", "blödmann", "blodmann", "halt die fresse",
        "verpiss", "fick dich", "du bist müll", "du bist mull", "imbecil", "bastard",
        "arsehole", "asshole", "moron", "retard", "geh sterben", "kill yourself"
    ];

    public EthicalClassifierService(ILogger<EthicalClassifierService> logger)
    {
        _logger = logger;
        var path = ClassifierDataLocator.Resolve(Path.Combine("Data", "ethical-training.tsv"), typeof(EthicalClassifierService));
        _classifier = BinaryTextClassifier.TrainFromTsv(path, "ok", "reject", minPositiveProbability: 0.40);
        _logger.LogInformation(
            "Ethical-Klassifizierer geladen aus {Path} (fail-open: Insult-Marker + reject≥{Threshold:0.00})",
            path, MinRejectConfidence);
    }

    public bool IsEthical(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var lower = text.ToLowerInvariant();
        if (HardRejectMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal)))
        {
            _logger.LogInformation("EthicalClassifier: REJECT (Hard-Marker)");
            return false;
        }

        var hasInsultCue = SoftInsultMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal));
        if (!hasInsultCue)
        {
            _logger.LogDebug("EthicalClassifier: OK (kein Insult-Marker)");
            return true;
        }

        var (label, probability) = _classifier.Classify(text);
        var isReject = label.Equals("reject", StringComparison.OrdinalIgnoreCase)
                       && probability >= MinRejectConfidence;

        if (isReject)
        {
            _logger.LogInformation(
                "EthicalClassifier: REJECT (Insult+Classifier label={Label}, p={Probability:0.00})",
                label, probability);
            return false;
        }

        _logger.LogDebug(
            "EthicalClassifier: OK trotz Insult-Cue (label={Label}, p={Probability:0.00})",
            label, probability);
        return true;
    }
}
