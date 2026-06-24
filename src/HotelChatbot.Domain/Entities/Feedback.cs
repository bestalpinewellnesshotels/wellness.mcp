namespace HotelChatbot.Domain.Entities;

/// <summary>
/// Feedback-Eintrag aus dem Admin-Bereich.
/// </summary>
public class Feedback
{
    public int Id { get; set; }
    public string? HotelId { get; set; }
    public required string Name { get; set; }
    public required string Keyword { get; set; }
    public required string Text { get; set; }
    public string Status { get; set; } = string.Empty;
    public required string CreatedByRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
