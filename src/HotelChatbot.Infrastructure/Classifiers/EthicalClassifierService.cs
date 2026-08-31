using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Classifiers;

public sealed class EthicalClassifierService : IEthicalClassifier
{
    private readonly BinaryTextClassifier _classifier;
    private readonly ILogger<EthicalClassifierService> _logger;

    // Harte Keyword-Blacklist für klare Reject-Fälle (ergänzt den Klassifizierer)
    private static readonly string[] HardRejectMarkers =
    [
        "kill yourself", "kys", "nigger", "nigga", "faggot", "hurensohn", "fotze",
        "arschloch", "wichser", "schwuchtel", "drecksau", "vergas", "heil hitler",
        "you should die", "i hope you die"
    ];

    public EthicalClassifierService(ILogger<EthicalClassifierService> logger)
    {
        _logger = logger;
        var path = ClassifierDataLocator.Resolve(Path.Combine("Data", "ethical-training.tsv"), typeof(EthicalClassifierService));
        _classifier = BinaryTextClassifier.TrainFromTsv(path, "ok", "reject", minPositiveProbability: 0.40);
        _logger.LogInformation("Ethical-Klassifizierer geladen aus {Path}", path);
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

        var ok = _classifier.IsPositive(text);
        _logger.LogDebug("EthicalClassifier: {Result}", ok ? "OK" : "REJECT");
        return ok;
    }
}
