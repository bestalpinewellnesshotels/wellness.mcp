namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Repräsentiert ein Hotel mit Basis- und redaktionellen Metadaten.
/// </summary>
public class Hotel
{
    public required string HotelId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Primäre Domain (Hostname), z.B. "www.stock.at"
    /// </summary>
    public required string Domain { get; set; }

    public List<string> AllowedDomains { get; set; } = new();

    public string? ApiKey { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ort / Stadt (redaktionell)</summary>
    public string? Location { get; set; }

    /// <summary>Region (redaktionell)</summary>
    public string? Region { get; set; }

    /// <summary>Land (redaktionell)</summary>
    public string? Country { get; set; }

    /// <summary>WGS84-Breite. Fehlt, bis die Geo-Migration/Seed gelaufen ist.</summary>
    public double? Latitude { get; set; }

    /// <summary>WGS84-Länge. Fehlt, bis die Geo-Migration/Seed gelaufen ist.</summary>
    public double? Longitude { get; set; }

    public bool HasCoordinates =>
        Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;

    /// <summary>Offizielle Hotel-URL</summary>
    public string? OfficialUrl { get; set; }

    /// <summary>Kanonische Quellen-/Redaktions-URL</summary>
    public string? SourceUrl { get; set; }

    /// <summary>z.B. approved, draft, unavailable</summary>
    public string? EditorialReviewStatus { get; set; }

    public DateTime? EditorialReviewedAt { get; set; }

    /// <summary>Freigegebene Kategorien, z.B. Wellness, Adults-only</summary>
    public List<string> Categories { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ableitung einer öffentlichen URL wenn OfficialUrl fehlt.
    /// </summary>
    public string? ResolveOfficialUrl()
    {
        if (!string.IsNullOrWhiteSpace(OfficialUrl))
            return OfficialUrl.Trim();
        if (string.IsNullOrWhiteSpace(Domain))
            return null;
        var host = Domain.Trim().TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return host;
        return $"https://{host}";
    }
}
