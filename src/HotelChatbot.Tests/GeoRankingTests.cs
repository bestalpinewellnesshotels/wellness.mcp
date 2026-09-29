using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Tests;

public class GeoQueryParserTests
{
    [Theory]
    [InlineData("Ich suche ein Wellnesshotel so nah an Salzburg-Stadt wie möglich", true, "Salzburg-Stadt")]
    [InlineData("nahe Salzburg", true, "Salzburg-Stadt")]
    [InlineData("Which hotel is closest to Zell am See?", true, "Zell am See")]
    [InlineData("Hotels in Salzburg", false, null)]
    [InlineData("Kennst du Hotels in Salzburg?", false, null)]
    [InlineData("Ich suche ein Hotel mit Sauna in Tirol", false, null)]
    public void Parse_ProximityVsRegion(string query, bool proximity, string? target)
    {
        var intent = GeoQueryParser.Parse(query);
        Assert.Equal(proximity, intent.IsProximityQuery);
        if (target != null)
            Assert.Contains(intent.Targets, t => t.Name == target);
    }

    [Fact]
    public void Parse_CityPlusSkiLift_IsConflict()
    {
        var intent = GeoQueryParser.Parse(
            "Ich suche etwas nahe Salzburg-Stadt mit direktem Zugang zum Skilift direkt vor dem Hotel");
        Assert.True(intent.IsProximityQuery);
        Assert.True(intent.RequiresSkiLiftAtHotel);
        Assert.True(intent.HasConflictingGoals);
    }

    [Fact]
    public void Parse_CityPlusSkiArea_IsConflict()
    {
        var intent = GeoQueryParser.Parse(
            "Ich suche ein Hotel nahe Salzburg-Stadt mit Nähe zu einem Skigebiet");
        Assert.True(intent.IsProximityQuery);
        Assert.Contains(intent.Targets, t => t.Name == "Salzburg-Stadt");
        Assert.True(intent.WantsNearestSkiArea);
        Assert.True(intent.HasConflictingGoals);
    }
}

public class GeoRankingTests
{
    private static Hotel H(string id, string name) => new()
    {
        HotelId = id,
        Name = name,
        Domain = "example.at",
        IsActive = true
    };

    private static List<Hotel> Catalog() =>
    [
        H("hotel_gmachl_at", "Genussdorf Gmachl"),
        H("hotel_nesslerhof_at", "Nesslerhof"),
        H("hotel_stock_at", "STOCK resort"),
        H("hotel_uebergossenealm_at", "Übergossene Alm")
    ];

    [Fact]
    public void Rank_ClosestToSalzburg_IsGmachl()
    {
        var intent = GeoQueryParser.Parse("so nah an Salzburg-Stadt wie möglich");
        var ranked = GeoRanking.Rank(Catalog(), [], intent, amenityMixed: false, maxResults: 3);

        Assert.Equal("hotel_gmachl_at", ranked.OrderedHotelIds[0]);
        Assert.False(ranked.HasConflict);
        var gmachl = ranked.Scores.First(s => s.HotelId == "hotel_gmachl_at");
        Assert.True(gmachl.PrimaryKm < 10);
        var nessler = ranked.Scores.First(s => s.HotelId == "hotel_nesslerhof_at");
        Assert.True(nessler.PrimaryKm > 50);
    }

    [Fact]
    public void Rank_CityPlusSkiLift_ReturnsPolesAndConflict()
    {
        var intent = GeoQueryParser.Parse(
            "nahe Salzburg-Stadt mit Skilift vor dem Hotel");
        var ranked = GeoRanking.Rank(Catalog(), [], intent, amenityMixed: false, maxResults: 3);

        Assert.True(ranked.HasConflict);
        Assert.Equal("hotel_gmachl_at", ranked.OrderedHotelIds[0]);
        Assert.Contains("hotel_nesslerhof_at", ranked.OrderedHotelIds);
        Assert.Contains("CONFLICT", ranked.Briefing);
        Assert.DoesNotContain("hotel_gmachl_at",
            ranked.Scores.Where(s => s.HasSkiLiftAtHotel).Select(s => s.HotelId));
    }

    [Fact]
    public void Haversine_GmachlToSalzburgCity_AboutFiveKm()
    {
        var g = GeoCatalog.HotelCoordinates["hotel_gmachl_at"];
        var km = GeoCatalog.HaversineKm(
            g.Lat, g.Lng,
            GeoCatalog.SalzburgCity.Latitude, GeoCatalog.SalzburgCity.Longitude);
        Assert.InRange(km, 3, 8);
    }
}
