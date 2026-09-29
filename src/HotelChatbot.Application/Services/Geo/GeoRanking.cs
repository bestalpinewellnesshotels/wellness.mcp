using System.Globalization;
using System.Text;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Application.Services;

public sealed class GeoDistance
{
    public required string TargetName { get; init; }
    public required double Kilometers { get; init; }
}

public sealed class GeoHotelScore
{
    public required string HotelId { get; init; }
    public required string HotelName { get; init; }
    public IReadOnlyList<GeoDistance> Distances { get; init; } = [];
    public double PrimaryKm { get; init; }
    public bool HasSkiLiftAtHotel { get; init; }
}

public sealed class GeoRankingResult
{
    public required IReadOnlyList<string> OrderedHotelIds { get; init; }
    public required IReadOnlyList<GeoHotelScore> Scores { get; init; }
    public bool HasConflict { get; init; }
    public required string Briefing { get; init; }
}

public static class GeoRanking
{
    public static GeoRankingResult Rank(
        IReadOnlyList<Hotel> allHotels,
        IReadOnlyCollection<string> vectorHotelIds,
        GeoQueryIntent intent,
        bool amenityMixed,
        int maxResults)
    {
        if (!intent.ShouldRerank || maxResults <= 0)
        {
            return new GeoRankingResult
            {
                OrderedHotelIds = vectorHotelIds.Take(maxResults).ToList(),
                Scores = [],
                HasConflict = false,
                Briefing = string.Empty
            };
        }

        var vectorSet = vectorHotelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = allHotels
            .Where(h => h.IsActive)
            .Where(h => !amenityMixed || vectorSet.Contains(h.HotelId))
            .Where(h => GeoCatalog.TryGetCoordinates(h, out _, out _))
            .ToList();

        if (candidates.Count == 0)
        {
            return new GeoRankingResult
            {
                OrderedHotelIds = vectorHotelIds.Take(maxResults).ToList(),
                Scores = [],
                HasConflict = false,
                Briefing = string.Empty
            };
        }

        var scores = candidates
            .Select(h => ScoreHotel(h, intent))
            .OrderBy(s => s.PrimaryKm)
            .ToList();

        var hasConflict = DetectConflict(scores, intent);
        var ordered = hasConflict
            ? SelectConflictPoles(scores, intent, maxResults)
            : scores.Select(s => s.HotelId).Take(maxResults).ToList();

        if (amenityMixed)
        {
            foreach (var id in vectorHotelIds)
            {
                if (ordered.Count >= maxResults) break;
                if (ordered.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                ordered.Add(id);
            }
        }

        return new GeoRankingResult
        {
            OrderedHotelIds = ordered,
            Scores = scores,
            HasConflict = hasConflict,
            Briefing = BuildBriefing(intent, scores, ordered, hasConflict)
        };
    }

    public static bool IsCloseEnough(GeoHotelScore score, GeoQueryIntent intent)
    {
        foreach (var d in score.Distances)
        {
            var target = intent.Targets.FirstOrDefault(t => t.Name == d.TargetName);
            var limit = target?.PlaceKind == GeoPlaceKind.SkiArea ||
                        target?.Kind == GeoTargetKind.NearestSkiArea
                ? GeoCatalog.CloseToSkiAreaKm
                : GeoCatalog.CloseToCityKm;
            if (d.Kilometers > limit) return false;
        }

        if (intent.RequiresSkiLiftAtHotel && !score.HasSkiLiftAtHotel)
            return false;
        return score.Distances.Count > 0;
    }

    private static GeoHotelScore ScoreHotel(Hotel hotel, GeoQueryIntent intent)
    {
        GeoCatalog.TryGetCoordinates(hotel, out var lat, out var lng);
        var distances = new List<GeoDistance>();
        foreach (var target in intent.Targets)
        {
            double km;
            if (target.Kind == GeoTargetKind.NearestSkiArea)
                km = GeoCatalog.DistanceToNearestSkiAreaKm(lat, lng);
            else if (target.Latitude is { } tLat && target.Longitude is { } tLng)
                km = GeoCatalog.HaversineKm(lat, lng, tLat, tLng);
            else
                continue;
            distances.Add(new GeoDistance { TargetName = target.Name, Kilometers = km });
        }

        var primary = distances.Count == 0 ? double.MaxValue : distances.Min(d => d.Kilometers);
        return new GeoHotelScore
        {
            HotelId = hotel.HotelId,
            HotelName = hotel.Name,
            Distances = distances,
            PrimaryKm = primary,
            HasSkiLiftAtHotel = GeoCatalog.HasSkiLiftAtHotel(hotel.HotelId)
        };
    }

    private static bool DetectConflict(List<GeoHotelScore> scores, GeoQueryIntent intent)
    {
        if (!intent.HasConflictingGoals) return false;
        return !scores.Any(s => IsCloseEnough(s, intent));
    }

    private static List<string> SelectConflictPoles(
        List<GeoHotelScore> scores,
        GeoQueryIntent intent,
        int maxResults)
    {
        var ids = new List<string>();
        var primaryName = intent.Targets.FirstOrDefault(t => t.Kind == GeoTargetKind.Place)?.Name
                          ?? intent.Targets.FirstOrDefault()?.Name;

        GeoHotelScore? closestPrimary = null;
        if (primaryName != null)
        {
            closestPrimary = scores
                .OrderBy(s => KmTo(s, primaryName))
                .FirstOrDefault();
            if (closestPrimary != null) ids.Add(closestPrimary.HotelId);
        }

        if (intent.RequiresSkiLiftAtHotel)
        {
            var ski = scores
                .Where(s => s.HasSkiLiftAtHotel)
                .OrderBy(s => primaryName == null ? s.PrimaryKm : KmTo(s, primaryName))
                .FirstOrDefault();
            if (ski != null && ids.All(id => !id.Equals(ski.HotelId, StringComparison.OrdinalIgnoreCase)))
                ids.Add(ski.HotelId);
        }

        var second = intent.Targets.Skip(1).FirstOrDefault();
        if (second != null)
        {
            var closestSecond = scores
                .OrderBy(s => KmTo(s, second.Name))
                .FirstOrDefault();
            if (closestSecond != null &&
                ids.All(id => !id.Equals(closestSecond.HotelId, StringComparison.OrdinalIgnoreCase)))
                ids.Add(closestSecond.HotelId);
        }

        foreach (var s in scores)
        {
            if (ids.Count >= maxResults) break;
            if (ids.Any(id => id.Equals(s.HotelId, StringComparison.OrdinalIgnoreCase))) continue;
            ids.Add(s.HotelId);
        }

        return ids.Take(maxResults).ToList();
    }

    private static double KmTo(GeoHotelScore score, string targetName)
    {
        var hit = score.Distances.FirstOrDefault(d => d.TargetName == targetName);
        return hit?.Kilometers ?? score.PrimaryKm;
    }

    private static string BuildBriefing(
        GeoQueryIntent intent,
        List<GeoHotelScore> scores,
        IReadOnlyList<string> orderedIds,
        bool hasConflict)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GEO CONSTRAINTS (authoritative for location; do not override with text similarity):");
        var wants = string.Join("; ", intent.Targets.Select(t => t.Name));
        if (!string.IsNullOrWhiteSpace(wants))
            sb.AppendLine($"- User proximity targets: {wants}");
        if (intent.RequiresSkiLiftAtHotel)
            sb.AppendLine("- User also requires a ski lift at / directly in front of the hotel (ski-in).");
        sb.AppendLine("- Ranking used hotel coordinates (approx. km). Rank 1 is geographically closest, not the highest text match.");

        if (hasConflict)
        {
            sb.AppendLine("- CONFLICT: No hotel in the database satisfies all geographic requirements at once.");
            sb.AppendLine("- Explain this tradeoff clearly. Do not present a distant hotel as the closest to a city.");
            sb.AppendLine("- Ask which criterion matters more, or present both poles (closest to the city vs. ski-lift / other target).");
        }

        sb.AppendLine("- Distances for hotels in this result set:");
        foreach (var id in orderedIds)
        {
            var s = scores.FirstOrDefault(x => x.HotelId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (s == null) continue;
            var parts = s.Distances.Select(d =>
                $"{d.Kilometers.ToString("0", CultureInfo.InvariantCulture)} km to {d.TargetName}");
            var ski = s.HasSkiLiftAtHotel ? "ski lift at hotel: yes" : "ski lift at hotel: no";
            sb.AppendLine($"  - {s.HotelName}: {string.Join("; ", parts)}; {ski}");
        }

        return sb.ToString().TrimEnd();
    }
}
