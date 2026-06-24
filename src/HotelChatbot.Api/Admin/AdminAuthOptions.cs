namespace HotelChatbot.Api.Admin;

public class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public string AdminPassword { get; set; } = string.Empty;
    public string HotelPassword { get; set; } = string.Empty;
    public string TokenSecret { get; set; } = string.Empty;
    public int TokenLifetimeDays { get; set; } = 30;
}

public static class AdminRoles
{
    public const string Admin = "admin";
    public const string Hotel = "hotel";
}
