using HotelChatbot.Infrastructure.Classifiers;
using HotelChatbot.Infrastructure.Classifiers.Language;
using HotelChatbot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Theory]
    [InlineData("Ich suche ein Wellnesshotel mit Sauna in Tirol.")]
    [InlineData("Mich interessiert ob es Hotels gibt, bei denen man mit den Schi bis zum Hoteleingang fahren kann")]
    [InlineData("Gibt es Hotels mit Pistenanschluss?")]
    [InlineData("Welche Hotels liegen direkt an der Skipiste?")]
    [InlineData("Are there ski-in ski-out hotels?")]
    [InlineData("Kann man in euren Hotels wandern gehen?")]
    [InlineData("Gibt es Reitangebote im Hotel?")]
    [InlineData("Y a-t-il des hôtels avec ski aux pieds?")]
    [InlineData("Sag mir mehr über Gmachl")]
    [InlineData("Was bietet der Krallerhof?")]
    [InlineData("Adults Only Hotel mit Pool")]
    [InlineData("Hotels in Tirol")]
    public void IntentClassifierService_AcceptsHotelAndVacationQueries(string text)
    {
        var svc = new IntentClassifierService(NullLogger<IntentClassifierService>.Instance);
        Assert.True(svc.IsHotelWellnessQuery(text));
    }

    [Theory]
    [InlineData("Was ist die Hauptstadt von Frankreich?")]
    [InlineData("Wie wird das Wetter morgen in Berlin?")]
    [InlineData("Schreib eine Python-Funktion für Sortieren.")]
    [InlineData("Erkläre Photosynthese einfach.")]
    [InlineData("Who won Wimbledon last year?")]
    [InlineData("Bitcoin price today?")]
    public void IntentClassifierService_RejectsOffTopic(string text)
    {
        var svc = new IntentClassifierService(NullLogger<IntentClassifierService>.Instance);
        Assert.False(svc.IsHotelWellnessQuery(text));
    }

    [Fact]
    public void IntentTraining_EachLanguageHasAtLeast64InAnd64Out()
    {
        var path = ClassifierDataLocator.Resolve(
            Path.Combine("Data", "intent-training.tsv"),
            typeof(BinaryTextClassifier));
        var blocks = LoadLanguageBlocks(path);
        Assert.True(blocks.Count >= 30, $"expected ≥30 languages, got {blocks.Count}");
        foreach (var (ins, outs) in blocks)
        {
            Assert.True(ins.Count >= 64, $"in_scope {ins.Count} < 64 ({ins[0]})");
            Assert.True(outs.Count >= 64, $"out_of_scope {outs.Count} < 64");
        }
    }

    [Fact]
    public void IntentTraining_NoAmenityLocationTemplateSwaps()
    {
        var path = ClassifierDataLocator.Resolve(
            Path.Combine("Data", "intent-training.tsv"),
            typeof(BinaryTextClassifier));
        foreach (var (ins, _) in LoadLanguageBlocks(path))
        {
            for (var i = 0; i < ins.Count; i++)
            {
                for (var j = i + 1; j < ins.Count; j++)
                {
                    var n = LongestCommonPrefix(ins[i], ins[j]);
                    var shorter = Math.Min(ins[i].Length, ins[j].Length);
                    Assert.False(
                        n >= 40 && shorter > 0 && n / (double)shorter >= 0.55,
                        $"template swap:\n  {ins[i]}\n  {ins[j]}");
                }
            }
        }
    }

    private static List<(List<string> In, List<string> Out)> LoadLanguageBlocks(string path)
    {
        var ins = new List<string>();
        var outs = new List<string>();
        var blocks = new List<(List<string>, List<string>)>();

        void Flush()
        {
            if (ins.Count == 0 && outs.Count == 0)
                return;
            blocks.Add((ins.ToList(), outs.ToList()));
            ins.Clear();
            outs.Clear();
        }

        string? prev = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.Equals("label\ttext", StringComparison.OrdinalIgnoreCase))
                continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0)
                continue;
            var label = line[..tab];
            var text = line[(tab + 1)..];
            if (label == "in_scope" && prev == "out_of_scope")
                Flush();
            if (label == "in_scope")
                ins.Add(text);
            else if (label == "out_of_scope")
                outs.Add(text);
            prev = label;
        }
        Flush();
        return blocks;
    }

    private static int LongestCommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i])
            i++;
        return i;
    }
    [Fact]
    public void HotelDomainCues_MatchCompoundsAndDialectNotExactQuestion()
    {
        Assert.True(HotelDomainCues.Matches("Hoteleingang an der Piste"));
        Assert.True(HotelDomainCues.Matches("Schifahren im Winter"));
        Assert.True(HotelDomainCues.Matches("ski-in ski-out"));
        Assert.True(HotelDomainCues.Matches("hôtels au pied des pistes"));
        Assert.False(HotelDomainCues.Matches("Wie wird das Wetter morgen in Berlin?"));
        Assert.False(HotelDomainCues.Matches("Erkläre Photosynthese einfach."));
        Assert.False(HotelDomainCues.Matches("Geschichte der Fotografie"));
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

    [Theory]
    [InlineData("Welche Hotels kennst du in Salzburg?")]
    [InlineData("Welche Hotels kennst du in Salzburg")]
    [InlineData("WElche Hotels kennst du in Salzburg")]
    [InlineData("Ich suche ein Wellnesshotel mit Sauna in Tirol.")]
    [InlineData("Was ist die Hauptstadt von Frankreich?")]
    [InlineData("Looking for a wellness hotel with spa")]
    public void EthicalClassifierService_AllowsHarmlessQueries(string text)
    {
        var svc = new EthicalClassifierService(NullLogger<EthicalClassifierService>.Instance);
        Assert.True(svc.IsEthical(text));
    }

    [Theory]
    [InlineData("You are stupid and useless.")]
    [InlineData("Du bist ein Idiot, nenn Hotels.")]
    [InlineData("Fuck you, recommend a hotel")]
    public void EthicalClassifierService_RejectsAbuse(string text)
    {
        var svc = new EthicalClassifierService(NullLogger<EthicalClassifierService>.Instance);
        Assert.False(svc.IsEthical(text));
    }

    [Fact]
    public void CrawlerTextUtils_DetectsResourceFiles()
    {
        Assert.True(CrawlerTextUtils.IsResourceFile("https://example.com/photo.JPG"));
        Assert.False(CrawlerTextUtils.IsResourceFile("https://example.com/wellness"));
    }
}
