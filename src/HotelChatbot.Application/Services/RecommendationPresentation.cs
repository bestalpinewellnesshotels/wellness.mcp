using System.Text;
using System.Text.RegularExpressions;
using HotelChatbot.Application.DTOs;
using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Präsentation: Katalog-Erkennung, Quellen mit Score, kompakte Hotel-Listen.
/// </summary>
public static partial class RecommendationPresentation
{
    public const int MaxSourcesPerHotel = 2;
    public const int MaxPrimarySourceLines = 8;

    /// <summary>
    /// Allgemeine Katalog-Fragen („Welche Hotels kennst du…“) ohne starke Filter.
    /// </summary>
    public static bool IsBroadCatalogQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim().ToLowerInvariant();

        // Starke Filter → normale Vektorsuche
        if (HasStrongFilters(t))
            return false;

        return BroadCatalogRegex().IsMatch(t);
    }

    public static bool IsMoreSourcesRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim().ToLowerInvariant();
        return MoreSourcesRegex().IsMatch(t);
    }

    private static bool HasStrongFilters(string lower)
    {
        string[] markers =
        [
            "sauna", "pool", "whirlpool", "spa ", " spa", "massage", "yoga", "adults",
            "hund", "dog", "pet", "ski", "schi", "halbpension", "all inclusive",
            "familie", "family", "kinder", "children", "budget", "preis", "günstig",
            "luxus", "luxury", "therme", "golf", "e-auto", "ladestation", "elektro"
        ];
        return markers.Any(m => lower.Contains(m, StringComparison.Ordinal));
    }

    public static List<Hotel> Shuffle(IEnumerable<Hotel> hotels, Random? rng = null)
    {
        var list = hotels.Where(h => h.IsActive).ToList();
        rng ??= Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    public static string BuildCatalogAnswer(
        IReadOnlyList<Hotel> shuffled,
        string language,
        string? regionFilter = null)
    {
        var sb = new StringBuilder();
        var hasRegion = !string.IsNullOrWhiteSpace(regionFilter);
        if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(hasRegion
                ? $"Here are the BestWellness hotels in {regionFilter} in our database (random order):"
                : "Here are all BestWellness hotels in our database (random order):");
            sb.AppendLine();
            if (shuffled.Count == 0)
            {
                sb.Append(hasRegion
                    ? $"I currently have no hotels listed for {regionFilter}. Try another region or ask without a region filter."
                    : "I currently have no hotels in the database.");
                return sb.ToString();
            }

            var i = 1;
            foreach (var h in shuffled)
            {
                var loc = string.Join(", ", new[] { h.Location, h.Region }.Where(s => !string.IsNullOrWhiteSpace(s)));
                sb.AppendLine(string.IsNullOrWhiteSpace(loc)
                    ? $"{i}. {h.Name} (id: {h.HotelId})"
                    : $"{i}. {h.Name} — {loc} (id: {h.HotelId})");
                i++;
            }
            sb.AppendLine();
            sb.Append("Which hotel are you interested in? Tell me the name or hotelId, or describe what you need (e.g. spa, adults only, dogs).");
        }
        else
        {
            sb.AppendLine(hasRegion
                ? $"Hier sind die BestWellness-Hotels in {regionFilter} in unserer Datenbank (zufällige Reihenfolge):"
                : "Hier sind alle BestWellness-Hotels in unserer Datenbank (zufällige Reihenfolge):");
            sb.AppendLine();
            if (shuffled.Count == 0)
            {
                sb.Append(hasRegion
                    ? $"Für {regionFilter} sind derzeit keine Hotels hinterlegt. Versuchen Sie eine andere Region oder fragen Sie ohne Regionsfilter."
                    : "In der Datenbank sind derzeit keine Hotels hinterlegt.");
                return sb.ToString();
            }

            var i = 1;
            foreach (var h in shuffled)
            {
                var loc = string.Join(", ", new[] { h.Location, h.Region }.Where(s => !string.IsNullOrWhiteSpace(s)));
                sb.AppendLine(string.IsNullOrWhiteSpace(loc)
                    ? $"{i}. {h.Name} (id: {h.HotelId})"
                    : $"{i}. {h.Name} — {loc} (id: {h.HotelId})");
                i++;
            }
            sb.AppendLine();
            sb.Append("Für welches Hotel interessieren Sie sich? Nennen Sie Name oder hotelId — oder beschreiben Sie genauer, was Sie suchen (z. B. Sauna, Adults only, hundefreundlich).");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Baut Quellen nur für Hotels, die in der Antwort vorkommen; max. 2 URLs/Hotel, nach Score.
    /// </summary>
    public static (
        string MarkdownSection,
        List<SourceCitationDto> Primary,
        List<SourceCitationDto> Additional
    ) BuildWeightedSources(
        string answerText,
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> resultsByHotel,
        Dictionary<string, Hotel> hotelDetails,
        string language,
        int maxPerHotel = MaxSourcesPerHotel,
        int maxPrimaryLines = MaxPrimarySourceLines)
    {
        var mentioned = hotelDetails.Values
            .Where(h => AnswerMentionsHotel(answerText, h))
            .Select(h => h.HotelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Fallback: wenn der LLM Namen anders schreibt, Top-Hotels aus results nutzen
        if (mentioned.Count == 0)
        {
            mentioned = resultsByHotel.Keys
                .Where(hotelDetails.ContainsKey)
                .OrderByDescending(id => resultsByHotel[id].DefaultIfEmpty().Max(x => x.Score))
                .Take(3)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var allCitations = new List<SourceCitationDto>();
        foreach (var hotelId in mentioned)
        {
            if (!resultsByHotel.TryGetValue(hotelId, out var chunks) ||
                !hotelDetails.TryGetValue(hotelId, out var hotel))
                continue;

            var bestByUrl = chunks
                .Where(c => !string.IsNullOrWhiteSpace(c.Chunk.SourceUrl))
                .GroupBy(c => c.Chunk.SourceUrl.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new SourceCitationDto
                {
                    HotelId = hotelId,
                    HotelName = hotel.Name,
                    Url = g.Key,
                    Score = g.Max(x => x.Score)
                })
                .OrderByDescending(c => c.Score)
                .Take(maxPerHotel)
                .ToList();

            allCitations.AddRange(bestByUrl);
        }

        allCitations = allCitations
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.HotelName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = allCitations.Take(maxPrimaryLines).ToList();
        var additional = allCitations.Skip(maxPrimaryLines).ToList();

        if (primary.Count == 0)
            return (string.Empty, primary, additional);

        var header = language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "\n\n---\n**Sources:**"
            : "\n\n---\n**Quellen:**";

        var lines = primary.Select(c =>
            $"- {c.HotelName} ({c.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}): {c.Url}");

        var section = header + "\n" + string.Join("\n", lines);
        if (additional.Count > 0)
        {
            section += language.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? "\n\nMore sources available — reply with “more sources”."
                : "\n\nWeitere Quellen verfügbar — antworten Sie mit „weitere Quellen“.";
        }

        return (section, primary, additional);
    }

    public static bool AnswerMentionsHotel(string answer, Hotel hotel)
    {
        if (string.IsNullOrWhiteSpace(answer) || hotel is null)
            return false;
        if (answer.Contains(hotel.HotelId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(hotel.Name) &&
            answer.Contains(hotel.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        // Kurzname ohne „Hotel “-Präfix
        var shortName = hotel.Name.Replace("Hotel ", "", StringComparison.OrdinalIgnoreCase).Trim();
        return shortName.Length >= 4 &&
               answer.Contains(shortName, StringComparison.OrdinalIgnoreCase);
    }

    public static List<(string HotelId, string Name, double Score)> SummarizeHotelScores(
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> results,
        Dictionary<string, Hotel> hotelDetails)
    {
        return results
            .Where(kvp => hotelDetails.ContainsKey(kvp.Key) && kvp.Value.Count > 0)
            .Select(kvp => (
                HotelId: kvp.Key,
                Name: hotelDetails[kvp.Key].Name,
                Score: kvp.Value.Max(x => x.Score)))
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    [GeneratedRegex(
        @"\b(" +
        @"kennst\s+du\s+hotels?|" +
        @"welche\s+hotels?\s+(kennst|gibt|habt|sind)|" +
        @"welche\s+hotels?\b|" +
        @"zeig\s+mir\s+(alle\s+)?hotels?|" +
        @"alle\s+hotels?|" +
        @"list(e)?\s+(all\s+)?hotels?|" +
        @"which\s+hotels?\s+(do\s+you\s+know|are\s+there|have\s+you)|" +
        @"what\s+hotels?\s+(do\s+you\s+know|are\s+available)|" +
        @"show\s+me\s+(all\s+)?hotels?|" +
        @"hotels?\s+in\s+(der\s+datenbank|your\s+database)" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BroadCatalogRegex();

    [GeneratedRegex(
        @"\b(weitere\s+quellen|more\s+sources|additional\s+sources)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoreSourcesRegex();
}
