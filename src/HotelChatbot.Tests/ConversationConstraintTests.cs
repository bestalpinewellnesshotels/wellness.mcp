using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Tests;

public class ConversationConstraintTests
{
    private static List<Hotel> SampleHotels() =>
    [
        new() { HotelId = "hotel_engel-tirol_com", Name = "Engel", Region = "Tirol", Location = "Achenkirch", IsActive = true, Domain = "engel-tirol.com" },
        new() { HotelId = "hotel_hochschober_com", Name = "Hochschober", Region = "Kärnten", Location = "Turracher Höhe", IsActive = true, Domain = "hochschober.com" },
        new() { HotelId = "hotel_schwarz_at", Name = "Schwarz", Region = "Salzburg", Location = "Hinterglemm", IsActive = true, Domain = "schwarz.at" },
        new() { HotelId = "hotel_alpenrose_at", Name = "Alpenrose", Region = "Tirol", Location = "Niederau", IsActive = true, Domain = "alpenrose.at" }
    ];

    [Theory]
    [InlineData("Welche Hotels kennst du in Tirol?", "Tirol")]
    [InlineData("Hotels in Salzburg", "Salzburg")]
    [InlineData("Something in Carinthia please", "Kärnten")]
    [InlineData("Wellness ohne Ort", null)]
    public void ExtractRegion_FindsCanonical(string text, string? expected)
    {
        Assert.Equal(expected, ConversationConstraintHelper.ExtractRegion(text));
    }

    [Fact]
    public void FilterByRegion_KeepsMatchingHotels()
    {
        var filtered = ConversationConstraintHelper.FilterByRegion(SampleHotels(), "Tirol");
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, h => Assert.Equal("Tirol", h.Region));
    }

    [Fact]
    public void MatchSingleHotelByName_Hochschober()
    {
        var hit = ConversationConstraintHelper.MatchSingleHotelByName("Hochschober", SampleHotels());
        Assert.NotNull(hit);
        Assert.Equal("hotel_hochschober_com", hit!.HotelId);
    }

    [Fact]
    public void EnrichSearchQuery_AddsFocusOnDeixis_SkipsRegion()
    {
        var c = new ConversationConstraints
        {
            Region = "Tirol",
            FocusHotelId = "hotel_hochschober_com",
            FocusHotelName = "Hochschober"
        };

        var enriched = ConversationConstraintHelper.EnrichSearchQuery("Wieviele Zimmer gibt es dort?", c);
        Assert.Contains("Hochschober", enriched, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tirol", enriched, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zimmer", enriched, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnrichSearchQuery_AddsRegionOnMultiHotelFollowUp()
    {
        var c = new ConversationConstraints { Region = "Tirol" };
        var enriched = ConversationConstraintHelper.EnrichSearchQuery(
            "Zu welchen Hotels hast du Informationen ueber die Zimmeranzahl?", c);
        Assert.Contains("Tirol", enriched, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_MultiHotelQuestion_ClearsFocus_KeepsRegion()
    {
        var existing = new ConversationConstraints
        {
            Region = "Tirol",
            FocusHotelId = "hotel_hochschober_com",
            FocusHotelName = "Hochschober"
        };
        var merged = ConversationConstraintHelper.Merge(
            existing,
            "Zu welchen Hotels hast du Informationen ueber die Zimmeranzahl?",
            SampleHotels());

        Assert.Equal("Tirol", merged.Region);
        Assert.Null(merged.FocusHotelId);
    }

    [Fact]
    public void ShouldFilterResultsByRegion_FalseForDeixis()
    {
        var c = new ConversationConstraints
        {
            Region = "Tirol",
            FocusHotelId = "hotel_hochschober_com",
            FocusHotelName = "Hochschober"
        };
        Assert.False(ConversationConstraintHelper.ShouldFilterResultsByRegion(c, "Wieviele Zimmer gibt es dort?"));
        Assert.True(ConversationConstraintHelper.ShouldFilterResultsByRegion(
            c, "Zu welchen Hotels hast du Infos zur Zimmeranzahl?"));
    }

    [Fact]
    public void IsDeicticHotelReference_Dort()
    {
        Assert.True(ConversationConstraintHelper.IsDeicticHotelReference("Wieviele Zimmer gibt es dort?"));
        Assert.False(ConversationConstraintHelper.IsDeicticHotelReference("Welche Hotels in Tirol?"));
    }

    [Fact]
    public void BuildCatalogAnswer_WithRegion()
    {
        var hotels = ConversationConstraintHelper.FilterByRegion(SampleHotels(), "Tirol");
        var text = RecommendationPresentation.BuildCatalogAnswer(hotels, "de", "Tirol");
        Assert.Contains("in Tirol", text);
        Assert.DoesNotContain("Hochschober", text);
        Assert.Contains("Engel", text);
    }
}
