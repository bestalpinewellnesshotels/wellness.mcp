namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Lokale Spracherkennung (Klassifizierer, kein LLM).
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Erkennt die Sprache und gibt einen ISO-639-1-Code zurück (z.B. "de", "en").
    /// Bei unsicherer Erkennung wird <paramref name="fallback"/> verwendet.
    /// </summary>
    string Detect(string text, string fallback = "de");
}
