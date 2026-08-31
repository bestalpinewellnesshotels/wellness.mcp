using HotelChatbot.Infrastructure.Classifiers;
using HotelChatbot.Infrastructure.Classifiers.Language;
using HotelChatbot.Infrastructure.Services;

namespace HotelChatbot.Tests;

public class ClassifierSmokeTests
{
    [Fact]
    public void LanguageClassifier_DetectsGermanAndEnglish()
    {
        var training = CorpusLoader.LoadTraining();
        var clf = LanguageClassifier.Train(training);

        Assert.Equal("de", clf.Classify("Bitte bringen Sie mir einen Kaffee.").Code);
        Assert.Equal("en", clf.Classify("Looking for a wellness hotel with spa.").Code);
    }

    [Fact]
    public void IntentClassifier_AcceptsHotelQuery_RejectsOffTopic()
    {
        var path = ClassifierDataLocator.Resolve(
            Path.Combine("Data", "intent-training.tsv"),
            typeof(BinaryTextClassifier));
        var clf = BinaryTextClassifier.TrainFromTsv(path, "in_scope", "out_of_scope", 0.42);

        Assert.True(clf.IsPositive("Ich suche ein Wellnesshotel mit Sauna in Tirol."));
        Assert.False(clf.IsPositive("Was ist die Hauptstadt von Frankreich?"));
    }

    [Fact]
    public void IntentClassifier_FailClosed_OnTooShortText()
    {
        var path = ClassifierDataLocator.Resolve(
            Path.Combine("Data", "intent-training.tsv"),
            typeof(BinaryTextClassifier));
        var clf = BinaryTextClassifier.TrainFromTsv(path, "in_scope", "out_of_scope", 0.42);

        Assert.False(clf.IsPositive("a"));
        Assert.False(clf.IsPositive(""));
    }

    [Fact]
    public void EthicalClassifier_RejectsAbuse_AcceptsPolite()
    {
        var path = ClassifierDataLocator.Resolve(
            Path.Combine("Data", "ethical-training.tsv"),
            typeof(BinaryTextClassifier));
        var clf = BinaryTextClassifier.TrainFromTsv(path, "ok", "reject", 0.40);

        Assert.True(clf.IsPositive("Guten Tag, welche Hotels haben eine Sauna?"));
        Assert.False(clf.IsPositive("You are stupid and useless."));
    }

    [Fact]
    public void CrawlerTextUtils_DetectsResourceFiles()
    {
        Assert.True(CrawlerTextUtils.IsResourceFile("https://example.com/photo.JPG"));
        Assert.False(CrawlerTextUtils.IsResourceFile("https://example.com/wellness"));
    }
}
