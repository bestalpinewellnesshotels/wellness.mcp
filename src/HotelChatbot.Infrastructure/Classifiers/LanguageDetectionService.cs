using HotelChatbot.Domain.Interfaces;
using HotelChatbot.Domain.Language;
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
        var details = Classify(text);
        if (details.IsUnknown ||
            details.IsAmbiguous ||
            details.IsUnrecognizedScript ||
            string.IsNullOrWhiteSpace(details.TopCode))
        {
            _logger.LogDebug(
                "Spracherkennung unsicher ({Reason}) → Fallback {Fallback}",
                details.Reason ?? "unknown", fallback);
            return fallback;
        }

        return details.TopCode;
    }

    public LanguageDetectionDetails Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return LanguageDetectionDetails.Empty("Leerer Text.");

        var result = _classifier.Classify(text);
        return new LanguageDetectionDetails
        {
            TopCode = result.Code,
            Confidence = result.Confidence,
            Coverage = result.Coverage,
            IsUnknown = result.IsUnknown,
            IsAmbiguous = result.IsAmbiguous,
            IsUnrecognizedScript = result.IsUnrecognizedScript,
            Reason = result.Reason,
            Ranked = result.Top
                .Select(item => new LanguageScore(item.Code, item.Name, item.Probability))
                .ToList()
        };
    }
}
