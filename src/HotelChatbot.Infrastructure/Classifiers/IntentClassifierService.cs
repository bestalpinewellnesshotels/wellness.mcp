using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Classifiers;

public sealed class IntentClassifierService : IIntentClassifier
{
    private readonly BinaryTextClassifier _classifier;
    private readonly ILogger<IntentClassifierService> _logger;

    public IntentClassifierService(ILogger<IntentClassifierService> logger)
    {
        _logger = logger;
        var path = ClassifierDataLocator.Resolve(Path.Combine("Data", "intent-training.tsv"), typeof(IntentClassifierService));
        _classifier = BinaryTextClassifier.TrainFromTsv(path, "in_scope", "out_of_scope", minPositiveProbability: 0.42);
        _logger.LogInformation("Intent-Klassifizierer geladen aus {Path}", path);
    }

    public bool IsHotelWellnessQuery(string text)
    {
        if (HotelDomainCues.Matches(text))
        {
            _logger.LogDebug("IntentClassifier: in_scope (domain cue)");
            return true;
        }

        var (label, probability) = _classifier.Classify(text);
        var ok = label.Equals("in_scope", StringComparison.OrdinalIgnoreCase)
                 && probability >= 0.42;
        _logger.LogDebug("IntentClassifier: {Result} (p={Probability:0.00})", ok ? "in_scope" : "out_of_scope", probability);
        return ok;
    }
}
