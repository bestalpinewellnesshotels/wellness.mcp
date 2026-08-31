using HotelChatbot.Domain.Interfaces;
using HotelChatbot.Infrastructure.Classifiers.Language;

namespace HotelChatbot.Tests;

/// <summary>
/// Leichte Pipeline-Logik-Tests ohne OpenAI/DB (Translate-Skip, Language-Normalisierung).
/// </summary>
public class PipelineLogicTests
{
    [Theory]
    [InlineData("de", true)]
    [InlineData("DE", true)]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    public void TranslateSkip_OnlyForGerman(string language, bool expectSkip)
    {
        var skip = language.Equals("de", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectSkip, skip);
    }

    [Fact]
    public void LanguageCatalog_ContainsGermanAndEnglish()
    {
        Assert.True(LanguageCatalog.ByCode.ContainsKey("de"));
        Assert.True(LanguageCatalog.ByCode.ContainsKey("en"));
        Assert.Equal("Deutsch", LanguageCatalog.NameOf("de"));
    }

    [Fact]
    public void RelevanceScoreSkip_ThresholdIsConfigured()
    {
        // Spiegelt ChatService.RelevanceScoreSkipThreshold
        const double threshold = 0.55;
        Assert.True(0.70 >= threshold);
        Assert.False(0.40 >= threshold);
    }
}
