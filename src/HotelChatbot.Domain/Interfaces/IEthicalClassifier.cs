namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Lokaler Ethical/Toxicity-Klassifizierer.
/// </summary>
public interface IEthicalClassifier
{
    /// <summary>
    /// true = Nachricht ist ethisch OK; false = REJECT.
    /// </summary>
    bool IsEthical(string text);
}
