using System.Text.RegularExpressions;

namespace HotelChatbot.Application.Services;

public sealed class GeoQueryIntent
{
    public IReadOnlyList<GeoTarget> Targets { get; init; } = [];
    public bool RequiresSkiLiftAtHotel { get; init; }
    public bool WantsNearestSkiArea { get; init; }

    public int PlaceTargetCount => Targets.Count(t => t.Kind == GeoTargetKind.Place);

    public bool IsProximityQuery => Targets.Count > 0;
    public bool ShouldRerank => IsProximityQuery;

    public bool HasConflictingGoals =>
        PlaceTargetCount >= 2
        || (PlaceTargetCount >= 1 && (WantsNearestSkiArea || RequiresSkiLiftAtHotel));
}

public enum GeoTargetKind
{
    Place,
    NearestSkiArea
}

public sealed class GeoTarget
{
    public required string Name { get; init; }
    public required GeoTargetKind Kind { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public GeoPlaceKind? PlaceKind { get; init; }
}

public static partial class GeoQueryParser
{
    public static GeoQueryIntent Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GeoQueryIntent();

        var raw = text.Trim();
        var lower = raw.ToLowerInvariant();
        var proximity = ProximityRegex().IsMatch(lower);
        var skiLiftAtHotel = SkiLiftAtHotelRegex().IsMatch(lower);
        var nearestSki = UnnamedSkiAreaRegex().IsMatch(lower);

        if (!proximity && !skiLiftAtHotel && !nearestSki)
            return new GeoQueryIntent();

        var targets = new List<GeoTarget>();
        if (proximity)
            targets.AddRange(MatchPlaces(lower));

        if (proximity && nearestSki && targets.All(t => t.PlaceKind != GeoPlaceKind.SkiArea))
        {
            targets.Add(new GeoTarget
            {
                Name = "Skigebiet",
                Kind = GeoTargetKind.NearestSkiArea
            });
        }

        // Ski-Lift-am-Haus allein bleibt die bestehende Vektorsuche.
        if (!proximity)
        {
            return new GeoQueryIntent
            {
                RequiresSkiLiftAtHotel = skiLiftAtHotel
            };
        }

        return new GeoQueryIntent
        {
            Targets = targets,
            RequiresSkiLiftAtHotel = skiLiftAtHotel,
            WantsNearestSkiArea = targets.Any(t => t.Kind == GeoTargetKind.NearestSkiArea)
        };
    }

    private static List<GeoTarget> MatchPlaces(string lower)
    {
        var hits = new List<(int Start, int Length, GeoPlace Place)>();
        foreach (var place in GeoCatalog.Places.OrderByDescending(p => p.Aliases.Max(a => a.Length)))
        {
            foreach (var alias in place.Aliases.OrderByDescending(a => a.Length))
            {
                var idx = 0;
                while (idx < lower.Length)
                {
                    var found = lower.IndexOf(alias, idx, StringComparison.Ordinal);
                    if (found < 0) break;
                    if (IsToken(lower, found, alias.Length) &&
                        !hits.Any(h => Overlaps(h.Start, h.Length, found, alias.Length)))
                    {
                        hits.Add((found, alias.Length, place));
                    }

                    idx = found + alias.Length;
                }
            }
        }

        // „nah an Salzburg“ (ohne Land) = Stadt, nicht Bundesland.
        if (!hits.Any(h => h.Place.Name == GeoCatalog.SalzburgCity.Name) &&
            Regex.IsMatch(lower, @"\bsalzburg\b", RegexOptions.CultureInvariant) &&
            !lower.Contains("salzburger land", StringComparison.Ordinal) &&
            !lower.Contains("salzburgerland", StringComparison.Ordinal))
        {
            hits.Add((0, 0, GeoCatalog.SalzburgCity));
        }

        return hits
            .Select(h => h.Place)
            .DistinctBy(p => p.Name)
            .Select(p => new GeoTarget
            {
                Name = p.Name,
                Kind = GeoTargetKind.Place,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                PlaceKind = p.Kind
            })
            .ToList();
    }

    private static bool IsToken(string text, int start, int length)
    {
        if (start > 0 && char.IsLetterOrDigit(text[start - 1])) return false;
        var end = start + length;
        if (end < text.Length && char.IsLetterOrDigit(text[end])) return false;
        return true;
    }

    private static bool Overlaps(int aStart, int aLen, int bStart, int bLen) =>
        aStart < bStart + bLen && bStart < aStart + aLen;

    [GeneratedRegex(
        @"\b(" +
        @"so\s+nah\s+(wie|an)|" +
        @"möglichst\s+nah|" +
        @"geringste\s+entfernung|" +
        @"beste\s+nähe|" +
        @"nähe\s+zu|" +
        @"nah(e|er)?\s+(an|bei|zu)|" +
        @"nah(e|er)?|" +
        @"in\s+der\s+nähe|" +
        @"unweit|" +
        @"nächst(e|en|er|es)?|" +
        @"stadtnah|" +
        @"closest|" +
        @"nearest|" +
        @"near(est)?(\s+to)?|" +
        @"close\s+to|" +
        @"as\s+close\s+as" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProximityRegex();

    [GeneratedRegex(
        @"\b(" +
        @"ski-?in|" +
        @"ski-?out|" +
        @"ski\s+in\s+ski\s+out|" +
        @"skilift\s+vor|" +
        @"ski\s*lift\s+vor|" +
        @"lift\s+vor\s+dem|" +
        @"direkt(er|em)?\s+zugang\s+zum\s+(ski)?lift|" +
        @"ski\s+an\s+der\s+haustür|" +
        @"ski\s+an\s+der\s+haustuer|" +
        @"ski\s+to\s+the\s+door|" +
        @"lift\s+in\s+front" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkiLiftAtHotelRegex();

    [GeneratedRegex(
        @"\b(" +
        @"skigebiet|" +
        @"ski\s*resort|" +
        @"ski\s*area|" +
        @"nähe\s+zu\s+einem\s+skigebiet|" +
        @"nahe\s+(einem\s+)?skigebiet" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnnamedSkiAreaRegex();
}
