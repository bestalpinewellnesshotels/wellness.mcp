namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Eine gewichtete Quellen-URL (Similarity-Score aus dem Retrieval).
/// </summary>
public class SourceCitationDto
{
    public required string HotelId { get; set; }
    public required string HotelName { get; set; }
    public required string Url { get; set; }

    /// <summary>Similarity 0..1</summary>
    public double Score { get; set; }
}
