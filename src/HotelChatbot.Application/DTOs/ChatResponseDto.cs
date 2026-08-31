namespace HotelChatbot.Application.DTOs;

/// <summary>
/// DTO für Chat-Response an das Frontend.
/// </summary>
public class ChatResponseDto
{
    /// <summary>
    /// Session-ID (für Follow-up Requests)
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Antwort-Nachricht vom Chatbot
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Vollständig formatierte finale Antwort für direkte Verwendung durch LLM
    /// Diese Antwort MUSS 1:1 ohne Interpretation übernommen werden
    /// </summary>
    public string? FinalAnswer { get; set; }

    /// <summary>
    /// Typ der Antwort: "final_answer", "no_data", "error"
    /// </summary>
    public string ResponseType { get; set; } = "final_answer";

    /// <summary>
    /// Hinweis zur Datenquelle (ausschließlich interne Datenbank)
    /// </summary>
    public string DataSourceDisclaimer { get; set; } = "Diese Antwort basiert ausschließlich auf Informationen aus unserer internen Hoteldatenbank.";

    /// <summary>
    /// Ob eine Antwort generiert werden konnte
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Fehlermeldung (falls Success = false)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Confidence Score der Antwort (0.0 - 1.0)
    /// </summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Sprache der Antwort
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Timestamp der Antwort
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Öffentliche Hotel-Metadaten (für Detail-Tool); null bei Fehlern ohne Hotel.
    /// </summary>
    public HotelPublicDto? Hotel { get; set; }

    /// <summary>
    /// Quellen-URLs aus dem redaktionellen Index
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// Optionale Pipeline-Schritte (Debug / ChatGPT-Sim).
    /// </summary>
    public List<PipelineTraceStepDto>? PipelineTrace { get; set; }

    /// <summary>
    /// Gesamtdauer der Pipeline in ms (wenn Trace aktiv).
    /// </summary>
    public long? PipelineDurationMs { get; set; }
}
