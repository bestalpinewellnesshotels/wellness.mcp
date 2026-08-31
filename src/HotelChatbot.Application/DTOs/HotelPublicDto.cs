namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Öffentliche Hotel-Metadaten für MCP/API (ohne interne Felder wie ApiKey).
/// Leere Felder werden als "not available" ausgegeben.
/// </summary>
public class HotelPublicDto
{
    public required string HotelId { get; set; }
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required string Region { get; set; }
    public required string Country { get; set; }
    public required string OfficialUrl { get; set; }
    public required string SourceUrl { get; set; }
    public required string EditorialReviewStatus { get; set; }
    public string? EditorialReviewedAt { get; set; }
    public List<string> Categories { get; set; } = new();

    public const string NotAvailable = "not available";

    public static HotelPublicDto FromHotel(Domain.Entities.Hotel hotel)
    {
        return new HotelPublicDto
        {
            HotelId = hotel.HotelId,
            Name = hotel.Name,
            Location = Display(hotel.Location),
            Region = Display(hotel.Region),
            Country = Display(hotel.Country),
            OfficialUrl = Display(hotel.ResolveOfficialUrl()),
            SourceUrl = Display(hotel.SourceUrl),
            EditorialReviewStatus = Display(hotel.EditorialReviewStatus),
            EditorialReviewedAt = hotel.EditorialReviewedAt?.ToString("yyyy-MM-dd"),
            Categories = hotel.Categories?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>()
        };
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? NotAvailable : value.Trim();
}
