using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Playwright-basiertes Web-Crawling.
/// Rendert Seiten in einem Headless-Browser und scrollt automatisch,
/// um lazy-geladene Inhalte vollständig zu erfassen.
/// </summary>
public interface IPlaywrightCrawlerService
{
    /// <summary>
    /// Crawlt eine einzelne Seite mit Headless-Browser und Auto-Scroll.
    /// </summary>
    Task<ContentChunk?> CrawlPageAsync(
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine Website rekursiv mit Headless-Browser und Callback für sofortige Verarbeitung.
    /// </summary>
    Task<int> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth,
        int maxPages,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine Website basierend auf sitemap.xml mit Headless-Browser und Callback.
    /// </summary>
    Task<int> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        HashSet<string>? excludeUrls = null,
        CancellationToken cancellationToken = default);
}
