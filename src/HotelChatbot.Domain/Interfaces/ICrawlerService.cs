using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Interface für Web-Crawling Service.
/// Crawlt Hotel-Webseiten und extrahiert Content-Chunks.
/// </summary>
public interface ICrawlerService
{
    /// <summary>
    /// Crawlt eine Website und extrahiert Content-Chunks.
    /// </summary>
    /// <param name="startUrl">Start-URL für das Crawling</param>
    /// <param name="maxDepth">Maximale Crawling-Tiefe (Anzahl der Link-Ebenen)</param>
    /// <param name="maxPages">Maximale Anzahl zu crawlender Seiten</param>
    /// <param name="allowedDomains">Liste erlaubter Domains (nur diese werden gecrawlt)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Liste der extrahierten Content-Chunks</returns>
    Task<List<ContentChunk>> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth = 2,
        int maxPages = 50,
        List<string>? allowedDomains = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine einzelne Seite und extrahiert Content.
    /// </summary>
    /// <param name="url">URL der zu crawlenden Seite</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Extrahierter Content-Chunk</returns>
    Task<ContentChunk?> CrawlPageAsync(
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine Website basierend auf ihrer Sitemap.xml.
    /// </summary>
    /// <param name="baseUrl">Basis-URL der Website (z.B. https://www.stock.at/)</param>
    /// <param name="allowedDomains">Liste erlaubter Domains</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Liste der extrahierten Content-Chunks</returns>
    Task<List<ContentChunk>> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine Website rekursiv mit Callback für sofortige Verarbeitung.
    /// </summary>
    /// <param name="startUrl">Start-URL für das Crawling</param>
    /// <param name="maxDepth">Maximale Crawling-Tiefe</param>
    /// <param name="maxPages">Maximale Anzahl zu crawlender Seiten</param>
    /// <param name="allowedDomains">Liste erlaubter Domains</param>
    /// <param name="onChunkCrawled">Callback-Funktion, die für jeden gecrawlten Chunk aufgerufen wird</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Anzahl der gecrawlten Chunks</returns>
    Task<int> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth,
        int maxPages,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crawlt eine Website basierend auf ihrer Sitemap.xml mit Callback für sofortige Verarbeitung.
    /// </summary>
    /// <param name="baseUrl">Basis-URL der Website</param>
    /// <param name="allowedDomains">Liste erlaubter Domains</param>
    /// <param name="onChunkCrawled">Callback-Funktion, die für jeden gecrawlten Chunk aufgerufen wird</param>
    /// <param name="excludeUrls">Optionale Liste von URLs die übersprungen werden sollen</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>Anzahl der gecrawlten Chunks</returns>
    Task<int> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        HashSet<string>? excludeUrls = null,
        CancellationToken cancellationToken = default);
}
