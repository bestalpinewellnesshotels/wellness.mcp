using HotelChatbot.Domain.Language;

namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Lokale Spracherkennung (Klassifizierer, kein LLM).
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Erkennt die Sprache und gibt einen ISO-639-1-Code zurück (z.B. "de", "en").
    /// Bei unsicherer Erkennung wird <paramref name="fallback"/> verwendet.
    /// Für Crawler und einfache Aufrufer; die Chat-Pipeline nutzt <see cref="Classify"/>.
    /// </summary>
    string Detect(string text, string fallback = "de");

    /// <summary>
    /// Liefert Rangfolge und Konfidenz. Wird nach jeder User-Eingabe aufgerufen.
    /// </summary>
    LanguageDetectionDetails Classify(string text);
}
