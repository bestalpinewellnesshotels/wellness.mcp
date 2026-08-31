namespace HotelChatbot.Infrastructure.Classifiers.Language;

public sealed record LabeledText(string Code, string Text, double Weight = 1.0);
