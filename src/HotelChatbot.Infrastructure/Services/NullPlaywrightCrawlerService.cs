using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// No-Op Playwright-Crawler für die schlanke API-Instanz (ohne Chromium).
/// Wirft bei Crawl-Aufrufen, damit Admin-Fallback nicht still „erfolgreich“ leert.
/// </summary>
public sealed class NullPlaywrightCrawlerService : IPlaywrightCrawlerService
{
    public const string DisabledMessage =
        "Playwright ist in dieser Instanz deaktiviert. Bitte den Crawl-Worker nutzen (docker compose --profile crawl up crawl-worker, Port 5002).";

    private readonly ILogger<NullPlaywrightCrawlerService> _logger;

    public NullPlaywrightCrawlerService(ILogger<NullPlaywrightCrawlerService> logger)
    {
        _logger = logger;
    }

    public Task<ContentChunk?> CrawlPageAsync(string url, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(DisabledMessage);
        throw new InvalidOperationException(DisabledMessage);
    }

    public Task<int> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth,
        int maxPages,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(DisabledMessage);
        throw new InvalidOperationException(DisabledMessage);
    }

    public Task<int> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        HashSet<string>? excludeUrls = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(DisabledMessage);
        throw new InvalidOperationException(DisabledMessage);
    }
}
