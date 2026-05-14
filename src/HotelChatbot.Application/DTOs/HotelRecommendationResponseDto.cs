namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Response DTO für Hotel-Empfehlungen.
/// </summary>
public class HotelRecommendationResponseDto
{
    /// <summary>
    /// Erfolg oder Fehler
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Fehlermeldung (falls Success = false)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Vollständig formatierte finale Antwort für direkte Verwendung durch LLM
    /// Diese Antwort MUSS 1:1 ohne Interpretation übernommen werden
    /// </summary>
    public string? FinalAnswer { get; set; }

    /// <summary>
    /// Typ der Antwort: "recommendations", "no_results", "error"
    /// </summary>
    public string ResponseType { get; set; } = "recommendations";

    /// <summary>
    /// Hinweis zur Datenquelle (ausschließlich interne Datenbank)
    /// </summary>
    public string DataSourceDisclaimer { get; set; } = "Diese Empfehlungen basieren ausschließlich auf Hotels in unserer internen Datenbank.";

    /// <summary>
    /// Die Anforderungen des Nutzers (Echo)
    /// </summary>
    public string? Requirements { get; set; }

    /// <summary>
    /// Liste der empfohlenen Hotels mit Details
    /// </summary>
    public List<HotelRecommendationDto> Recommendations { get; set; } = new();

    /// <summary>
    /// Zusammenfassende Empfehlung als Text
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Session-ID für Konversations-Fortsetzung
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Zeitstempel
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Einzelne Hotel-Empfehlung mit Details.
/// </summary>
public class HotelRecommendationDto
{
    /// <summary>
    /// Hotel-ID
    /// </summary>
    public required string HotelId { get; set; }

    /// <summary>
    /// Hotel-Name
    /// </summary>
    public required string HotelName { get; set; }

    /// <summary>
    /// Hotel-Domain
    /// </summary>
    public required string Domain { get; set; }

    /// <summary>
    /// Match-Score (0.0 - 1.0) - wie gut das Hotel zu den Anforderungen passt
    /// </summary>
    public double MatchScore { get; set; }

    /// <summary>
    /// Begründung warum dieses Hotel empfohlen wird
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Relevante Features/Highlights die zur Anforderung passen
    /// </summary>
    public List<string> MatchingFeatures { get; set; } = new();

    /// <summary>
    /// Quellen (Content-Chunks) die zur Empfehlung beigetragen haben
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// Rang/Position in der Empfehlungs-Liste (1 = beste Empfehlung)
    /// </summary>
    public int Rank { get; set; }
}
