using System.Text.RegularExpressions;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Gemeinsame Hilfsfunktionen für Web- und Playwright-Crawler.
/// </summary>
public static class CrawlerTextUtils
{
    private static readonly string[] ResourceExtensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".tif",
        ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv", ".m4v", ".mpg", ".mpeg",
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac", ".wma",
        ".pdf", ".zip", ".rar", ".7z", ".tar", ".gz", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".css", ".js", ".xml", ".json", ".csv"
    ];

    public static bool IsResourceFile(string url) =>
        ResourceExtensions.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public static string DetectLanguage(Domain.Interfaces.ILanguageDetector detector, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "de";
        var sample = text.Length > 2000 ? text[..2000] : text;
        return detector.Detect(sample, fallback: "de");
    }

    public static string CollapseWhitespace(string text)
    {
        text = Regex.Replace(text, @"[^\S\n]+", " ");
        return text.Trim();
    }
}
