using HotelChatbot.Domain.Interfaces;
using HotelChatbot.Infrastructure.Classifiers.Language;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Classifiers;

public sealed class LanguageDetectionService : ILanguageDetector
{
    private readonly LanguageClassifier _classifier;
    private readonly ILogger<LanguageDetectionService> _logger;

    public LanguageDetectionService(ILogger<LanguageDetectionService> logger)
    {
        _logger = logger;
        var training = CorpusLoader.LoadTraining();
        _classifier = LanguageClassifier.Train(training);
        _logger.LogInformation(
            "Sprachklassifizierer geladen: {Languages} Sprachen, {Samples} Trainingssätze",
            _classifier.Labels.Count, training.Count);
    }

    public string Detect(string text, string fallback = "de")
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        var result = _classifier.Classify(text);
        if (result.IsUnknown || string.IsNullOrWhiteSpace(result.Code))
        {
            _logger.LogDebug(
                "Spracherkennung unsicher ({Reason}) → Fallback {Fallback}",
                result.Reason ?? "unknown", fallback);
            return fallback;
        }

        return result.Code;
    }
}
