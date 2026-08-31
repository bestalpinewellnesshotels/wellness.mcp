using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Language;
using HotelChatbot.Infrastructure.Classifiers.Language;

namespace HotelChatbot.Tests;

public class ConversationLanguagePolicyTests
{
    [Fact]
    public void FirstTurn_CloseScores_AsksInThoseLanguages()
    {
        var detection = Ranked(
            ("de", 0.34),
            ("en", 0.32),
            ("nl", 0.28),
            ("fr", 0.03));
        detection = WithAmbiguous(detection);

        var decision = ConversationLanguagePolicy.Resolve(detection, null, "Hotels in Salzburg");

        Assert.True(decision.NeedsClarification);
        Assert.Equal("clarify", decision.Source);
        Assert.Contains("Deutsch", decision.ClarificationMessage);
        Assert.Contains("English", decision.ClarificationMessage);
        Assert.Contains("Nederlands", decision.ClarificationMessage);
        Assert.DoesNotContain("français", decision.ClarificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["de", "en", "nl"], decision.State.PendingCodes);
        Assert.Equal("Hotels in Salzburg", decision.State.PendingQuery);
    }

    [Fact]
    public void FirstTurn_ClearGerman_LocksGerman()
    {
        var detection = Ranked(("de", 0.88), ("en", 0.06), ("nl", 0.02));

        var decision = ConversationLanguagePolicy.Resolve(
            detection, null, "Ich suche ein Wellnesshotel mit Sauna in Tirol.");

        Assert.False(decision.NeedsClarification);
        Assert.Equal("de", decision.Language);
        Assert.Equal("de", decision.State.LockedLanguage);
        Assert.True(decision.State.FirstTurnUnambiguous);
        Assert.Equal("detected", decision.Source);
    }

    [Fact]
    public void FollowUp_LockedGermanStillHighButNotTop_KeepsGerman()
    {
        var locked = new ConversationLanguageState
        {
            LockedLanguage = "de",
            FirstTurnUnambiguous = true
        };
        var detection = Ranked(("nl", 0.38), ("en", 0.30), ("de", 0.22));

        var decision = ConversationLanguagePolicy.Resolve(detection, locked, "Hotels in Salzburg");

        Assert.False(decision.NeedsClarification);
        Assert.Equal("de", decision.Language);
        Assert.Equal("sticky", decision.Source);
    }

    [Fact]
    public void FollowUp_ClearLanguageSwitch_UsesNewLanguage()
    {
        var locked = new ConversationLanguageState
        {
            LockedLanguage = "de",
            FirstTurnUnambiguous = true
        };
        var detection = Ranked(("fr", 0.91), ("de", 0.03), ("en", 0.02));

        var decision = ConversationLanguagePolicy.Resolve(
            detection, locked, "Je cherche un hôtel wellness avec spa.");

        Assert.Equal("fr", decision.Language);
        Assert.Equal("fr", decision.State.LockedLanguage);
        Assert.Equal("switched", decision.Source);
    }

    [Fact]
    public void UnrecognizedScript_AnswersEnglish_DoesNotLock()
    {
        var detection = new LanguageDetectionDetails
        {
            IsUnrecognizedScript = true,
            IsUnknown = true,
            Reason = "Nicht unterstützte Schrift"
        };

        var decision = ConversationLanguagePolicy.Resolve(detection, null, "京都のホテルを探しています");

        Assert.Equal("en", decision.Language);
        Assert.False(decision.NeedsClarification);
        Assert.Null(decision.State.LockedLanguage);
        Assert.Equal("unrecognized_en", decision.Source);
    }

    [Fact]
    public void UnknownLatin_FallsBackToGerman()
    {
        var detection = LanguageDetectionDetails.Empty("Zu wenig Text für eine zuverlässige Erkennung.");

        var decision = ConversationLanguagePolicy.Resolve(detection, null, "ok");

        Assert.Equal("de", decision.Language);
        Assert.Equal("fallback_de", decision.Source);
        Assert.Null(decision.State.LockedLanguage);
    }

    [Fact]
    public void PendingClarification_Deutsch_UsesPendingQuery()
    {
        var pending = new ConversationLanguageState
        {
            PendingCodes = ["de", "en", "nl"],
            PendingQuery = "Hotels in Salzburg"
        };

        var decision = ConversationLanguagePolicy.Resolve(
            LanguageDetectionDetails.Empty("n/a"), pending, "Deutsch");

        Assert.False(decision.NeedsClarification);
        Assert.Equal("de", decision.Language);
        Assert.Equal("de", decision.State.LockedLanguage);
        Assert.Equal("Hotels in Salzburg", decision.TextToProcess);
        Assert.Equal("choice", decision.Source);
        Assert.Null(decision.State.PendingCodes);
    }

    [Fact]
    public void PendingClarification_EnglishAlias_LocksEnglish()
    {
        var pending = new ConversationLanguageState
        {
            PendingCodes = ["de", "en", "nl"],
            PendingQuery = "Hotels in Salzburg"
        };

        var decision = ConversationLanguagePolicy.Resolve(
            Ranked(("en", 0.4), ("de", 0.3)), pending, "English please");

        Assert.Equal("en", decision.Language);
        Assert.Equal("Hotels in Salzburg", decision.TextToProcess);
    }

    [Fact]
    public void JapaneseClassifier_MarksUnrecognizedScript()
    {
        var clf = LanguageClassifier.Train(CorpusLoader.LoadTraining());
        var result = clf.Classify("京都のホテルを探しています");
        Assert.True(result.IsUnrecognizedScript);
        Assert.True(ScriptFilter.IsUnsupportedScript("京都のホテルを探しています"));
    }

    [Fact]
    public void SharedPhrase_PeakedDutchSoftmax_StillAsksDeEnNl()
    {
        var detection = Ranked(("nl", 0.97), ("de", 0.03), ("rm", 0.00));
        var decision = ConversationLanguagePolicy.Resolve(detection, null, "Hotels in Salzburg");
        Assert.True(decision.NeedsClarification);
        Assert.Equal(["de", "en", "nl"], decision.State.PendingCodes);
    }

    [Fact]
    public void SharedPhrase_HotelsInSalzburg_AsksClarification()
    {
        var clf = LanguageClassifier.Train(CorpusLoader.LoadTraining());
        var details = ToDetails(clf.Classify("Hotels in Salzburg"));
        var decision = ConversationLanguagePolicy.Resolve(details, null, "Hotels in Salzburg");
        Assert.True(decision.NeedsClarification, RankedPreview(details));
        Assert.Contains("Deutsch", decision.ClarificationMessage);
        Assert.Contains("English", decision.ClarificationMessage);
        Assert.Contains("Nederlands", decision.ClarificationMessage);
    }

    [Fact]
    public void SharedPhrase_AfterClearGerman_StaysGerman()
    {
        var clf = LanguageClassifier.Train(CorpusLoader.LoadTraining());
        var first = ToDetails(clf.Classify("Ich suche ein Wellnesshotel mit Sauna in Tirol."));
        var locked = ConversationLanguagePolicy.Resolve(first, null, "Ich suche ein Wellnesshotel mit Sauna in Tirol.");
        Assert.Equal("de", locked.Language);
        Assert.Equal("de", locked.State.LockedLanguage);

        var follow = ToDetails(clf.Classify("Hotels in Salzburg"));
        var decision = ConversationLanguagePolicy.Resolve(follow, locked.State, "Hotels in Salzburg");
        Assert.Equal("de", decision.Language);
        Assert.Equal("sticky", decision.Source);
    }

    private static LanguageDetectionDetails ToDetails(ClassificationResult result) =>
        new()
        {
            TopCode = result.Code,
            Confidence = result.Confidence,
            IsUnknown = result.IsUnknown,
            IsAmbiguous = result.IsAmbiguous,
            IsUnrecognizedScript = result.IsUnrecognizedScript,
            Ranked = result.Top.Select(t => new LanguageScore(t.Code, t.Name, t.Probability)).ToList()
        };

    private static string RankedPreview(LanguageDetectionDetails details) =>
        string.Join(", ", details.Ranked.Take(4).Select(s => $"{s.Code}:{s.Probability:0.00}"));

    private static LanguageDetectionDetails Ranked(params (string Code, double Probability)[] items)
    {
        var ranked = items
            .Select(item => new LanguageScore(item.Code, item.Code, item.Probability))
            .ToList();
        return new LanguageDetectionDetails
        {
            TopCode = ranked[0].Code,
            Confidence = ranked[0].Probability,
            Ranked = ranked,
            IsUnknown = false,
            IsAmbiguous = false
        };
    }

    private static LanguageDetectionDetails WithAmbiguous(LanguageDetectionDetails details) =>
        new()
        {
            TopCode = details.TopCode,
            Confidence = details.Confidence,
            Ranked = details.Ranked,
            IsUnknown = false,
            IsAmbiguous = true
        };
}
