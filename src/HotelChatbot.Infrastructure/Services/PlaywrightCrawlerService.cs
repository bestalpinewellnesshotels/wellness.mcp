using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Playwright-basierter Web-Crawler.
/// Rendert Seiten im Headless-Browser (Chromium) und scrollt automatisch,
/// damit lazy-geladene Inhalte vollständig erfasst werden.
/// </summary>
public sealed class PlaywrightCrawlerService : IPlaywrightCrawlerService, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlaywrightCrawlerService> _logger;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    public PlaywrightCrawlerService(IHttpClientFactory httpClientFactory, ILogger<PlaywrightCrawlerService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(PlaywrightCrawlerService));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _logger = logger;
    }

    // ---------------------------------------------------------------------------
    // Browser-Lifecycle
    // ---------------------------------------------------------------------------

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null) return _browser;

        await _browserLock.WaitAsync();
        try
        {
            if (_browser is not null) return _browser;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--disable-dev-shm-usage", "--no-sandbox" }
            });

            _logger.LogInformation("Playwright Chromium gestartet");
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public async Task<ContentChunk?> CrawlPageAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            // Überspringe Media- und Resource-Dateien
            if (IsResourceFile(url))
            {
                _logger.LogDebug("Überspringe Resource-Datei: {Url}", url);
                return null;
            }

            var browser = await GetBrowserAsync();
            var page = await browser.NewPageAsync();
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });

                await AutoScrollAsync(page);
                await WaitForNetworkIdleAsync(page);

                var html = await page.ContentAsync();
                return ExtractChunkFromHtml(html, url);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Playwright-Crawlen von {Url}", url);
            return null;
        }
    }

    public async Task<int> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starte Playwright Sitemap-Crawling für {BaseUrl}", baseUrl);

        var baseUri = new Uri(baseUrl);
        var allowedSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };

        var sitemapCandidates = new[]
        {
            $"{baseUrl.TrimEnd('/')}/sitemap.xml",
            $"{baseUrl.TrimEnd('/')}/sitemap_index.xml",
            $"https://{baseUri.Host}/sitemap.xml",
            $"https://{baseUri.Host}/sitemap_index.xml"
        };

        var urls = new List<string>();
        foreach (var sitemapUrl in sitemapCandidates.Distinct())
        {
            var extracted = await ExtractUrlsFromSitemapAsync(sitemapUrl, allowedSet, cancellationToken);
            if (extracted.Count > 0)
            {
                urls.AddRange(extracted);
                _logger.LogInformation("Sitemap gefunden: {Url} mit {Count} URLs", sitemapUrl, extracted.Count);
                break;
            }
        }

        if (urls.Count == 0)
        {
            _logger.LogWarning("Keine Sitemap gefunden für {BaseUrl}", baseUrl);
            return 0;
        }

        var uniqueUrls = urls.Distinct().Where(u => !IsResourceFile(u)).ToList();
        _logger.LogInformation("Crawle {Count} URLs aus Sitemap mit Playwright", uniqueUrls.Count);

        int crawledCount = 0;
        foreach (var url in uniqueUrls)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var chunk = await CrawlPageAsync(url, cancellationToken);
            if (chunk != null)
            {
                crawledCount++;
                _logger.LogInformation(
                    "Playwright gecrawlt: {Url} ({Count}/{Total})",
                    url, crawledCount, uniqueUrls.Count);

                await onChunkCrawled(chunk);
            }

            await Task.Delay(300, cancellationToken);
        }

        return crawledCount;
    }

    public async Task<int> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth,
        int maxPages,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starte rekursives Playwright-Crawling: URL={Url}, MaxDepth={Depth}, MaxPages={Pages}",
            startUrl, maxDepth, maxPages);

        var baseUri = new Uri(startUrl);
        var allowedSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, int Depth)>();
        queue.Enqueue((startUrl, 0));

        int crawledCount = 0;

        while (queue.Count > 0 && crawledCount < maxPages && !cancellationToken.IsCancellationRequested)
        {
            var (currentUrl, depth) = queue.Dequeue();

            if (!visitedUrls.Add(currentUrl)) continue;

            var browser = await GetBrowserAsync();
            var page = await browser.NewPageAsync();
            string html;

            try
            {
                await page.GotoAsync(currentUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });

                await AutoScrollAsync(page);
                await WaitForNetworkIdleAsync(page);

                html = await page.ContentAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playwright-Fehler bei {Url}", currentUrl);
                await page.CloseAsync();
                continue;
            }

            await page.CloseAsync();

            var chunk = ExtractChunkFromHtml(html, currentUrl);
            if (chunk != null)
            {
                crawledCount++;
                _logger.LogInformation("Playwright gecrawlt: {Url} ({Count})", currentUrl, crawledCount);
                await onChunkCrawled(chunk);
            }

            if (depth < maxDepth)
            {
                foreach (var link in ExtractLinksFromHtml(html, currentUrl, allowedSet))
                {
                    if (!visitedUrls.Contains(link))
                        queue.Enqueue((link, depth + 1));
                }
            }

            await Task.Delay(300, cancellationToken);
        }

        return crawledCount;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static async Task AutoScrollAsync(IPage page)
    {
        try
        {
            await page.EvaluateAsync(@"async () => {
                await new Promise(resolve => {
                    let totalHeight = 0;
                    const distance = 300;
                    const timer = setInterval(() => {
                        window.scrollBy(0, distance);
                        totalHeight += distance;
                        if (totalHeight >= document.body.scrollHeight) {
                            clearInterval(timer);
                            resolve();
                        }
                    }, 100);
                });
            }");
        }
        catch
        {
            // Ignore – some pages block JS evaluation
        }
    }

    private static async Task WaitForNetworkIdleAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // Content is already available; ignore the timeout
        }
    }

    private ContentChunk? ExtractChunkFromHtml(string html, string url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
            ?? "Untitled";

        var content = ExtractContent(doc);

        if (string.IsNullOrWhiteSpace(content) || content.Length < 50)
        {
            _logger.LogWarning("Zu wenig Content auf {Url} (nur {Length} Zeichen)", url, content?.Length ?? 0);
            return null;
        }

        if (content.Length > 15_000)
        {
            content = content[..15_000];
            var lastPeriod = content.LastIndexOf('.');
            if (lastPeriod > 12_000)
                content = content[..(lastPeriod + 1)];
        }

        return new ContentChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            HotelId = string.Empty,
            SourceUrl = url,
            Content = content,
            Title = CleanText(title),
            Language = DetectLanguage(content),
            IsActive = true,
            CrawledAt = DateTime.UtcNow
        };
    }

    private static string ExtractContent(HtmlDocument doc)
    {
        var nodesToRemove = doc.DocumentNode.SelectNodes(
            "//script | //style | //nav | //header | //footer | //aside | //form | //iframe");

        if (nodesToRemove != null)
            foreach (var node in nodesToRemove) node.Remove();

        // Hauptcontent-Bereiche priorisieren (inkl. TYPO3-spezifische Klassen)
        var selectors = new[]
        {
            "//main",
            "//article",            "//*[@id='main']",
            "//*[@id='content']",
            "//div[@id='main']",
            "//div[@id='content']",            "//div[contains(@class,'dce')]",
            "//div[contains(@class,'tx-')]",
            "//div[contains(@class,'content')]",
            "//div[contains(@class,'main')]",
            "//body"
        };

        HtmlNode? contentNode = null;
        foreach (var sel in selectors)
        {
            contentNode = doc.DocumentNode.SelectSingleNode(sel);
            if (contentNode != null) break;
        }

        if (contentNode == null) return string.Empty;

        var textNodes = contentNode.SelectNodes(".//text()[normalize-space()]");
        if (textNodes == null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in textNodes)
        {
            var text = node.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 3)
                sb.AppendLine(text);
        }

        return CleanText(sb.ToString());
    }

    private static List<string> ExtractLinksFromHtml(string html, string baseUrlStr, HashSet<string> allowedDomains)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var baseUri = new Uri(baseUrlStr);
        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (linkNodes == null) return [];

        return linkNodes
            .Select(n => n.GetAttributeValue("href", string.Empty))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => Uri.TryCreate(baseUri, h, out var abs) ? abs : null)
            .Where(abs =>
                abs != null &&
                (abs.Scheme == "http" || abs.Scheme == "https") &&
                allowedDomains.Contains(abs.Host) &&
                !abs.ToString().Contains('#') &&
                !IsResourceFile(abs.ToString()))
            .Select(abs => abs!.ToString())
            .Distinct()
            .ToList();
    }

    private async Task<List<string>> ExtractUrlsFromSitemapAsync(
        string sitemapUrl,
        HashSet<string> allowedDomains,
        CancellationToken ct)
    {
        var urls = new List<string>();
        try
        {
            var response = await _httpClient.GetAsync(sitemapUrl, ct);
            if (!response.IsSuccessStatusCode) return urls;

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Sitemap-Index: enthält verschachtelte <sitemap><loc>…</loc></sitemap>
            var sitemapLocs = doc.Descendants(ns + "sitemap")
                .Select(e => e.Element(ns + "loc")?.Value)
                .Where(v => v is not null)
                .ToList();

            if (sitemapLocs.Count > 0)
            {
                foreach (var loc in sitemapLocs)
                {
                    var sub = await ExtractUrlsFromSitemapAsync(loc!, allowedDomains, ct);
                    urls.AddRange(sub);
                }
            }
            else
            {
                // Reguläre Sitemap: <url><loc>…</loc></url>
                foreach (var loc in doc.Descendants(ns + "url")
                    .Select(e => e.Element(ns + "loc")?.Value)
                    .Where(v => v is not null))
                {
                    if (Uri.TryCreate(loc, UriKind.Absolute, out var uri) &&
                        allowedDomains.Contains(uri.Host))
                    {
                        urls.Add(loc!);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Lesen der Sitemap {Url}", sitemapUrl);
        }

        return urls;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = HtmlEntity.DeEntitize(text);
        // Multiple Newlines reduzieren (MUSS vor \s+ kommen!)
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        // Multiple Whitespaces entfernen (aber Newlines erhalten)
        text = Regex.Replace(text, @"[^\S\n]+", " ");
        return text.Trim();
    }

    private static string DetectLanguage(string text)
    {
        var lower = text.ToLowerInvariant();
        var de = new[] { "der", "die", "das", "und", "ist", "ein", "zu", "in", "für", "von" }
            .Count(w => lower.Contains($" {w} "));
        var en = new[] { "the", "and", "is", "to", "in", "for", "of", "with", "on", "at" }
            .Count(w => lower.Contains($" {w} "));
        return de > en ? "de" : "en";
    }

    private static bool IsResourceFile(string url)
    {
        var exts = new[] { 
            // Images
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".tif",
            // Videos
            ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv", ".m4v", ".mpg", ".mpeg",
            // Audio
            ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac", ".wma",
            // Documents & Archives
            ".pdf", ".zip", ".rar", ".7z", ".tar", ".gz", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            // Code & Data
            ".css", ".js", ".xml", ".json", ".csv"
        };
        return exts.Any(e => url.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _browserLock.Dispose();
    }
}
