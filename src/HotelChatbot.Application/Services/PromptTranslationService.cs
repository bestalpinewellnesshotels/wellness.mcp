using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Übersetzt deutsche System-Prompts ins Englische, um die LLM-Qualität zu maximieren.
/// Nutzt das bereits konfigurierte Chat-Completion-Modell.
/// Die Übersetzung erfolgt nur einmalig beim Speichern — nicht bei jedem Request.
/// </summary>
public class PromptTranslationService : IPromptTranslationService
{
    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<PromptTranslationService> _logger;

    private const string TranslationSystemPrompt =
        "You are a professional translator. " +
        "Translate the following text from German to English. " +
        "If the text is already in English, return it unchanged. " +
        "Preserve all placeholders exactly as written (e.g. {language}, {context}, {userQuery}). " +
        "Output ONLY the translated text — no explanation, no preamble, no markdown.";

    public PromptTranslationService(
        IChatCompletionService chatCompletion,
        ILogger<PromptTranslationService> logger)
    {
        _chatCompletion = chatCompletion;
        _logger = logger;
    }

    public async Task<string> TranslateToEnglishAsync(string germanText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(germanText))
            return germanText;

        // Reine Konfigurationswerte (Zahlen) benötigen keine Übersetzung
        if (int.TryParse(germanText.Trim(), out _))
            return germanText;

        _logger.LogInformation("Übersetze Text: {Preview}... ({Length} Zeichen)", 
            germanText.Length > 100 ? germanText.Substring(0, 100) : germanText, 
            germanText.Length);

        var result = await _chatCompletion.GenerateResponseAsync(
            systemPrompt: TranslationSystemPrompt,
            context: string.Empty,
            userQuery: germanText,
            conversationHistory: [],
            userMessageTemplate: "{userQuery}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Übersetzung erhalten: {Preview}... ({Length} Zeichen)", 
            result.Length > 100 ? result.Substring(0, 100) : result, 
            result.Length);

        return result.Trim();
    }
}
