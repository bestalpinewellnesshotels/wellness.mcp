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

    /// <summary>
    /// Optionale Pipeline-Schritte (Debug / ChatGPT-Sim).
    /// </summary>
    public List<PipelineTraceStepDto>? PipelineTrace { get; set; }

    /// <summary>
    /// Gesamtdauer der Pipeline in ms (wenn Trace aktiv).
    /// </summary>
    public long? PipelineDurationMs { get; set; }

    /// <summary>Vektor-Suchquery (nach optionalem Translate).</summary>
    public string? VectorQuery { get; set; }

    /// <summary>Quellen in der Antwort (mit Score).</summary>
    public List<SourceCitationDto> CitedSources { get; set; } = new();

    /// <summary>Weitere Quellen (nicht in der Antwort; auf Anfrage / Sim-Button).</summary>
    public List<SourceCitationDto> AdditionalSources { get; set; } = new();

    /// <summary>Hotel-Treffer mit Top-Similarity (Debug / History).</summary>
    public List<HotelScoreDto> HotelScores { get; set; } = new();
}

/// <summary>Hotel-Match mit Similarity für Trace/UI.</summary>
public class HotelScoreDto
{
    public required string HotelId { get; set; }
    public required string HotelName { get; set; }
    public double Score { get; set; }
    public double? DistanceKm { get; set; }
}

/// <summary>
/// Einzelne Hotel-Empfehlung mit Details.
/// </summary>
public class HotelRecommendationDto
{
    public required string HotelId { get; set; }

    public required string HotelName { get; set; }

    public required string Domain { get; set; }

    public string Location { get; set; } = HotelPublicDto.NotAvailable;

    public string Region { get; set; } = HotelPublicDto.NotAvailable;

    public string Country { get; set; } = HotelPublicDto.NotAvailable;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Entfernung in km zum primären Geo-Ziel (nur bei Nähe-Fragen).</summary>
    public double? DistanceKm { get; set; }

    public string OfficialUrl { get; set; } = HotelPublicDto.NotAvailable;

    public string SourceUrl { get; set; } = HotelPublicDto.NotAvailable;

    public string EditorialReviewStatus { get; set; } = HotelPublicDto.NotAvailable;

    public string? EditorialReviewedAt { get; set; }

    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Match-Score (0.0 - 1.0)
    /// </summary>
    public double MatchScore { get; set; }

    public required string Reason { get; set; }

    public List<string> MatchingFeatures { get; set; } = new();

    public List<string> Sources { get; set; } = new();

    public int Rank { get; set; }
}
