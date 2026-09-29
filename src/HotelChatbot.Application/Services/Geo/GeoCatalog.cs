using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Orts-Gazetteer und Hotel-Koordinaten-Fallback (gleiche Werte wie seed-hotel-geo.sql).
/// DB-Koordinaten haben Vorrang, sobald die Seed-SQL gelaufen ist.
/// </summary>
public static class GeoCatalog
{
    public const double CloseToCityKm = 25;
    public const double CloseToSkiAreaKm = 20;

    public static readonly GeoPlace SalzburgCity = new(
        "Salzburg-Stadt",
        GeoPlaceKind.City,
        47.7982,
        13.0465,
        ["salzburg-stadt", "salzburg stadt", "stadt salzburg", "salzburger altstadt",
         "salzburg city", "salzburg old town", "salzburg centre", "salzburg center"]);

    /// <summary>Hotels mit Skilift / Talstation unmittelbar am Haus (nicht nur „Skigebiet in der Region“).</summary>
    private static readonly HashSet<string> SkiLiftAtHotelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "hotel_stock_at",
        "hotel_post-lermoos_at",
        "hotel_nesslerhof_at",
        "hotel_hochschober_com",
        "hotel_uebergossenealm_at",
        "hotel_wartherhof_at",
        "hotel_krallerhof_at"
    };

    public static IReadOnlyList<GeoPlace> Places { get; } =
    [
        SalzburgCity,
        new("Innsbruck", GeoPlaceKind.City, 47.2692, 11.4041, ["innsbruck"]),
        new("München", GeoPlaceKind.City, 48.1374, 11.5755, ["münchen", "muenchen", "munich"]),
        new("Wien", GeoPlaceKind.City, 48.2082, 16.3738, ["wien", "vienna"]),
        new("Zell am See", GeoPlaceKind.City, 47.3233, 12.7967, ["zell am see"]),
        new("Zell am Ziller", GeoPlaceKind.City, 47.2336, 11.8808, ["zell am ziller", "zell im zillertal"]),
        new("Kitzbühel", GeoPlaceKind.City, 47.4467, 12.3922, ["kitzbühel", "kitzbuehel", "kitzbuhel"]),
        new("Fuschl", GeoPlaceKind.City, 47.7964, 13.3028, ["fuschl", "fuschlsee"]),
        new("Mondsee", GeoPlaceKind.City, 47.8564, 13.3508, ["mondsee"]),
        new("Berchtesgaden", GeoPlaceKind.City, 47.6303, 13.0011, ["berchtesgaden", "berchtesgadener land"]),
        new("Bergheim", GeoPlaceKind.City, 47.8397, 13.0228, ["bergheim"]),
        new("Flughafen Salzburg", GeoPlaceKind.Place, 47.7933, 13.0043,
            ["flughafen salzburg", "salzburg airport", "szg"]),
        new("Flughafen Innsbruck", GeoPlaceKind.Place, 47.2602, 11.3439,
            ["flughafen innsbruck", "innsbruck airport"]),
        new("Flughafen München", GeoPlaceKind.Place, 48.3538, 11.7861,
            ["flughafen münchen", "flughafen muenchen", "munich airport", "muc"]),
        new("Skicircus Saalbach", GeoPlaceKind.SkiArea, 47.3910, 12.6360,
            ["saalbach", "hinterglemm", "skicircus", "saalbach-hinterglemm"]),
        new("Großarltal", GeoPlaceKind.SkiArea, 47.2374, 13.2009,
            ["großarl", "grossarl", "großarltal", "grossarltal", "dorfgastein"]),
        new("Hochkönig", GeoPlaceKind.SkiArea, 47.3853, 13.0028,
            ["hochkönig", "hochkoenig", "dienten"]),
        new("Zillertal 3000", GeoPlaceKind.SkiArea, 47.1680, 11.8640,
            ["zillertal 3000", "finkenberg", "mayrhofen", "hintertux"]),
        new("Zillertal Arena", GeoPlaceKind.SkiArea, 47.2336, 11.8808,
            ["zillertal arena"]),
        new("Zugspitz Arena", GeoPlaceKind.SkiArea, 47.4014, 10.8796,
            ["zugspitz arena", "lermoos", "grubigstein"]),
        new("Tannheimertal", GeoPlaceKind.SkiArea, 47.5006, 10.5563,
            ["tannheimertal", "tannheim", "grän", "graen"]),
        new("Turracher Höhe", GeoPlaceKind.SkiArea, 46.9140, 13.8758,
            ["turracher höhe", "turracher hoehe", "turrach"]),
        new("Warth-Schröcken", GeoPlaceKind.SkiArea, 47.2583, 10.1836,
            ["warth", "schröcken", "schroecken", "arlberg"]),
        new("Seefeld", GeoPlaceKind.SkiArea, 47.3306, 11.1870, ["seefeld"]),
        new("Ötztal", GeoPlaceKind.SkiArea, 46.9670, 10.9750,
            ["ötztal", "oetztal", "sölden", "soelden", "längenfeld", "laengenfeld"]),
        new("Alpbachtal", GeoPlaceKind.SkiArea, 47.3986, 11.9428, ["alpbach", "alpbachtal"]),
        new("Achensee/Rofan", GeoPlaceKind.SkiArea, 47.4250, 11.7548,
            ["achensee", "rofan", "maurach"]),
        new("Kitzbühel Ski", GeoPlaceKind.SkiArea, 47.4467, 12.3922, ["kitzbüheler alpen"]),
        new("Kaprun", GeoPlaceKind.SkiArea, 47.2725, 12.7597, ["kaprun"]),
        new("Leogang", GeoPlaceKind.SkiArea, 47.4394, 12.7610, ["leogang"])
    ];

    /// <summary>Fallback, solange hotels.latitude/longitude noch leer sind.</summary>
    public static IReadOnlyDictionary<string, (double Lat, double Lng)> HotelCoordinates { get; } =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["hotel_stock_at"] = (47.154477, 11.823156),
            ["hotel_post-lermoos_at"] = (47.4014, 10.8796),
            ["hotel_nesslerhof_at"] = (47.2374, 13.2009),
            ["hotel_alpbacherhof_at"] = (47.3986, 11.9428),
            ["hotel_waldklause_at"] = (47.0703, 10.9618),
            ["hotel_engel-tirol_com"] = (47.5006, 10.5563),
            ["hotel_hochschober_com"] = (46.9140, 13.8758),
            ["hotel_uebergossenealm_at"] = (47.3853, 13.0028),
            ["hotel_alpenrose_at"] = (47.4250, 11.7548),
            ["hotel_wartherhof_at"] = (47.2583, 10.1836),
            ["hotel_krallerhof_at"] = (47.4394, 12.7610),
            ["hotel_theresa_at"] = (47.2328, 11.8775),
            ["hotel_gmachl_at"] = (47.839749, 13.022782),
            ["hotel_alpenresort-schwarz_at"] = (47.3008, 10.9842),
            ["hotel_seefeld_sacher_com"] = (47.3315, 11.1850)
        };

    public static bool HasSkiLiftAtHotel(string? hotelId) =>
        !string.IsNullOrWhiteSpace(hotelId) && SkiLiftAtHotelIds.Contains(hotelId);

    public static bool TryGetCoordinates(Hotel hotel, out double latitude, out double longitude)
    {
        if (hotel.HasCoordinates)
        {
            latitude = hotel.Latitude!.Value;
            longitude = hotel.Longitude!.Value;
            return true;
        }

        if (HotelCoordinates.TryGetValue(hotel.HotelId, out var fallback))
        {
            latitude = fallback.Lat;
            longitude = fallback.Lng;
            return true;
        }

        latitude = 0;
        longitude = 0;
        return false;
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthKm = 6371.0;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthKm * c;
    }

    public static double DistanceToNearestSkiAreaKm(double latitude, double longitude)
    {
        var min = double.MaxValue;
        foreach (var place in Places)
        {
            if (place.Kind != GeoPlaceKind.SkiArea) continue;
            var d = HaversineKm(latitude, longitude, place.Latitude, place.Longitude);
            if (d < min) min = d;
        }

        return min;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}

public enum GeoPlaceKind
{
    City,
    Place,
    SkiArea
}

public sealed record GeoPlace(
    string Name,
    GeoPlaceKind Kind,
    double Latitude,
    double Longitude,
    string[] Aliases);
