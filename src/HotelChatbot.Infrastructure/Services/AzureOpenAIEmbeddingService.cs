using Azure;
using Azure.AI.OpenAI;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using System.ClientModel;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Azure OpenAI Embedding Service.
/// Generiert Vektor-Embeddings für Text-Content.
/// </summary>
public class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;
    private readonly int _dimensions;

    public AzureOpenAIEmbeddingService(
        IConfiguration configuration,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        _logger = logger;

        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API Key nicht konfiguriert");
        var endpoint = configuration["OpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("OpenAI Endpoint nicht konfiguriert");
        var embeddingDeployment = configuration["OpenAI:EmbeddingDeploymentName"]
            ?? throw new InvalidOperationException("OpenAI EmbeddingDeploymentName nicht konfiguriert");

        _dimensions = int.TryParse(configuration["OpenAI:EmbeddingDimensions"], out var dims) ? dims : 1536;

        var client = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));

        _embeddingClient = client.GetEmbeddingClient(embeddingDeployment);
    }

    /// <summary>
    /// Generiert Embedding für einen einzelnen Text.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Text kürzen falls zu lang (max ~5000 tokens = ~20000 Zeichen)
            var truncatedText = TruncateText(text, 20000);
            
            _logger.LogInformation("Generiere Embedding für Text (Länge: {Length})", truncatedText.Length);

            var response = await _embeddingClient.GenerateEmbeddingAsync(truncatedText, cancellationToken: cancellationToken);
            var embedding = response.Value.ToFloats().ToArray();

            _logger.LogInformation("Embedding generiert: {Dimensions} Dimensionen", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Embedding-Generierung");
            throw;
        }
    }

    /// <summary>
    /// Generiert Embeddings für mehrere Texte (Batch).
    /// </summary>
    public async Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var textList = texts.ToList();
            _logger.LogInformation("Generiere Embeddings für {Count} Texte", textList.Count);

            // Texte kürzen falls zu lang (max ~5000 tokens = ~20000 Zeichen)
            var truncatedTexts = textList.Select(t => TruncateText(t, 20000)).ToList();

            var response = await _embeddingClient.GenerateEmbeddingsAsync(truncatedTexts, cancellationToken: cancellationToken);
            var embeddings = response.Value.Select(e => e.ToFloats().ToArray()).ToList();

            _logger.LogInformation("Embeddings generiert: {Count} Vektoren", embeddings.Count);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Batch-Embedding-Generierung");
            throw;
        }
    }

    /// <summary>
    /// Kürzt Text auf maximale Länge.
    /// </summary>
    private string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        _logger.LogWarning(
            "Text wird gekürzt: {OriginalLength} → {MaxLength} Zeichen",
            text.Length, maxLength);

        // An Satzende kürzen wenn möglich
        var truncated = text.Substring(0, maxLength);
        var lastPeriod = truncated.LastIndexOf('.');
        if (lastPeriod > maxLength * 0.8) // Mindestens 80% des Textes behalten
        {
            return truncated.Substring(0, lastPeriod + 1);
        }

        return truncated + "...";
    }
}
