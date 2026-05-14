using System.Text;
using HotelChatbot.Domain.Interfaces;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Schreibt Protokoll-Einträge in tagesweise rotierende .txt-Dateien
/// unter dem konfigurierten Verzeichnis (standardmäßig 'log/' neben 'wwwroot/').
/// Thread-sicher durch einen SemaphoreSlim.
/// </summary>
public class FileQueryLogger : IQueryLogger
{
    private readonly string _logDirectory;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private const string Separator = "══════════════════════════════════════════════════════════════════════";

    public FileQueryLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    // ─── Tages-Logdatei ─────────────────────────────────────────────────────

    private string TodaysFile =>
        Path.Combine(_logDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.txt");

    // ─── IQueryLogger ────────────────────────────────────────────────────────

    public async Task LogIntentCheckAsync(
        string query,
        string language,
        string promptKey,
        string? promptContent,
        bool isHotelQuery)
    {
        var ts        = Timestamp();
        var category  = isHotelQuery ? "ANFRAGE OK" : "OUT-OF-SCOPE";
        var decision  = isHotelQuery
            ? "JA  →  Anfrage wird weiterverarbeitet"
            : "NEIN  →  Anfrage als OUT-OF-SCOPE eingestuft";

        var sb = new StringBuilder();
        sb.AppendLine(Separator);
        sb.AppendLine($"[{ts}]  {category}");
        sb.AppendLine($"  Query    : {query}");
        sb.AppendLine($"  Language : {language}");
        sb.AppendLine($"  Prompt   : {promptKey}");
        sb.AppendLine();
        AppendPromptBlock(sb, promptContent);
        sb.AppendLine();
        sb.AppendLine($"  Ergebnis : {decision}");
        sb.AppendLine();

        await WriteAsync(sb.ToString());
    }

    public async Task LogNoMatchEvaluationAsync(
        string query,
        string language,
        string evalPromptKey,
        string? evalPromptContent,
        IList<(string HotelName, bool IsMatch, string EvaluationText)> evaluations)
    {
        var ts = Timestamp();

        var sb = new StringBuilder();
        sb.AppendLine(Separator);
        sb.AppendLine($"[{ts}]  KEINE-TREFFER  (alle Hotels NEIN)");
        sb.AppendLine($"  Query    : {query}");
        sb.AppendLine($"  Language : {language}");
        sb.AppendLine($"  Prompt   : {evalPromptKey}");
        sb.AppendLine();
        AppendPromptBlock(sb, evalPromptContent);
        sb.AppendLine();
        sb.AppendLine($"  ── Hotel-Bewertungen ({evaluations.Count} insgesamt) ──────────────────────");

        foreach (var (hotelName, isMatch, evaluationText) in evaluations)
        {
            var verdict = isMatch ? "PASST:JA" : "PASST:NEIN";
            sb.AppendLine($"  Hotel    : {hotelName}  →  {verdict}");
            if (!string.IsNullOrWhiteSpace(evaluationText))
            {
                foreach (var line in evaluationText.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        sb.AppendLine($"           | {trimmed}");
                }
            }
        }

        sb.AppendLine();
        await WriteAsync(sb.ToString());
    }

    // ─── Hilfsmethoden ──────────────────────────────────────────────────────

    private static string Timestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    private static void AppendPromptBlock(StringBuilder sb, string? content)
    {
        sb.AppendLine("  ┌─ Prompt-Inhalt ───────────────────────────────────────────────────");
        foreach (var line in (content ?? "(nicht gesetzt – Fallback aktiv)").Split('\n'))
            sb.AppendLine($"  │ {line.TrimEnd()}");
        sb.AppendLine("  └──────────────────────────────────────────────────────────────────");
    }

    private async Task WriteAsync(string text)
    {
        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(TodaysFile, text, Encoding.UTF8);
        }
        finally
        {
            _lock.Release();
        }
    }
}
