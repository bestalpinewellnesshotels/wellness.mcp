using Azure;
using Azure.AI.OpenAI;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.ClientModel;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// OpenAI Chat Completion Service.
/// Implementiert die Kommunikation mit OpenAI für RAG-basierte Antworten.
/// API-Keys werden serverseitig verwaltet.
/// </summary>
public class OpenAIChatCompletionService : IChatCompletionService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIChatCompletionService> _logger;
    private readonly string _modelName;

    public OpenAIChatCompletionService(
        IConfiguration configuration,
        ILogger<OpenAIChatCompletionService> logger)
    {
        _logger = logger;

        var apiKey = configuration["OpenAI:ApiKey"] 
            ?? throw new InvalidOperationException("OpenAI API Key nicht konfiguriert");
        
        _modelName = configuration["OpenAI:DeploymentName"] ?? "gpt-4";

        var client = new AzureOpenAIClient(
            new Uri(configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1"),
            new ApiKeyCredential(apiKey));

        _chatClient = client.GetChatClient(_modelName);
    }

    // ── Eingebaute Fallback-Prompts ──────────────────────────────────────────
    // Werden verwendet wenn die DB keinen aktiven Prompt für den jeweiligen Key liefert.

    private const string FallbackHotelWellnessPrompt =
        "You are a topic relevance classifier for a wellness hotel search assistant.\nDetermine whether the following user message could relate to: hotels, accommodation, wellness, spa, vacation, leisure, skiing, hiking, horse riding, culinary experiences, adults-only resorts, relaxation, recovery, or similar topics.\nBe inclusive: a message may seem unrelated at first but still be relevant to a hotel stay or vacation.\nReply ONLY with 'YES' if the message could relate to the above topics.\nReply ONLY with 'NO' if the message has no conceivable connection to hotels or vacation.\nNo other text. No explanation. Just: YES or NO.";

    private const string FallbackLanguageDetectPrompt =
        "You are a language detector. Identify the language of the following text.\nReply with ONLY the ISO 639-1 two-letter language code in lowercase (e.g., \"de\", \"en\", \"fr\", \"it\", \"es\").\nNo other text. No punctuation. Just the code.";

    private const string FallbackEthicalCheckPrompt =
        "You are an ethical content validator for a hotel search assistant.\nEvaluate whether the following user message is appropriate to respond to.\nReply ONLY with \"OK\" if the message is polite and appropriate.\nReply ONLY with \"REJECT\" if the message is rude, offensive, discriminatory, hateful, or harassing.\nNo other text. No explanation. Just: OK or REJECT.";

    private const string FallbackTranslateToGermanPrompt =
        "Translate the following text to German.\nIf the text is already in German, output it unchanged.\nOutput ONLY the translated or original text. No greeting, no explanation, no other text.";

    private const string FallbackRelevanceCheckPrompt =
        "You are a relevance validator for a hotel search assistant.\nYou will receive a user query and a context containing hotel database excerpts.\nDetermine whether the context contains factual information that directly answers or addresses the user's query.\nReply ONLY with \"YES\" if the context contains relevant information that answers the user's query.\nReply ONLY with \"NO\" if the context does NOT contain relevant information for the user's query.\nBe strict: if the specific feature, activity, or property the user is asking about is not mentioned in the context, answer NO.\nNo other text. No explanation. Just: YES or NO.";

    /// <summary>
    /// Generiert eine Antwort basierend auf Retrieval-Kontext und User-Query.
    /// </summary>
    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        string context,
        string userQuery,
        List<(string Role, string Content)> conversationHistory,
        string? targetLanguage = null,
        string? userMessageTemplate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt)
            };

            // Konversationshistorie hinzufügen
            foreach (var (role, content) in conversationHistory)
            {
                messages.Add(role switch
                {
                    "user" => new UserChatMessage(content),
                    "assistant" => new AssistantChatMessage(content),
                    _ => new SystemChatMessage(content)
                });
            }

            // Kontext strikt eingerahmt mit dem konfigurierbaren Template
            var template = !string.IsNullOrWhiteSpace(userMessageTemplate)
                ? userMessageTemplate
                : "User Query: {userQuery}\n\nContext:\n{context}";

            var promptWithContext = template
                .Replace("{context}", context)
                .Replace("{userQuery}", userQuery);

            if (!string.IsNullOrEmpty(targetLanguage))
            {
                promptWithContext += $"\nSprache der Antwort: {targetLanguage}";
            }

            messages.Add(new UserChatMessage(promptWithContext));

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,  // 0 = vollständig deterministisch, keine Kreativität, kein freies Wissen
                MaxOutputTokenCount = 1000,
                TopP = 0.1f  // Extremer Fokus auf wahrscheinlichste Tokens - verhindert Abweichungen
            };

            _logger.LogInformation("Sende Chat Completion Request an OpenAI");
            
            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            var responseText = response.Value.Content[0].Text;
            _logger.LogInformation("OpenAI Response erhalten: {Length} Zeichen", responseText.Length);

            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei OpenAI Chat Completion");
            throw;
        }
    }

    /// <summary>
    /// LogicAgent: Klassifiziert semantisch ob eine Anfrage hotel/urlaubs/wellness-relevant ist.
    /// </summary>
    public async Task<bool> IsHotelWellnessQueryAsync(
        string query,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promptText = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : FallbackHotelWellnessPrompt;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(promptText),
                new UserChatMessage($"User message: {query}\n=== ANSWER (YES or NO only) ===")
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,
                MaxOutputTokenCount = 3
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken: cancellationToken);
            var answer = response.Value.Content[0].Text.Trim().ToUpper();

            return answer.Contains("YES") || answer.Contains("JA");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in hotel/wellness intent classification");
            return true; // Im Fehlerfall: Anfrage durchlassen, nicht blockieren
        }
    }

    /// <summary>
    /// LanguageAgent: Erkennt die Sprache der Nutzernachricht (ISO 639-1 Code).
    /// </summary>
    public async Task<string> DetectLanguageAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promptText = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : FallbackLanguageDetectPrompt;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(promptText),
                new UserChatMessage(message)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,
                MaxOutputTokenCount = 5
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            return response.Value.Content[0].Text.Trim().ToLower();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting language");
            return "de"; // Fallback
        }
    }

    /// <summary>
    /// EthicalAgent: Prüft ob die Nachricht ethisch vertretbar ist.
    /// Gibt true zurück wenn OK, false wenn REJECT.
    /// </summary>
    public async Task<bool> IsEthicalAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promptText = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : FallbackEthicalCheckPrompt;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(promptText),
                new UserChatMessage(message)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,
                MaxOutputTokenCount = 5
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var answer = response.Value.Content[0].Text.Trim().ToUpper();
            return !answer.Contains("REJECT");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ethical check");
            return true; // Fail-open: im Fehlerfall nicht blockieren
        }
    }

    /// <summary>
    /// SearchAgent (Schritt 1): Übersetzt die Anfrage ins Deutsche für die Vektordatenbank-Suche.
    /// </summary>
    public async Task<string> TranslateToGermanAsync(
        string message,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promptText = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : FallbackTranslateToGermanPrompt;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(promptText),
                new UserChatMessage(message)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,
                MaxOutputTokenCount = 500
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            return response.Value.Content[0].Text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating to German");
            return message; // Fallback: Original zurückgeben
        }
    }

    /// <summary>
    /// RelevanceAgent: Prüft ob der Vektordatenbank-Kontext die Benutzeranfrage inhaltlich beantwortet.
    /// Gibt true zurück wenn YES (relevant), false wenn NO (nicht relevant).
    /// </summary>
    public async Task<bool> IsContextRelevantAsync(
        string query,
        string context,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var promptText = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : FallbackRelevanceCheckPrompt;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(promptText),
                new UserChatMessage($"User query: {query}\n\n=== CONTEXT ===\n{context}\n\n=== ANSWER (YES or NO only) ===")
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.0f,
                MaxOutputTokenCount = 3
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var answer = response.Value.Content[0].Text.Trim().ToUpper();
            return answer.Contains("YES");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in relevance check");
            return true; // Fail-open: im Fehlerfall Antwort generieren lassen
        }
    }
}
