namespace HotelChatbot.Application.DTOs;

public class FeedbackDto
{
    public int Id { get; set; }
    public string? HotelId { get; set; }
    public string? HotelName { get; set; }
    public required string Name { get; set; }
    public required string Keyword { get; set; }
    public required string Text { get; set; }
    public string Status { get; set; } = string.Empty;
    public required string CreatedByRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateFeedbackDto
{
    public required string HotelId { get; set; }
    public required string Name { get; set; }
    public required string Keyword { get; set; }
    public required string Text { get; set; }
}

public class UpdateFeedbackDto
{
    public required string HotelId { get; set; }
    public required string Name { get; set; }
    public required string Keyword { get; set; }
    public required string Text { get; set; }
}

public class UpdateFeedbackStatusDto
{
    public required string Status { get; set; }
}
