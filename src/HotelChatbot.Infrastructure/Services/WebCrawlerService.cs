using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HotelChatbot.Infrastructure.Services;

/// <summary>
/// Web-Crawler Service für Hotel-Webseiten.
/// Crawlt Webseiten rekursiv und extrahiert relevanten Content.
/// </summary>
public class WebCrawlerService : ICrawlerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebCrawlerService> _logger;
    private readonly HashSet<string> _visitedUrls = new();
    private readonly Queue<(string Url, int Depth)> _urlQueue = new();

    public WebCrawlerService(
        HttpClient httpClient,
        ILogger<WebCrawlerService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // User-Agent setzen
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Crawlt eine Website rekursiv.
    /// </summary>
    public async Task<List<ContentChunk>> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth = 2,
        int maxPages = 50,
        List<string>? allowedDomains = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starte Crawling: URL={Url}, MaxDepth={Depth}, MaxPages={Pages}",
            startUrl, maxDepth, maxPages);

        _visitedUrls.Clear();
        _urlQueue.Clear();

        var chunks = new List<ContentChunk>();
        var baseUri = new Uri(startUrl);
        var allowedDomainsSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };

        _urlQueue.Enqueue((startUrl, 0));

        while (_urlQueue.Count > 0 && chunks.Count < maxPages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var (currentUrl, depth) = _urlQueue.Dequeue();

            if (_visitedUrls.Contains(currentUrl) || depth > maxDepth)
                continue;

            _visitedUrls.Add(currentUrl);

            try
            {
                // HTML einmal laden und für Content + Links verwenden
                var (chunk, htmlDoc) = await CrawlPageWithDocumentAsync(currentUrl, cancellationToken);
                if (chunk != null)
                {
                    chunks.Add(chunk);
                    _logger.LogInformation("Seite gecrawlt: {Url} ({Count}/{Max})", currentUrl, chunks.Count, maxPages);
                }

                // Links extrahieren für weitere Ebenen (aus bereits geladenem HTML)
                if (depth < maxDepth && htmlDoc != null)
                {
                    var links = ExtractLinksFromDocument(htmlDoc, new Uri(currentUrl), allowedDomainsSet);
                    foreach (var link in links)
                    {
                        if (!_visitedUrls.Contains(link))
                        {
                            _urlQueue.Enqueue((link, depth + 1));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fehler beim Crawlen von {Url}", currentUrl);
            }

            // Rate limiting
            await Task.Delay(500, cancellationToken);
        }

        _logger.LogInformation("Crawling abgeschlossen: {Count} Seiten gecrawlt", chunks.Count);
        return chunks;
    }

    /// <summary>
    /// Crawlt eine einzelne Seite.
    /// </summary>
    public async Task<ContentChunk?> CrawlPageAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Überspringe Media- und Resource-Dateien
            if (IsResourceFile(url))
            {
                _logger.LogDebug("Überspringe Resource-Datei: {Url}", url);
                return null;
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP {Status} für {Url}", response.StatusCode, url);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Titel extrahieren
            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim()
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
                ?? "Untitled";

            // Content extrahieren
            var content = ExtractContent(doc);

            if (string.IsNullOrWhiteSpace(content) || content.Length < 50)
            {
                _logger.LogWarning("Zu wenig Content auf {Url} (nur {Length} Zeichen)", url, content?.Length ?? 0);
                return null;
            }

            // Content kürzen auf max 15000 Zeichen (~3750 Tokens, sicherer unter 8192 Token-Limit)
            if (content.Length > 15000)
            {
                _logger.LogWarning("Content zu groß ({Length} Zeichen), wird gekürzt auf 15000", content.Length);
                content = content.Substring(0, 15000);
                // An Satzende kürzen wenn möglich
                var lastPeriod = content.LastIndexOf('.');
                if (lastPeriod > 12000) // Mindestens 80% behalten
                {
                    content = content.Substring(0, lastPeriod + 1);
                }
            }

            // Sprache erkennen (vereinfacht)
            var language = DetectLanguage(content);

            return new ContentChunk
            {
                ChunkId = Guid.NewGuid().ToString(),
                HotelId = string.Empty, // Wird später gesetzt
                SourceUrl = url,
                Content = content,
                Title = CleanText(title),
                Language = language,
                IsActive = true,
                CrawledAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Crawlen von {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Extrahiert relevanten Text-Content aus HTML.
    /// </summary>
    private string ExtractContent(HtmlDocument doc)
    {
        // Unwichtige Elemente entfernen
        var nodesToRemove = doc.DocumentNode.SelectNodes(
            "//script | //style | //nav | //header | //footer | //aside | //form | //iframe");
        
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        // Hauptcontent-Bereiche priorisieren (inkl. TYPO3-spezifische Klassen)
        var contentSelectors = new[]
        {
            "//main",
            "//article",
            "//*[@id='main']",
            "//*[@id='content']",
            "//div[@id='main']",
            "//div[@id='content']",
            "//div[contains(@class, 'dce')]",
            "//div[contains(@class, 'tx-')]",
            "//div[contains(@class, 'content')]",
            "//div[contains(@class, 'main')]",
            "//body"
        };

        HtmlNode? contentNode = null;
        foreach (var selector in contentSelectors)
        {
            contentNode = doc.DocumentNode.SelectSingleNode(selector);
            if (contentNode != null)
                break;
        }

        if (contentNode == null)
            return string.Empty;

        // Text extrahieren
        var textNodes = contentNode.SelectNodes(".//text()[normalize-space()]");
        if (textNodes == null)
            return string.Empty;

        var contentBuilder = new StringBuilder();
        foreach (var textNode in textNodes)
        {
            var text = textNode.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 3)
            {
                contentBuilder.AppendLine(text);
            }
        }

        return CleanText(contentBuilder.ToString());
    }

    /// <summary>
    /// Crawlt eine Seite und gibt sowohl den Content-Chunk als auch das HTML-Document zurück.
    /// Vermeidet doppeltes Laden der Seite für Content- und Link-Extraktion.
    /// </summary>
    private async Task<(ContentChunk? Chunk, HtmlDocument? Document)> CrawlPageWithDocumentAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Überspringe Media- und Resource-Dateien
            if (IsResourceFile(url))
            {
                _logger.LogDebug("Überspringe Resource-Datei: {Url}", url);
                return (null, null);
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP {Status} für {Url}", response.StatusCode, url);
                return (null, null);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Titel extrahieren
            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim()
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
                ?? "Untitled";

            // Content extrahieren
            var content = ExtractContent(doc);

            if (string.IsNullOrWhiteSpace(content) || content.Length < 50)
            {
                _logger.LogWarning("Zu wenig Content auf {Url} (nur {Length} Zeichen)", url, content?.Length ?? 0);
                return (null, doc); // Document zurückgeben für Link-Extraktion
            }

            // Content kürzen auf max 15000 Zeichen
            if (content.Length > 15000)
            {
                _logger.LogWarning("Content zu groß ({Length} Zeichen), wird gekürzt auf 15000", content.Length);
                content = content.Substring(0, 15000);
                var lastPeriod = content.LastIndexOf('.');
                if (lastPeriod > 12000)
                {
                    content = content.Substring(0, lastPeriod + 1);
                }
            }

            var language = DetectLanguage(content);

            var chunk = new ContentChunk
            {
                ChunkId = Guid.NewGuid().ToString(),
                HotelId = string.Empty,
                SourceUrl = url,
                Content = content,
                Title = CleanText(title),
                Language = language,
                IsActive = true,
                CrawledAt = DateTime.UtcNow
            };

            return (chunk, doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Crawlen von {Url}", url);
            return (null, null);
        }
    }

    /// <summary>
    /// Extrahiert Links aus einem bereits geladenen HTML-Document.
    /// </summary>
    private List<string> ExtractLinksFromDocument(
        HtmlDocument doc,
        Uri baseUri,
        HashSet<string> allowedDomains)
    {
        var links = new List<string>();

        try
        {
            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

            if (linkNodes == null)
                return links;

            foreach (var linkNode in linkNodes)
            {
                var href = linkNode.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                // Absolute URL erstellen
                if (Uri.TryCreate(baseUri, href, out var absoluteUri))
                {
                    var absoluteUrl = absoluteUri.ToString();

                    // Nur erlaubte Domains und HTTP(S)
                    if ((absoluteUri.Scheme == "http" || absoluteUri.Scheme == "https") &&
                        allowedDomains.Contains(absoluteUri.Host) &&
                        !absoluteUrl.Contains("#") &&
                        !IsResourceFile(absoluteUrl))
                    {
                        links.Add(absoluteUrl);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Extrahieren von Links von {Url}", baseUri);
        }

        return links.Distinct().ToList();
    }

    /// <summary>
    /// Extrahiert Links von einer Seite (lädt die Seite per HTTP).
    /// DEPRECATED: Verwende stattdessen CrawlPageWithDocumentAsync + ExtractLinksFromDocument.
    /// </summary>
    private async Task<List<string>> ExtractLinksAsync(
        string url,
        HashSet<string> allowedDomains,
        CancellationToken cancellationToken)
    {
        var links = new List<string>();

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return links;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var baseUri = new Uri(url);
            return ExtractLinksFromDocument(doc, baseUri, allowedDomains);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Extrahieren von Links von {Url}", url);
        }

        return links.Distinct().ToList();
    }

    /// <summary>
    /// Bereinigt Text von HTML-Artefakten.
    /// </summary>
    private string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // HTML-Entities dekodieren
        text = HtmlEntity.DeEntitize(text);

        // Multiple Newlines reduzieren (MUSS vor \s+ kommen!)
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Multiple Whitespaces entfernen (aber Newlines erhalten)
        text = Regex.Replace(text, @"[^\S\n]+", " ");

        return text.Trim();
    }

    /// <summary>
    /// Einfache Sprach-Erkennung.
    /// </summary>
    private string DetectLanguage(string text)
    {
        var germanWords = new[] { "der", "die", "das", "und", "ist", "ein", "zu", "in", "für", "von" };
        var englishWords = new[] { "the", "and", "is", "to", "in", "for", "of", "with", "on", "at" };

        var lowerText = text.ToLowerInvariant();
        var germanCount = germanWords.Count(w => lowerText.Contains($" {w} "));
        var englishCount = englishWords.Count(w => lowerText.Contains($" {w} "));

        return germanCount > englishCount ? "de" : "en";
    }

    /// <summary>
    /// Prüft ob URL eine Resource-Datei ist (Bilder, PDFs, etc.)
    /// </summary>
    private bool IsResourceFile(string url)
    {
        var resourceExtensions = new[] { 
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
        return resourceExtensions.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Crawlt eine Website basierend auf ihrer Sitemap.xml.
    /// </summary>
    public async Task<List<ContentChunk>> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starte Sitemap-basiertes Crawling für {BaseUrl}", baseUrl);

        var chunks = new List<ContentChunk>();
        var baseUri = new Uri(baseUrl);
        var allowedDomainsSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };

        try
        {
            // Sitemap-URLs zum Probieren
            var sitemapUrls = new[]
            {
                $"{baseUrl.TrimEnd('/')}/sitemap.xml",
                $"{baseUrl.TrimEnd('/')}/sitemap_index.xml",
                $"https://{baseUri.Host}/sitemap.xml",
                $"https://{baseUri.Host}/sitemap_index.xml"
            };

            var urls = new List<string>();
            foreach (var sitemapUrl in sitemapUrls.Distinct())
            {
                var extractedUrls = await ExtractUrlsFromSitemapAsync(sitemapUrl, allowedDomainsSet, cancellationToken);
                if (extractedUrls.Count > 0)
                {
                    urls.AddRange(extractedUrls);
                    _logger.LogInformation("Sitemap gefunden: {SitemapUrl} mit {Count} URLs", sitemapUrl, extractedUrls.Count);
                    break; // Erste gefundene Sitemap verwenden
                }
            }

            if (urls.Count == 0)
            {
                _logger.LogWarning("Keine Sitemap gefunden für {BaseUrl}. Verwende Fallback.", baseUrl);
                return chunks;
            }

            _logger.LogInformation("Crawle {Count} URLs aus Sitemap", urls.Count);

            // URLs filtern und crawlen
            var uniqueUrls = urls.Distinct()
                .Where(url => !IsResourceFile(url))
                .ToList();

            _logger.LogInformation("Nach Filterung: {Count} zu crawlende URLs", uniqueUrls.Count);

            int crawledCount = 0;
            foreach (var url in uniqueUrls)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var chunk = await CrawlPageAsync(url, cancellationToken);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                        crawledCount++;
                        _logger.LogInformation("Seite gecrawlt: {Url} ({Count}/{Total})", url, crawledCount, uniqueUrls.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fehler beim Crawlen von {Url}", url);
                }

                // Rate limiting
                await Task.Delay(500, cancellationToken);
            }

            _logger.LogInformation("Sitemap-Crawling abgeschlossen: {Count} Seiten gecrawlt von {Total} URLs", chunks.Count, uniqueUrls.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Sitemap-Crawling für {BaseUrl}", baseUrl);
        }

        return chunks;
    }

    /// <summary>
    /// Crawlt eine Website rekursiv mit Callback für sofortige Verarbeitung.
    /// Anstatt alle Chunks zu sammeln, wird für jeden Chunk ein Callback aufgerufen.
    /// </summary>
    public async Task<int> CrawlWebsiteAsync(
        string startUrl,
        int maxDepth,
        int maxPages,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starte Crawling mit Callback: URL={Url}, MaxDepth={Depth}, MaxPages={Pages}",
            startUrl, maxDepth, maxPages);

        _visitedUrls.Clear();
        _urlQueue.Clear();

        int crawledCount = 0;
        var baseUri = new Uri(startUrl);
        var allowedDomainsSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };

        _urlQueue.Enqueue((startUrl, 0));

        while (_urlQueue.Count > 0 && crawledCount < maxPages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var (currentUrl, depth) = _urlQueue.Dequeue();

            if (_visitedUrls.Contains(currentUrl) || depth > maxDepth)
                continue;

            _visitedUrls.Add(currentUrl);

            try
            {
                // HTML einmal laden und für Content + Links verwenden
                var (chunk, htmlDoc) = await CrawlPageWithDocumentAsync(currentUrl, cancellationToken);
                if (chunk != null)
                {
                    crawledCount++;
                    _logger.LogInformation("Seite gecrawlt: {Url} ({Count}/{Max})", currentUrl, crawledCount, maxPages);
                    
                    // Callback sofort aufrufen
                    await onChunkCrawled(chunk);
                }

                // Links extrahieren für weitere Ebenen (aus bereits geladenem HTML)
                if (depth < maxDepth && htmlDoc != null)
                {
                    var links = ExtractLinksFromDocument(htmlDoc, new Uri(currentUrl), allowedDomainsSet);
                    foreach (var link in links)
                    {
                        if (!_visitedUrls.Contains(link))
                        {
                            _urlQueue.Enqueue((link, depth + 1));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fehler beim Crawlen von {Url}", currentUrl);
            }

            // Rate limiting
            await Task.Delay(500, cancellationToken);
        }

        _logger.LogInformation("Crawling mit Callback abgeschlossen: {Count} Seiten gecrawlt", crawledCount);
        return crawledCount;
    }

    /// <summary>
    /// Crawlt eine Website basierend auf ihrer Sitemap.xml mit Callback für sofortige Verarbeitung.
    /// Anstatt alle Chunks zu sammeln, wird für jeden Chunk ein Callback aufgerufen.
    /// </summary>
    public async Task<int> CrawlFromSitemapAsync(
        string baseUrl,
        List<string>? allowedDomains,
        Func<ContentChunk, Task> onChunkCrawled,
        HashSet<string>? excludeUrls = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starte Sitemap-basiertes Crawling mit Callback für {BaseUrl}", baseUrl);

        int crawledCount = 0;
        int skippedCount = 0;
        var baseUri = new Uri(baseUrl);
        var allowedDomainsSet = allowedDomains?.ToHashSet() ?? new HashSet<string> { baseUri.Host };

        try
        {
            // Sitemap-URLs zum Probieren
            var sitemapUrls = new[]
            {
                $"{baseUrl.TrimEnd('/')}/sitemap.xml",
                $"{baseUrl.TrimEnd('/')}/sitemap_index.xml",
                $"https://{baseUri.Host}/sitemap.xml",
                $"https://{baseUri.Host}/sitemap_index.xml"
            };

            var urls = new List<string>();
            foreach (var sitemapUrl in sitemapUrls.Distinct())
            {
                var extractedUrls = await ExtractUrlsFromSitemapAsync(sitemapUrl, allowedDomainsSet, cancellationToken);
                if (extractedUrls.Count > 0)
                {
                    urls.AddRange(extractedUrls);
                    _logger.LogInformation("Sitemap gefunden: {SitemapUrl} mit {Count} URLs", sitemapUrl, extractedUrls.Count);
                    break; // Erste gefundene Sitemap verwenden
                }
            }

            if (urls.Count == 0)
            {
                _logger.LogWarning("Keine Sitemap gefunden für {BaseUrl}. Verwende Fallback.", baseUrl);
                return 0;
            }

            _logger.LogInformation("Crawle {Count} URLs aus Sitemap", urls.Count);

            // URLs filtern und crawlen
            var uniqueUrls = urls.Distinct()
                .Where(url => !IsResourceFile(url))
                .ToList();

            _logger.LogInformation("Nach Filterung: {Count} zu crawlende URLs", uniqueUrls.Count);
            
            if (excludeUrls != null && excludeUrls.Count > 0)
            {
                _logger.LogInformation("📋 {Count} existierende URLs werden übersprungen", excludeUrls.Count);
            }

            foreach (var url in uniqueUrls)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Überspringe bereits indexierte URLs
                if (excludeUrls?.Contains(url) == true)
                {
                    skippedCount++;
                    _logger.LogDebug("⏭️ URL übersprungen (bereits indexiert): {Url}", url);
                    continue;
                }

                try
                {
                    var chunk = await CrawlPageAsync(url, cancellationToken);
                    if (chunk != null)
                    {
                        crawledCount++;
                        _logger.LogInformation("Seite gecrawlt: {Url} ({Count}/{Total})", url, crawledCount, uniqueUrls.Count - skippedCount);
                        
                        // Callback sofort aufrufen
                        await onChunkCrawled(chunk);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fehler beim Crawlen von {Url}", url);
                }

                // Rate limiting
                await Task.Delay(500, cancellationToken);
            }

            _logger.LogInformation("Sitemap-Crawling mit Callback abgeschlossen: {Crawled} Seiten gecrawlt, {Skipped} übersprungen von {Total} URLs", 
                crawledCount, skippedCount, uniqueUrls.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Sitemap-Crawling mit Callback für {BaseUrl}", baseUrl);
        }

        return crawledCount;
    }

    /// <summary>
    /// Extrahiert URLs aus einer Sitemap.xml (inkl. Sitemap-Index).
    /// </summary>
    private async Task<List<string>> ExtractUrlsFromSitemapAsync(
        string sitemapUrl,
        HashSet<string> allowedDomains,
        CancellationToken cancellationToken)
    {
        var urls = new List<string>();

        try
        {
            var response = await _httpClient.GetAsync(sitemapUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return urls;
            }

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xmlContent);

            // Namespace für Sitemap
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            // Prüfe ob es ein Sitemap-Index ist (verweist auf weitere Sitemaps)
            var sitemapElements = doc.Descendants(ns + "sitemap").ToList();
            if (sitemapElements.Any())
            {
                _logger.LogInformation("Sitemap-Index gefunden mit {Count} Sub-Sitemaps", sitemapElements.Count);

                // Rekursiv alle Sub-Sitemaps laden
                foreach (var sitemapElement in sitemapElements)
                {
                    var subSitemapUrl = sitemapElement.Element(ns + "loc")?.Value;
                    if (!string.IsNullOrWhiteSpace(subSitemapUrl))
                    {
                        var subUrls = await ExtractUrlsFromSitemapAsync(subSitemapUrl, allowedDomains, cancellationToken);
                        urls.AddRange(subUrls);
                    }
                }
            }
            else
            {
                // Normale Sitemap - URLs direkt extrahieren
                var urlElements = doc.Descendants(ns + "url").ToList();
                _logger.LogInformation("Sitemap enthält {Count} URLs", urlElements.Count);

                foreach (var urlElement in urlElements)
                {
                    var loc = urlElement.Element(ns + "loc")?.Value;
                    if (!string.IsNullOrWhiteSpace(loc))
                    {
                        // Prüfe ob URL zu erlaubten Domains gehört
                        if (Uri.TryCreate(loc, UriKind.Absolute, out var uri) &&
                            allowedDomains.Contains(uri.Host))
                        {
                            urls.Add(loc);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sitemap nicht gefunden oder Fehler beim Parsen: {Url}", sitemapUrl);
        }

        return urls;
    }
}
