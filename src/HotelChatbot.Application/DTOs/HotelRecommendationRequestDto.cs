namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Request DTO für Hotel-Empfehlungen basierend auf Nutzeranforderungen.
/// </summary>
public class HotelRecommendationRequestDto
{
    /// <summary>
    /// Die Anforderungen/Wünsche des Nutzers als Text
    /// (z.B. "Hotel mit Pool, Adults Only, E-Auto-Ladestation, keine Hunde")
    /// </summary>
    public required string Requirements { get; set; }

    /// <summary>
    /// Maximale Anzahl der zu empfehlenden Hotels (wird vom Admin über config.search.max_results gesteuert)
    /// </summary>
    public int MaxResults { get; set; } = 3;

    /// <summary>
    /// Sprache der Antwort. Nur Tie-Breaker bei Mehrdeutigkeit, kein harter Override.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Optional: Session-ID für Konversations-Kontext
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Minimaler Confidence Score für die Vektorsuche (0.0 - 1.0, Standard: 0.45)
    /// </summary>
    public double MinConfidence { get; set; } = 0.45;
}
