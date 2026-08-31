namespace HotelChatbot.Application.DTOs;

/// <summary>
/// Ein Schritt der Chat-Pipeline für Live-Debugging (Classifier, LLM, Suche, …).
/// </summary>
public class PipelineTraceStepDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Anzeigename, z. B. Language, Ethical, Intent, Translate, Embedding, VectorSearch, Relevance, Answer.</summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>classifier | embedding | search | llm | db | mcp | other</summary>
    public string Kind { get; set; } = "other";

    /// <summary>start | end</summary>
    public string Phase { get; set; } = "start";

    public string Detail { get; set; } = string.Empty;

    /// <summary>Millisekunden seit Pipeline-Start.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Dauer dieses Schritts (nur bei Phase=end).</summary>
    public long? DurationMs { get; set; }

    /// <summary>running | ok | skip | reject | error</summary>
    public string Status { get; set; } = "running";

    public Dictionary<string, object?>? Meta { get; set; }
}
