namespace HotelChatbot.Domain.Enums;

/// <summary>
/// Definiert die möglichen Rollen in einer Chat-Konversation.
/// </summary>
public static class MessageRole
{
    /// <summary>
    /// Nachricht vom Benutzer
    /// </summary>
    public const string User = "user";

    /// <summary>
    /// Nachricht vom Chatbot-Assistenten
    /// </summary>
    public const string Assistant = "assistant";

    /// <summary>
    /// System-Nachricht (z.B. Fehler, Hinweise)
    /// </summary>
    public const string System = "system";
}
