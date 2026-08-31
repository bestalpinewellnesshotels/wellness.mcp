namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Lokaler Intent-Klassifizierer: Hotel/Wellness-Anfrage vs. Out-of-Scope.
/// </summary>
public interface IIntentClassifier
{
    /// <summary>
    /// true = Anfrage ist hotel-/wellness-relevant.
    /// </summary>
    bool IsHotelWellnessQuery(string text);
}
