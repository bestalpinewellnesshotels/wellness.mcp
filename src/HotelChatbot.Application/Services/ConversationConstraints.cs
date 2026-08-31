using System.Text.Json;
using System.Text.RegularExpressions;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Gesprächskontext für Retrieval: Region und Fokus-Hotel aus User-Text / Session.
/// History geht an den Answer-LLM; ohne diese Anreicherung driftet die Vektorsuche.
/// </summary>
public sealed class ConversationConstraints
{
    public string? Region { get; set; }
    public string? FocusHotelId { get; set; }
    public string? FocusHotelName { get; set; }

    public bool HasRegion => !string.IsNullOrWhiteSpace(Region);
    public bool HasFocus => !string.IsNullOrWhiteSpace(FocusHotelId);
}

public static partial class ConversationConstraintHelper
{
    public const string SessionPrefix = "__conversation_constraints__:";

    private static readonly (string Canonical, string[] Aliases)[] Regions =
    [
        ("Tirol", ["tirol", "tyrol", "tyrolen"]),
        ("Salzburg", ["salzburg", "salzburger land", "salzburgerland"]),
        ("Kärnten", ["kärnten", "karnten", "carinthia", "carinthien"]),
        ("Steiermark", ["steiermark", "styria"]),
        ("Vorarlberg", ["vorarlberg"]),
        ("Oberösterreich", ["oberösterreich", "oberoesterreich", "upper austria"]),
        ("Niederösterreich", ["niederösterreich", "niederoesterreich", "lower austria"]),
        ("Wien", ["wien", "vienna"]),
        ("Bayern", ["bayern", "bavaria", "bayerischen alpen"]),
        ("Südtirol", ["südtirol", "sudtirol", "south tyrol", "alto adige"]),
        ("Trentino", ["trentino"])
    ];

    public static ConversationConstraints? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ConversationConstraints>(json);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(ConversationConstraints c) =>
        JsonSerializer.Serialize(c);

    public static string? ExtractRegion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();

        foreach (var (canonical, aliases) in Regions)
        {
            foreach (var alias in aliases)
            {
                if (Regex.IsMatch(lower, $@"\b{Regex.Escape(alias)}\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return canonical;
            }
        }

        return null;
    }

    /// <summary>
    /// Einziges Hotel, dessen Name (oder Kurzname) die Nachricht klar meint.
    /// </summary>
    public static Hotel? MatchSingleHotelByName(string? text, IEnumerable<Hotel> hotels)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length < 3) return null;

        var matches = new List<Hotel>();
        foreach (var h in hotels.Where(h => h.IsActive && !string.IsNullOrWhiteSpace(h.Name)))
        {
            if (t.Contains(h.HotelId, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(h);
                continue;
            }

            var name = h.Name.Trim();
            var shortName = name.Replace("Hotel ", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (name.Length >= 3 && t.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(h);
                continue;
            }

            if (shortName.Length >= 4 &&
                (t.Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
                 WordBoundaryContains(t, shortName)))
            {
                matches.Add(h);
            }
        }

        matches = matches
            .DistinctBy(h => h.HotelId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            var ordered = matches.OrderByDescending(h => h.Name.Length).ToList();
            if (ordered[0].Name.Length > ordered[1].Name.Length)
                return ordered[0];
        }

        return null;
    }

    public static bool IsDeicticHotelReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DeixisRegex().IsMatch(text.Trim());
    }

    public static bool IsMultiHotelQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return MultiHotelRegex().IsMatch(text.Trim());
    }

    public static bool HotelMatchesRegion(Hotel hotel, string? region)
    {
        if (string.IsNullOrWhiteSpace(region) || hotel is null) return true;
        var needle = region.Trim();
        return FieldContains(hotel.Region, needle)
               || FieldContains(hotel.Location, needle)
               || FieldContains(hotel.Country, needle)
               || FieldContains(hotel.Domain, needle)
               || FieldContains(hotel.HotelId, needle);
    }

    public static List<Hotel> FilterByRegion(IEnumerable<Hotel> hotels, string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return hotels.Where(h => h.IsActive).ToList();
        return hotels.Where(h => h.IsActive && HotelMatchesRegion(h, region)).ToList();
    }

    /// <summary>
    /// Merged Session-Constraints mit aktueller Nachricht.
    /// Neue Region überschreibt; Namensmatch setzt Fokus; Multi-Hotel-Fragen lockern den Fokus.
    /// </summary>
    public static ConversationConstraints Merge(
        ConversationConstraints? existing,
        string userMessage,
        IReadOnlyList<Hotel> hotels)
    {
        var result = new ConversationConstraints
        {
            Region = existing?.Region,
            FocusHotelId = existing?.FocusHotelId,
            FocusHotelName = existing?.FocusHotelName
        };

        var regionInMessage = ExtractRegion(userMessage);
        if (regionInMessage != null)
            result.Region = regionInMessage;

        var named = MatchSingleHotelByName(userMessage, hotels);
        if (named != null)
        {
            result.FocusHotelId = named.HotelId;
            result.FocusHotelName = named.Name;
        }
        else if (IsMultiHotelQuestion(userMessage) && !IsDeicticHotelReference(userMessage))
        {
            result.FocusHotelId = null;
            result.FocusHotelName = null;
        }

        return result;
    }

    /// <summary>
    /// Hängt Fokus-Hotel (bei Deixis) und Region an die Vektor-Query an.
    /// Bei Deixis+Fokus keine Region (Fokus-Hotel kann außerhalb der Regions-Constraint liegen).
    /// </summary>
    public static string EnrichSearchQuery(string query, ConversationConstraints constraints)
    {
        var q = (query ?? string.Empty).Trim();
        var parts = new List<string>();
        var deicticFocus = constraints.HasFocus && IsDeicticHotelReference(q);

        if (deicticFocus &&
            !string.IsNullOrWhiteSpace(constraints.FocusHotelName) &&
            !q.Contains(constraints.FocusHotelName, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(constraints.FocusHotelName);
        }

        if (!string.IsNullOrWhiteSpace(q))
            parts.Add(q);

        if (constraints.HasRegion && !deicticFocus)
        {
            var joined = string.Join(" ", parts);
            if (!joined.Contains(constraints.Region!, StringComparison.OrdinalIgnoreCase))
                parts.Add(constraints.Region!);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Region-Filter auf Treffer: nicht bei Fokus+Deixis / explizitem Hotel-Namen.
    /// </summary>
    public static bool ShouldFilterResultsByRegion(ConversationConstraints c, string userMessage)
    {
        if (!c.HasRegion) return false;
        if (c.HasFocus && IsDeicticHotelReference(userMessage)) return false;
        if (c.HasFocus &&
            !string.IsNullOrWhiteSpace(c.FocusHotelName) &&
            userMessage.Contains(c.FocusHotelName, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool FieldContains(string? field, string needle) =>
        !string.IsNullOrWhiteSpace(field) &&
        field.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool WordBoundaryContains(string haystack, string needle) =>
        Regex.IsMatch(
            haystack,
            $@"\b{Regex.Escape(needle)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(
        @"\b(" +
        @"dort|dazu|davon|darüber|darueber|dieses\s+hotel|diesem\s+hotel|dem\s+hotel|" +
        @"that\s+hotel|this\s+hotel|there|the\s+hotel" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeixisRegex();

    [GeneratedRegex(
        @"\b(" +
        @"zu\s+welchen\s+hotels?|welche\s+hotels?|in\s+welchen\s+hotels?|" +
        @"which\s+hotels?|what\s+hotels?|among\s+(the\s+)?hotels?" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiHotelRegex();
}
