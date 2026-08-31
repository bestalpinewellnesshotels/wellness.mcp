using HotelChatbot.Application.DTOs;
using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Tests;

public class RecommendationPresentationTests
{
    [Theory]
    [InlineData("Kennst du Hotels in Salzburg?", true)]
    [InlineData("Welche Hotels kennst du?", true)]
    [InlineData("Which hotels do you know?", true)]
    [InlineData("Zeig mir alle Hotels", true)]
    [InlineData("Ich suche ein Hotel mit Sauna in Tirol", false)]
    [InlineData("Adults Only Hotel mit Pool", false)]
    [InlineData("Hundefreundliches Wellnesshotel", false)]
    public void BroadCatalogQuery_Detection(string text, bool expected)
    {
        Assert.Equal(expected, RecommendationPresentation.IsBroadCatalogQuery(text));
    }

    [Theory]
    [InlineData("weitere Quellen", true)]
    [InlineData("more sources please", true)]
    [InlineData("Hotels in Tirol", false)]
    public void MoreSourcesRequest_Detection(string text, bool expected)
    {
        Assert.Equal(expected, RecommendationPresentation.IsMoreSourcesRequest(text));
    }

    [Fact]
    public void BuildWeightedSources_MaxTwoPerMentionedHotel_OrderedByScore()
    {
        var hotel = new Hotel
        {
            HotelId = "hotel_a",
            Name = "Alpenrose",
            Domain = "alpenrose.at",
            IsActive = true
        };
        var other = new Hotel
        {
            HotelId = "hotel_b",
            Name = "Stock",
            Domain = "stock.at",
            IsActive = true
        };
        var details = new Dictionary<string, Hotel>
        {
            [hotel.HotelId] = hotel,
            [other.HotelId] = other
        };
        var results = new Dictionary<string, List<(ContentChunk Chunk, double Score)>>
        {
            [hotel.HotelId] =
            [
                (Chunk("https://a.example/low", hotel.HotelId), 0.40),
                (Chunk("https://a.example/high", hotel.HotelId), 0.90),
                (Chunk("https://a.example/mid", hotel.HotelId), 0.70),
                (Chunk("https://a.example/extra", hotel.HotelId), 0.60)
            ],
            [other.HotelId] =
            [
                (Chunk("https://b.example/x", other.HotelId), 0.95)
            ]
        };

        var answer = "Ich empfehle das Hotel Alpenrose am Achensee.";
        var (section, primary, additional) = RecommendationPresentation.BuildWeightedSources(
            answer, results, details, "de", maxPerHotel: 2, maxPrimaryLines: 8);

        Assert.Contains("Alpenrose", section);
        Assert.DoesNotContain("Stock", section);
        Assert.Equal(2, primary.Count);
        Assert.Equal(0.90, primary[0].Score, 2);
        Assert.Equal(0.70, primary[1].Score, 2);
        Assert.Contains("(0.90)", section);
        Assert.Empty(additional);
    }

    private static ContentChunk Chunk(string url, string hotelId) => new()
    {
        ChunkId = Guid.NewGuid().ToString(),
        HotelId = hotelId,
        SourceUrl = url,
        Content = "x",
        Language = "de",
        CrawledAt = DateTime.UtcNow
    };
}
