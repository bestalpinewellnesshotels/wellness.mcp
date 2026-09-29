using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using HotelChatbot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Concurrent;

namespace HotelChatbot.Api.Controllers;

/// <summary>
/// Admin-Controller für Content-Management und Indexierung.
/// </summary>
[ApiController]
[Route("api/admin")]
[SwaggerTag("Admin-Funktionen für Content-Crawling und Indexierung")]
public class AdminController : ControllerBase
{
    private readonly IVectorStore _vectorStore;
    private readonly IHotelRepository _hotelRepository;
    private readonly ICrawlerService _crawlerService;
    private readonly IPlaywrightCrawlerService _playwrightCrawlerService;
    private readonly ILogger<AdminController> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IMemoryCache _memoryCache;

    // In-Memory Status-Store für Crawl-Jobs
    private static readonly ConcurrentDictionary<string, CrawlJobStatus> _crawlJobs = new();

    public AdminController(
        IVectorStore vectorStore,
        IHotelRepository hotelRepository,
        ICrawlerService crawlerService,
        IPlaywrightCrawlerService playwrightCrawlerService,
        ILogger<AdminController> logger,
        IServiceScopeFactory serviceScopeFactory,
        IMemoryCache memoryCache)
    {
        _vectorStore = vectorStore;
        _hotelRepository = hotelRepository;
        _crawlerService = crawlerService;
        _playwrightCrawlerService = playwrightCrawlerService;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// Ruft alle Hotels ab.
    /// </summary>
    [HttpGet("hotels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllHotels(CancellationToken cancellationToken)
    {
        try
        {
            var hotels = await _hotelRepository.GetAllAsync(cancellationToken);
            
            return Ok(new
            {
                count = hotels.Count,
                hotels = hotels.Select(h => new
                {
                    hotelId = h.HotelId,
                    name = h.Name,
                    domain = h.Domain,
                    allowedDomains = h.AllowedDomains,
                    isActive = h.IsActive
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Hotels");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Ruft ein Hotel anhand der ID ab.
    /// </summary>
    [HttpGet("hotels/{hotelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHotelById(string hotelId, CancellationToken cancellationToken)
    {
        try
        {
            var hotel = await _hotelRepository.GetByIdAsync(hotelId, cancellationToken);
            
            if (hotel == null)
            {
                return NotFound(new { error = $"Hotel {hotelId} nicht gefunden" });
            }
            
            return Ok(new
            {
                hotelId = hotel.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                allowedDomains = hotel.AllowedDomains,
                apiKey = hotel.ApiKey,
                isActive = hotel.IsActive,
                location = hotel.Location,
                region = hotel.Region,
                country = hotel.Country,
                latitude = hotel.Latitude,
                longitude = hotel.Longitude,
                officialUrl = hotel.OfficialUrl,
                sourceUrl = hotel.SourceUrl,
                editorialReviewStatus = hotel.EditorialReviewStatus,
                editorialReviewedAt = hotel.EditorialReviewedAt,
                categories = hotel.Categories
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen des Hotels {HotelId}", hotelId);
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Ruft Content-Statistiken für alle Hotels ab.
    /// </summary>
    [HttpGet("hotels/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHotelContentStats(CancellationToken cancellationToken)
    {
        try
        {
            var hotels = await _hotelRepository.GetAllAsync(cancellationToken);
            var stats = new List<object>();

            foreach (var hotel in hotels)
            {
                var chunkCount = await _vectorStore.GetChunkCountAsync(hotel.HotelId, cancellationToken);
                stats.Add(new
                {
                    hotelId = hotel.HotelId,
                    name = hotel.Name,
                    domain = hotel.Domain,
                    isActive = hotel.IsActive,
                    chunkCount = chunkCount,
                    hasContent = chunkCount > 0
                });
            }

            return Ok(new
            {
                totalHotels = hotels.Count,
                hotelsWithContent = stats.Count(s => ((dynamic)s).chunkCount > 0),
                hotelsWithoutContent = stats.Count(s => ((dynamic)s).chunkCount == 0),
                hotels = stats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Hotel-Statistiken");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Indexiert Content-Chunks für ein Hotel.
    /// </summary>
    [HttpPost("index-content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IndexContent(
        [FromBody] IndexContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.HotelId))
            {
                return BadRequest(new { error = "HotelId ist erforderlich" });
            }

            if (request.Chunks == null || !request.Chunks.Any())
            {
                return BadRequest(new { error = "Mindestens ein Content-Chunk erforderlich" });
            }

            _logger.LogInformation(
                "Indexiere {Count} Chunks für Hotel {HotelId}",
                request.Chunks.Count, request.HotelId);

            // Hotel prüfen
            var hotel = await _hotelRepository.GetByIdAsync(request.HotelId, cancellationToken);
            if (hotel == null)
            {
                return BadRequest(new { error = $"Hotel {request.HotelId} nicht gefunden" });
            }

            // Content-Chunks erstellen
            var chunks = request.Chunks.Select(c => new ContentChunk
            {
                ChunkId = Guid.NewGuid().ToString(),
                HotelId = request.HotelId,
                SourceUrl = c.SourceUrl,
                Content = c.Content,
                Title = c.Title,
                Language = c.Language ?? "de",
                IsActive = true,
                CrawledAt = DateTime.UtcNow
            }).ToList();

            // In Vector Store indexieren
            await _vectorStore.AddChunksAsync(chunks, cancellationToken);

            return Ok(new
            {
                message = "Content erfolgreich indexiert",
                hotelId = request.HotelId,
                chunksIndexed = chunks.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Indexieren von Content");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Löscht alle indexierten Chunks eines Hotels.
    /// </summary>
    [HttpDelete("delete-content/{hotelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteContent(
        string hotelId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Lösche Content für Hotel {HotelId}", hotelId);

            var hotel = await _hotelRepository.GetByIdAsync(hotelId, cancellationToken);
            if (hotel == null)
            {
                return NotFound(new { error = $"Hotel {hotelId} nicht gefunden" });
            }

            await _vectorStore.DeleteHotelChunksAsync(hotelId, cancellationToken);

            return Ok(new
            {
                message = "Content erfolgreich gelöscht",
                hotelId = hotelId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Löschen von Content");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Testet die Vektor-Suche.
    /// </summary>
    [HttpPost("test-search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSearch(
        [FromBody] TestSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await _vectorStore.SearchAsync(
                request.HotelId,
                request.Query,
                request.TopK ?? 5,
                request.MinScore ?? 0.7,
                cancellationToken);

            return Ok(new
            {
                query = request.Query,
                results = results.Select(r => new
                {
                    chunkId = r.Chunk.ChunkId,
                    title = r.Chunk.Title,
                    content = r.Chunk.Content,
                    sourceUrl = r.Chunk.SourceUrl,
                    score = r.Score
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Test-Suche");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Crawlt eine Website und indexiert den Content automatisch.
    /// </summary>
    [HttpPost("crawl-and-index")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CrawlAndIndex(
        [FromBody] CrawlRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.HotelId))
            {
                return BadRequest(new { error = "HotelId ist erforderlich" });
            }

            if (string.IsNullOrEmpty(request.StartUrl))
            {
                return BadRequest(new { error = "StartUrl ist erforderlich" });
            }

            _logger.LogInformation(
                "Starte Crawling und Indexierung: Hotel={HotelId}, URL={Url}",
                request.HotelId, request.StartUrl);

            // Hotel prüfen
            var hotel = await _hotelRepository.GetByIdAsync(request.HotelId, cancellationToken);
            if (hotel == null)
            {
                return BadRequest(new { error = $"Hotel {request.HotelId} nicht gefunden" });
            }

            // Alte Chunks löschen vor Re-Crawling
            _logger.LogInformation("Lösche alte Chunks für {HotelId} vor Re-Crawling", request.HotelId);
            await _vectorStore.DeleteHotelChunksAsync(request.HotelId, cancellationToken);

            // Batch-Sammlung für effiziente DB-Operationen
            var chunkBatch = new List<ContentChunk>();
            const int batchSize = 50;
            int totalCrawled = 0;
            var crawledUrls = new List<string>();

            // Callback für sofortige Verarbeitung
            async Task ProcessChunk(ContentChunk chunk)
            {
                chunk.HotelId = request.HotelId;
                chunkBatch.Add(chunk);
                crawledUrls.Add(chunk.SourceUrl);
                totalCrawled++;

                // Batch speichern wenn voll
                if (chunkBatch.Count >= batchSize)
                {
                    _logger.LogInformation("Speichere Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, request.HotelId);
                    await _vectorStore.AddChunksAsync(chunkBatch, cancellationToken);
                    chunkBatch.Clear();
                }
            }

            // Website crawlen mit Callback
            var crawledCount = await _crawlerService.CrawlWebsiteAsync(
                request.StartUrl,
                request.MaxDepth ?? 2,
                request.MaxPages ?? 50,
                request.AllowedDomains,
                ProcessChunk,
                cancellationToken);

            // Restliche Chunks im Batch speichern
            if (chunkBatch.Count > 0)
            {
                _logger.LogInformation("Speichere finalen Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, request.HotelId);
                await _vectorStore.AddChunksAsync(chunkBatch, cancellationToken);
            }

            if (totalCrawled == 0)
            {
                return Ok(new
                {
                    message = "Keine Inhalte gefunden",
                    hotelId = request.HotelId,
                    pagesProcessed = 0
                });
            }

            _logger.LogInformation(
                "Crawling und Indexierung abgeschlossen: {Count} Seiten",
                totalCrawled);

            return Ok(new
            {
                message = "Website erfolgreich gecrawlt und indexiert",
                hotelId = request.HotelId,
                pagesProcessed = totalCrawled,
                urls = crawledUrls
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Crawling und Indexieren");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Crawlt eine einzelne Seite und gibt den Content zurück (ohne zu indexieren).
    /// </summary>
    [HttpPost("crawl-preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CrawlPreview(
        [FromBody] CrawlPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var chunk = await _crawlerService.CrawlPageAsync(request.Url, cancellationToken);

            if (chunk == null)
            {
                return Ok(new { message = "Keine Inhalte gefunden" });
            }

            return Ok(new
            {
                url = chunk.SourceUrl,
                title = chunk.Title,
                content = chunk.Content,
                language = chunk.Language,
                contentLength = chunk.Content.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Preview-Crawling");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Crawlt mehrere Websites und erstellt für jede ein eigenes Hotel.
    /// </summary>
    [HttpPost("crawl-multiple-hotels")]
    [SwaggerOperation(
        Summary = "🚀 Crawl Multiple Hotels",
        Description = @"Crawlt mehrere Hotel-Websites automatisch und indexiert den Content in der Vektordatenbank. 
Erstellt für jede URL automatisch ein Hotel mit generierter ID (z.B. hotel_stock_at).

- Lädt sitemap.xml und crawlt alle URLs daraus
- Gibt sofort eine Job-ID zurück und crawlt im Hintergrund
- Verwende GET /api/admin/crawl/status/{jobId} um den Fortschritt abzufragen

**Empfohlener Request (nur URLs + useSitemap nötig):**
```json
{
  ""urls"": [
    ""https://www.stock.at/"",
    ""https://www.post-lermoos.at/"",
    ""https://www.nesslerhof.at/"",
    ""https://www.alpbacherhof.at/"",
    ""https://www.waldklause.at/"",
    ""https://www.engel-tirol.com/"",
    ""https://www.hochschober.com/"",
    ""https://www.uebergossenealm.at/"",
    ""https://www.alpenrose.at/"",
    ""https://www.wartherhof.at/"",
    ""https://www.krallerhof.at/"",
    ""https://www.theresa.at/"",
    ""https://www.gmachl.at/"",
    ""https://www.alpenresort-schwarz.at/"",
    ""https://seefeld.sacher.com/""
  ],
  ""useSitemap"": true
}
```

**Hinweis:** maxDepth und maxPages werden nur verwendet, wenn keine Sitemap gefunden wird (Fallback).",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [SwaggerResponse(200, "Crawl-Job gestartet", typeof(object))]
    [SwaggerResponse(400, "Ungültige Anfrage")]
    [SwaggerResponse(500, "Interner Serverfehler")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CrawlMultipleHotels(
        [FromBody] CrawlMultipleRequest request)
    {
        try
        {
            // Job-ID erstellen
            var jobId = Guid.NewGuid().ToString("N");
            
            // Job-Status initialisieren
            var jobStatus = new CrawlJobStatus
            {
                JobId = jobId,
                Status = "running",
                TotalHotels = request.Urls.Count,
                ProcessedHotels = 0,
                StartedAt = DateTime.UtcNow,
                Results = new List<CrawlResult>()
            };
            
            _crawlJobs[jobId] = jobStatus;
            
            // Crawling im Hintergrund starten
            _ = Task.Run(async () =>
            {
                // Eigener Service-Scope für Background-Task
                using var scope = _serviceScopeFactory.CreateScope();
                var scopedVectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
                var scopedHotelRepository = scope.ServiceProvider.GetRequiredService<IHotelRepository>();
                var scopedCrawlerService = scope.ServiceProvider.GetRequiredService<ICrawlerService>();
                var scopedPlaywrightCrawlerService = scope.ServiceProvider.GetRequiredService<IPlaywrightCrawlerService>();
                var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<AdminController>>();
                
                try
                {
                    for (int i = 0; i < request.Urls.Count; i++)
                    {
                        var url = request.Urls[i];
                        
                        try
                        {
                            scopedLogger.LogWarning("═══════════════════════════════════════════════════");
                            scopedLogger.LogWarning("🚀 STARTE CRAWLING FÜR {Url} ({Index}/{Total})", url, i + 1, request.Urls.Count);
                            scopedLogger.LogWarning("═══════════════════════════════════════════════════");

                            // Hotel-ID aus Domain generieren
                            var uri = new Uri(url);
                            var baseHost = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
                            var hotelId = $"hotel_{baseHost.Replace(".", "_")}";

                            // Status aktualisieren
                            jobStatus.CurrentHotel = baseHost;
                            jobStatus.CurrentHotelPages = 0;
                            jobStatus.CurrentHotelTotalPages = 0;

                            // Prüfe ob Hotel existiert, sonst erstelle es
                            var hotel = await scopedHotelRepository.GetByIdAsync(hotelId, CancellationToken.None);
                            if (hotel == null)
                            {
                                var rawName = baseHost.Replace(".at", "").Replace(".com", "").Replace(".de", "");
                                hotel = new Domain.Entities.Hotel
                                {
                                    HotelId = hotelId,
                                    Name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rawName),
                                    Domain = baseHost,
                                    AllowedDomains = new List<string> { baseHost, $"www.{baseHost}" },
                                    ApiKey = Guid.NewGuid().ToString(),
                                    IsActive = true
                                };
                                await scopedHotelRepository.AddAsync(hotel);
                                scopedLogger.LogInformation("Hotel erstellt: {HotelId}", hotelId);
                            }

                            // Inkrementelles Crawling: Existierende URLs abrufen ODER alte Chunks löschen
                            HashSet<string>? excludeUrls = null;
                            if (request.SkipExistingUrls == true)
                            {
                                scopedLogger.LogInformation("🔄 Inkrementelles Crawling aktiviert - lade existierende URLs für {HotelId}", hotelId);
                                excludeUrls = await scopedVectorStore.GetExistingUrlsAsync(hotelId, CancellationToken.None);
                                scopedLogger.LogInformation("📋 {Count} URLs bereits indexiert, werden übersprungen", excludeUrls.Count);
                            }
                            else
                            {
                                scopedLogger.LogInformation("🗑️ Lösche alte Chunks für {HotelId} vor Re-Crawling", hotelId);
                                await scopedVectorStore.DeleteHotelChunksAsync(hotelId, CancellationToken.None);
                            }

                            // Sitemap-URL-Count ermitteln für TotalPages
                            if (request.UseSitemap ?? true)
                            {
                                try
                                {
                                    var sitemapUrl = url.TrimEnd('/') + "/sitemap.xml";
                                    using var httpClient = new HttpClient();
                                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; HotelChatbot/1.0)");
                                    
                                    var sitemapXml = await httpClient.GetStringAsync(sitemapUrl);
                                    var sitemapDoc = System.Xml.Linq.XDocument.Parse(sitemapXml);
                                    var ns = sitemapDoc.Root?.GetDefaultNamespace();
                                    
                                    // Count URLs in sitemap
                                    if (ns != null && sitemapDoc.Root?.Name.LocalName == "sitemapindex")
                                    {
                                        // Sitemap-Index: zähle URLs in allen Sub-Sitemaps
                                        var sitemapUrls = sitemapDoc.Root.Elements(ns + "sitemap")
                                            .Select(s => s.Element(ns + "loc")?.Value)
                                            .Where(loc => !string.IsNullOrEmpty(loc))
                                            .ToList();
                                        
                                        int totalUrls = 0;
                                        foreach (var subSitemapUrl in sitemapUrls)
                                        {
                                            try
                                            {
                                                var subSitemapXml = await httpClient.GetStringAsync(subSitemapUrl);
                                                var subSitemapDoc = System.Xml.Linq.XDocument.Parse(subSitemapXml);
                                                var subNs = subSitemapDoc.Root?.GetDefaultNamespace();
                                                if (subNs != null)
                                                {
                                                    totalUrls += subSitemapDoc.Root?.Elements(subNs + "url").Count() ?? 0;
                                                }
                                            }
                                            catch { /* Ignoriere Fehler bei Sub-Sitemaps */ }
                                        }
                                        jobStatus.CurrentHotelTotalPages = totalUrls;
                                    }
                                    else if (ns != null)
                                    {
                                        // Reguläre Sitemap
                                        jobStatus.CurrentHotelTotalPages = sitemapDoc.Root?.Elements(ns + "url").Count() ?? 0;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    scopedLogger.LogWarning(ex, "Konnte Sitemap-Größe nicht ermitteln für {Url}", url);
                                }
                            }

                            // Batch-Sammlung für effiziente DB-Operationen
                            var chunkBatch = new List<ContentChunk>();
                            var allChunksForQualityCheck = new List<ContentChunk>(); // Für Fallback-Prüfung
                            const int batchSize = 50;
                            int totalCrawled = 0;

                            // Callback für sofortige Verarbeitung + Status-Update
                            async Task ProcessChunk(ContentChunk chunk)
                            {
                                chunk.HotelId = hotelId;
                                chunkBatch.Add(chunk);
                                allChunksForQualityCheck.Add(chunk); // Für spätere Qualitätsprüfung
                                totalCrawled++;
                                
                                // Status aktualisieren
                                jobStatus.CurrentHotelPages = totalCrawled;

                                // Batch speichern wenn voll
                                if (chunkBatch.Count >= batchSize)
                                {
                                    scopedLogger.LogInformation("Speichere Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                                    await scopedVectorStore.AddChunksAsync(chunkBatch, CancellationToken.None);
                                    chunkBatch.Clear();
                                }
                            }

                            int crawledCount;

                            // Entscheide zwischen Sitemap und rekursivem Crawling
                            if (request.UseSitemap ?? true)
                            {
                                scopedLogger.LogWarning("🔍 Versuche Sitemap-basiertes Crawling für {HotelId}", hotelId);
                                crawledCount = await scopedCrawlerService.CrawlFromSitemapAsync(
                                    url,
                                    new List<string> { baseHost, $"www.{baseHost}" },
                                    ProcessChunk,
                                    excludeUrls,
                                    CancellationToken.None);

                                scopedLogger.LogWarning("🔍 CrawlFromSitemapAsync zurückgekehrt: crawledCount={Count}, totalCrawled={Total}", crawledCount, totalCrawled);

                                // Fallback zu rekursivem Crawling, wenn Sitemap leer ist
                                if (crawledCount == 0)
                                {
                                    // ⚠️ Bei inkrementellem Crawling keinen rekursiven Fallback
                                    if (request.SkipExistingUrls == true)
                                    {
                                        scopedLogger.LogWarning("⚠️ Sitemap leer und inkrementelles Crawling aktiv");
                                        scopedLogger.LogWarning("⏭️ Rekursives Crawling wird übersprungen (würde existierende URLs neu crawlen)");
                                        scopedLogger.LogWarning("💡 Tipp: Für vollständiges Crawling 'Nur neue URLs' deaktivieren");
                                    }
                                    else
                                    {
                                        scopedLogger.LogWarning("Sitemap lieferte keine URLs. Fallback zu rekursivem Crawling für {HotelId}", hotelId);
                                        crawledCount = await scopedCrawlerService.CrawlWebsiteAsync(
                                            url,
                                            request.MaxDepth ?? 2,
                                            request.MaxPages ?? 200,
                                            new List<string> { baseHost, $"www.{baseHost}" },
                                            ProcessChunk,
                                            CancellationToken.None);
                                        
                                        scopedLogger.LogWarning("🔍 CrawlWebsiteAsync zurückgekehrt: crawledCount={Count}, totalCrawled={Total}", crawledCount, totalCrawled);
                                    }
                                }
                            }
                            else
                            {
                                // ⚠️ Bei inkrementellem Crawling nur Sitemap-basiert unterstützt
                                if (request.SkipExistingUrls == true)
                                {
                                    scopedLogger.LogWarning("⚠️ Rekursives Crawling mit inkrementeller Option nicht unterstützt");
                                    scopedLogger.LogWarning("💡 Aktiviere 'Sitemap verwenden' oder deaktiviere 'Nur neue URLs'");
                                }
                                else
                                {
                                    scopedLogger.LogWarning("Verwende rekursives Crawling für {HotelId}", hotelId);
                                    crawledCount = await scopedCrawlerService.CrawlWebsiteAsync(
                                        url,
                                        request.MaxDepth ?? 2,
                                        request.MaxPages ?? 200,
                                        new List<string> { baseHost, $"www.{baseHost}" },
                                        ProcessChunk,
                                        CancellationToken.None);
                                    
                                    scopedLogger.LogWarning("🔍 CrawlWebsiteAsync zurückgekehrt: crawledCount={Count}, totalCrawled={Total}", crawledCount, totalCrawled);
                                }
                            }

                            scopedLogger.LogWarning("🔍 VOR Batch-Speicherung: chunkBatch.Count={Count}", chunkBatch.Count);

                            // Restliche Chunks im Batch speichern
                            if (chunkBatch.Count > 0)
                            {
                                scopedLogger.LogWarning("Speichere finalen Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                                await scopedVectorStore.AddChunksAsync(chunkBatch, CancellationToken.None);
                            }

                            scopedLogger.LogWarning("🔍 NACH Batch-Speicherung - gleich kommt Fallback-Prüfung...");
                            scopedLogger.LogWarning("───────────────────────────────────────────────────");
                            scopedLogger.LogWarning("📊 WEBCRAWLER ABGESCHLOSSEN für {HotelId}: {Count} Seiten gecrawlt", hotelId, totalCrawled);
                            scopedLogger.LogWarning("───────────────────────────────────────────────────");

                            // ============================================================
                            // AUTOMATISCHER FALLBACK ZU PLAYWRIGHT BEI UNZUREICHENDEM CONTENT
                            // ============================================================
                            bool needsPlaywrightRetry = false;
                            string retryReason = string.Empty;

                            // Berechne durchschnittliche Content-Länge aus allen gecrawlten Chunks
                            double avgContentLength = allChunksForQualityCheck.Any() 
                                ? allChunksForQualityCheck.Average(c => c.Content?.Length ?? 0)
                                : 0;

                            scopedLogger.LogWarning(
                                "📊 CONTENT-QUALITÄT: {Count} Chunks, Ø {AvgLength:F0} Zeichen, Expected: {Expected} URLs",
                                totalCrawled, avgContentLength, jobStatus.CurrentHotelTotalPages);

                            // Kriterium 1: Zu wenig Seiten gecrawlt (< 3 Seiten)
                            if (totalCrawled < 3)
                            {
                                needsPlaywrightRetry = true;
                                retryReason = $"Nur {totalCrawled} Seiten gecrawlt (Minimum: 3)";
                                scopedLogger.LogWarning("⚠️ KRITERIUM 1 ERFÜLLT: {Reason}", retryReason);
                            }
                            // Kriterium 2: Sitemap hatte URLs, aber fast nichts wurde gecrawlt (< 20% Coverage)
                            else if (jobStatus.CurrentHotelTotalPages > 0 && 
                                     totalCrawled < jobStatus.CurrentHotelTotalPages * 0.2)
                            {
                                needsPlaywrightRetry = true;
                                retryReason = $"Schlechte Coverage: {totalCrawled}/{jobStatus.CurrentHotelTotalPages} = {((double)totalCrawled / jobStatus.CurrentHotelTotalPages * 100):F1}%";
                                scopedLogger.LogWarning("⚠️ KRITERIUM 2 ERFÜLLT: {Reason}", retryReason);
                            }
                            // Kriterium 3: Durchschnittliche Content-Länge zu gering
                            else if (totalCrawled > 0 && avgContentLength < 500)
                            {
                                needsPlaywrightRetry = true;
                                retryReason = $"Durchschnittliche Content-Länge zu gering: {avgContentLength:F0} Zeichen (Minimum: 500)";
                                scopedLogger.LogWarning("⚠️ KRITERIUM 3 ERFÜLLT: {Reason}", retryReason);
                            }
                            else
                            {
                                scopedLogger.LogWarning("✓ KEIN FALLBACK-KRITERIUM ERFÜLLT - WebCrawler-Ergebnis wird verwendet");
                            }

                            if (needsPlaywrightRetry)
                            {
                                // ⚠️ WICHTIG: Bei inkrementellem Crawling (SkipExistingUrls) KEINEN Playwright-Fallback!
                                // Grund: Fallback würde ALLE Chunks (inkl. existierende) löschen
                                if (request.SkipExistingUrls == true)
                                {
                                    scopedLogger.LogWarning("═══════════════════════════════════════════════════");
                                    scopedLogger.LogWarning("⏭️ PLAYWRIGHT-FALLBACK ÜBERSPRUNGEN für {HotelId}", hotelId);
                                    scopedLogger.LogWarning("Grund (Fallback wäre nötig): {Reason}", retryReason);
                                    scopedLogger.LogWarning("⚠️ Inkrementelles Crawling aktiviert → Bestehende Daten werden NICHT gelöscht");
                                    scopedLogger.LogWarning("💡 Tipp: Für vollständiges Re-Crawling 'Nur neue URLs' deaktivieren");
                                    scopedLogger.LogWarning("═══════════════════════════════════════════════════");
                                    // Behalte die bereits gespeicherten Chunks, kein Playwright-Retry
                                }
                                else if (scopedPlaywrightCrawlerService is NullPlaywrightCrawlerService)
                                {
                                    scopedLogger.LogError(
                                        "Playwright-Fallback für {HotelId} abgebrochen: {Reason}. {Hint}",
                                        hotelId, retryReason, NullPlaywrightCrawlerService.DisabledMessage);
                                    jobStatus.Results.Add(new CrawlResult
                                    {
                                        Url = url,
                                        HotelId = hotelId,
                                        Success = false,
                                        PagesProcessed = totalCrawled,
                                        Error = NullPlaywrightCrawlerService.DisabledMessage
                                    });
                                    jobStatus.ProcessedHotels = i + 1;
                                    continue;
                                }
                                else
                                {
                                    scopedLogger.LogError("═══════════════════════════════════════════════════");
                                    scopedLogger.LogError("🎭 PLAYWRIGHT-FALLBACK AKTIVIERT für {HotelId}", hotelId);
                                    scopedLogger.LogError("Grund: {Reason}", retryReason);
                                    scopedLogger.LogError("═══════════════════════════════════════════════════");

                                    // Alte Werte merken für Vergleich
                                    int oldCrawledCount = totalCrawled;
                                    double oldAvgContentLength = avgContentLength;

                                    // Alte Chunks löschen (NUR bei vollständigem Re-Crawling)
                                    scopedLogger.LogInformation("Lösche {Count} unzureichende Chunks vor Playwright-Retry", totalCrawled);
                                    await scopedVectorStore.DeleteHotelChunksAsync(hotelId, CancellationToken.None);

                                    // Reset für Playwright-Crawling
                                    chunkBatch.Clear();
                                    allChunksForQualityCheck.Clear();
                                    totalCrawled = 0;
                                    jobStatus.CurrentHotelPages = 0;

                                    // Playwright-Crawling (ohne excludeUrls, da wir neu starten)
                                    scopedLogger.LogInformation("🎭 Starte Playwright-Crawler für {HotelId}", hotelId);

                                    if (request.UseSitemap ?? true)
                                    {
                                        scopedLogger.LogInformation("Versuche Sitemap-basiertes Playwright-Crawling für {HotelId}", hotelId);
                                        crawledCount = await scopedPlaywrightCrawlerService.CrawlFromSitemapAsync(
                                            url,
                                            new List<string> { baseHost, $"www.{baseHost}" },
                                            ProcessChunk,
                                            null, // Keine excludeUrls beim Fallback, da wir alles neu crawlen
                                            CancellationToken.None);

                                        if (crawledCount == 0)
                                        {
                                            scopedLogger.LogInformation("Sitemap leer – Fallback zu rekursivem Playwright-Crawling für {HotelId}", hotelId);
                                            crawledCount = await scopedPlaywrightCrawlerService.CrawlWebsiteAsync(
                                                url,
                                                request.MaxDepth ?? 2,
                                                request.MaxPages ?? 200,
                                                new List<string> { baseHost, $"www.{baseHost}" },
                                                ProcessChunk,
                                                CancellationToken.None);
                                        }
                                    }
                                    else
                                    {
                                        scopedLogger.LogInformation("Verwende rekursives Playwright-Crawling für {HotelId}", hotelId);
                                        crawledCount = await scopedPlaywrightCrawlerService.CrawlWebsiteAsync(
                                            url,
                                            request.MaxDepth ?? 2,
                                            request.MaxPages ?? 200,
                                            new List<string> { baseHost, $"www.{baseHost}" },
                                            ProcessChunk,
                                            CancellationToken.None);
                                    }

                                    // Finale Chunks nach Playwright speichern
                                    if (chunkBatch.Count > 0)
                                    {
                                        scopedLogger.LogInformation("Speichere finalen Playwright-Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                                        await scopedVectorStore.AddChunksAsync(chunkBatch, CancellationToken.None);
                                    }

                                    // Berechne neue durchschnittliche Content-Länge nach Playwright
                                    double newAvgContentLength = allChunksForQualityCheck.Any() 
                                        ? allChunksForQualityCheck.Average(c => c.Content?.Length ?? 0)
                                        : 0;

                                    scopedLogger.LogError("═══════════════════════════════════════════════════");
                                    scopedLogger.LogError(
                                        "✅ PLAYWRIGHT-FALLBACK ERFOLGREICH für {HotelId}",
                                        hotelId);
                                    scopedLogger.LogError(
                                        "Ergebnis: {NewCount} Seiten (vorher: {OldCount}), Ø {NewAvg:F0} Zeichen (vorher: {OldAvg:F0})",
                                        totalCrawled, oldCrawledCount, newAvgContentLength, oldAvgContentLength);
                                    scopedLogger.LogError("═══════════════════════════════════════════════════");
                                }
                            }

                            jobStatus.Results.Add(new CrawlResult
                            {
                                Url = url,
                                HotelId = hotelId,
                                Success = true,
                                PagesProcessed = totalCrawled
                            });

                            scopedLogger.LogWarning("═══════════════════════════════════════════════════");
                            scopedLogger.LogWarning(
                                "🏁 FINAL: {HotelId} abgeschlossen mit {Count} Seiten",
                                hotelId, totalCrawled);
                            scopedLogger.LogWarning("═══════════════════════════════════════════════════");
                        }
                        catch (Exception ex)
                        {
                            scopedLogger.LogError(ex, "Fehler beim Crawlen von {Url}", url);
                            jobStatus.Results.Add(new CrawlResult
                            {
                                Url = url,
                                Success = false,
                                Error = ex.Message
                            });
                        }
                        
                        // Fortschritt aktualisieren
                        jobStatus.ProcessedHotels = i + 1;
                    }
                    
                    // Job abgeschlossen
                    jobStatus.Status = "completed";
                    jobStatus.CompletedAt = DateTime.UtcNow;
                    
                    scopedLogger.LogInformation("Batch-Crawling abgeschlossen für Job {JobId}: {Total} Hotels", jobId, request.Urls.Count);
                }
                catch (Exception ex)
                {
                    scopedLogger.LogError(ex, "Fehler beim Batch-Crawling Job {JobId}", jobId);
                    jobStatus.Status = "failed";
                    jobStatus.CompletedAt = DateTime.UtcNow;
                }
            });
            
            // Sofort Job-ID zurückgeben
            return Ok(new
            {
                jobId = jobId,
                message = "Crawl-Job gestartet",
                totalHotels = request.Urls.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Starten des Crawl-Jobs");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Crawlt mehrere Websites mit Headless-Browser (Playwright).
    /// Scrollt automatisch durch jede Seite, um lazy-geladene Inhalte zu erfassen.
    /// </summary>
    [HttpPost("crawl-multiple-hotels-headless")]
    [SwaggerOperation(
        Summary = "🖥️ Crawl Multiple Hotels (Headless Browser)",
        Description = @"Crawlt mehrere Hotel-Websites mit einem Headless-Chromium-Browser (Playwright).
Scrollt automatisch durch jede Seite, damit lazy-geladene Inhalte vollständig erfasst werden.

Verwende diesen Endpunkt, wenn Inhalte erst beim Scrollen sichtbar werden.

**Empfohlener Request:**
```json
{
  ""urls"": [
    ""https://www.stock.at/"",
    ""https://www.post-lermoos.at/""
  ],
  ""useSitemap"": true
}
```

⚠️ Hinweis: Headless-Crawling ist langsamer als HTTP-basiertes Crawling.
Sicherstellen, dass Playwright-Browser installiert sind (`playwright install chromium`).",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [SwaggerResponse(200, "Crawling erfolgreich abgeschlossen", typeof(CrawlMultipleResponse))]
    [SwaggerResponse(400, "Ungültige Anfrage")]
    [SwaggerResponse(500, "Interner Serverfehler")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CrawlMultipleHotelsHeadless(
        [FromBody] CrawlMultipleHeadlessRequest request,
        CancellationToken cancellationToken)
    {
        if (_playwrightCrawlerService is NullPlaywrightCrawlerService)
        {
            return StatusCode(503, new
            {
                error = NullPlaywrightCrawlerService.DisabledMessage,
                hint = "POST crawl requests against the crawl-worker on port 5002"
            });
        }

        try
        {
            var results = new List<object>();

            foreach (var url in request.Urls)
            {
                try
                {
                    _logger.LogInformation("Starte Playwright-Crawling für {Url}", url);

                    var uri = new Uri(url);
                    var baseHost = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
                    var hotelId = $"hotel_{baseHost.Replace(".", "_")}";

                    var hotel = await _hotelRepository.GetByIdAsync(hotelId, cancellationToken);
                    if (hotel == null)
                    {
                        var rawName = baseHost.Replace(".at", "").Replace(".com", "");
                        hotel = new Domain.Entities.Hotel
                        {
                            HotelId = hotelId,
                            Name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rawName),
                            Domain = baseHost,
                            AllowedDomains = new List<string> { baseHost, $"www.{baseHost}" },
                            ApiKey = Guid.NewGuid().ToString(),
                            IsActive = true
                        };
                        await _hotelRepository.AddAsync(hotel);
                        _logger.LogInformation("Hotel erstellt: {HotelId}", hotelId);
                    }

                    _logger.LogInformation("Lösche alte Chunks für {HotelId} vor Re-Crawling", hotelId);
                    await _vectorStore.DeleteHotelChunksAsync(hotelId, cancellationToken);

                    var chunkBatch = new List<ContentChunk>();
                    const int batchSize = 50;
                    int totalCrawled = 0;

                    async Task ProcessChunk(ContentChunk chunk)
                    {
                        chunk.HotelId = hotelId;
                        chunkBatch.Add(chunk);
                        totalCrawled++;

                        if (chunkBatch.Count >= batchSize)
                        {
                            _logger.LogInformation("Speichere Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                            await _vectorStore.AddChunksAsync(chunkBatch, cancellationToken);
                            chunkBatch.Clear();
                        }
                    }

                    int crawledCount;

                    if (request.UseSitemap ?? true)
                    {
                        _logger.LogInformation("Versuche Sitemap-basiertes Playwright-Crawling für {HotelId}", hotelId);
                        crawledCount = await _playwrightCrawlerService.CrawlFromSitemapAsync(
                            url,
                            new List<string> { baseHost, $"www.{baseHost}" },
                            ProcessChunk,
                            null, // excludeUrls - nicht verwendet im Headless-Endpoint
                            cancellationToken);

                        if (crawledCount == 0)
                        {
                            _logger.LogInformation("Sitemap leer – Fallback zu rekursivem Playwright-Crawling für {HotelId}", hotelId);
                            crawledCount = await _playwrightCrawlerService.CrawlWebsiteAsync(
                                url,
                                request.MaxDepth ?? 2,
                                request.MaxPages ?? 200,
                                new List<string> { baseHost, $"www.{baseHost}" },
                                ProcessChunk,
                                cancellationToken);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Verwende rekursives Playwright-Crawling für {HotelId}", hotelId);
                        crawledCount = await _playwrightCrawlerService.CrawlWebsiteAsync(
                            url,
                            request.MaxDepth ?? 2,
                            request.MaxPages ?? 200,
                            new List<string> { baseHost, $"www.{baseHost}" },
                            ProcessChunk,
                            cancellationToken);
                    }

                    if (chunkBatch.Count > 0)
                    {
                        _logger.LogInformation("Speichere finalen Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                        await _vectorStore.AddChunksAsync(chunkBatch, cancellationToken);
                    }

                    results.Add(new
                    {
                        url = url,
                        hotelId = hotelId,
                        success = true,
                        pagesProcessed = totalCrawled
                    });

                    _logger.LogInformation(
                        "Playwright-Crawling abgeschlossen für {HotelId}: {Count} Seiten",
                        hotelId, totalCrawled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Playwright-Crawlen von {Url}", url);
                    results.Add(new
                    {
                        url = url,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            return Ok(new
            {
                message = "Headless-Batch-Crawling abgeschlossen",
                totalUrls = request.Urls.Count,
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Headless-Batch-Crawling");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Detaillierte Crawl-Analyse aller Hotels.
    /// Zeigt gecrawlte Chunks, Sitemap-URLs und Diagnose-Status.
    /// </summary>
    [HttpGet("crawl/hotels-analysis")]
    [SwaggerOperation(
        Summary = "🔍 Hotels-Crawl-Analyse",
        Description = "Analysiert Crawling-Status für alle Hotels: gecrawlte Chunks, Sitemap-URLs, Probleme. ⚡ Optimiert mit Parallelisierung und Caching.",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHotelsAnalysis(CancellationToken cancellationToken)
    {
        try
        {
            var hotels = await _hotelRepository.GetAllAsync(cancellationToken);

            // Begrenzte Parallelität (max. 4), um HTTP-/Socket-Stampede zu vermeiden
            var analysis = new object[hotels.Count];
            using var gate = new SemaphoreSlim(4);
            var tasks = hotels.Select(async (hotel, index) =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    analysis[index] = await AnalyzeHotelAsync(hotel, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);

            return Ok(new
            {
                totalHotels = hotels.Count,
                analysis = analysis
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Hotels-Analyse");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Analysiert ein einzelnes Hotel (mit Caching für Sitemap-Daten).
    /// </summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<object> AnalyzeHotelAsync(Domain.Entities.Hotel hotel, CancellationToken cancellationToken)
    {
        try
        {
            var chunkCount = await _vectorStore.GetChunkCountAsync(hotel.HotelId, cancellationToken);
            
            // ⚡ OPTIMIERUNG: Sitemap-Daten aus Cache laden
            // Erfolgreiche Requests: 1 Stunde, Fehler: 5 Minuten
            var cacheKey = $"sitemap_{hotel.Domain}";
            var sitemapData = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                var data = await LoadSitemapDataAsync(hotel.Domain, cancellationToken);
                
                // Fehler kürzer cachen als erfolgreiche Requests
                if (data?.Status == "error" || data?.Status == "not_found" || data?.UrlCount == 0)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                }
                else
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                }
                
                return data;
            });

            // Diagnose-Status bestimmen
            string status;
            if (chunkCount == 0)
            {
                status = "not_crawled";
            }
            else if (chunkCount < 10)
            {
                status = "problematic";
            }
            else if (chunkCount < 30)
            {
                status = "warning";
            }
            else
            {
                status = "good";
            }

            return new
            {
                hotelId = hotel.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                isActive = hotel.IsActive,
                chunks = chunkCount,
                sitemapUrls = sitemapData?.UrlCount ?? 0,
                sitemapStatus = sitemapData?.Status ?? "unknown",
                status = status,
                coverage = sitemapData?.UrlCount > 0 
                    ? ((double)chunkCount / sitemapData.UrlCount * 100).ToString("F1") + "%" 
                    : "n/a"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler bei Analyse von {HotelId}", hotel.HotelId);
            return new
            {
                hotelId = hotel.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                isActive = hotel.IsActive,
                chunks = 0,
                sitemapUrls = 0,
                sitemapStatus = "error",
                status = "error",
                coverage = "n/a",
                error = ex.Message
            };
        }
    }

    /// <summary>
    /// Lädt Sitemap-Daten (URL-Count und Status) für eine Domain.
    /// Unterstützt Sitemap-Indizes mit parallelem Laden der Sub-Sitemaps.
    /// </summary>
    private async Task<SitemapData?> LoadSitemapDataAsync(string domain, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10); // Erhöht für Sitemap-Indizes mit mehreren Sub-Sitemaps
        
        try
        {
            var sitemapUrl = $"https://{domain}/sitemap.xml";
            _logger.LogDebug("Lade Sitemap für {Domain}: {Url}", domain, sitemapUrl);
            
            var response = await httpClient.GetAsync(sitemapUrl, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sitemap nicht gefunden für {Domain}: HTTP {StatusCode}", domain, response.StatusCode);
                return new SitemapData { Status = "not_found", UrlCount = 0 };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            int urlCount = 0;
            string status;
            
            // Prüfe ob es ein Sitemap-Index ist
            if (content.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase))
            {
                // Extrahiere alle Sub-Sitemap URLs
                var subSitemapMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"<loc>(.*?)</loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                var subSitemapUrls = subSitemapMatches.Select(m => m.Groups[1].Value.Trim()).ToList();
                _logger.LogDebug("Sitemap-Index für {Domain} enthält {Count} Sub-Sitemaps", domain, subSitemapUrls.Count);
                
                // ⚡ OPTIMIERUNG: Sub-Sitemaps parallel laden
                var subSitemapTasks = subSitemapUrls.Select(async (subSitemapUrl, index) =>
                {
                    try
                    {
                        _logger.LogDebug("Lade Sub-Sitemap {Index}/{Total} für {Domain}: {Url}", 
                            index + 1, subSitemapUrls.Count, domain, subSitemapUrl);
                            
                        var subResponse = await httpClient.GetAsync(subSitemapUrl, cancellationToken);
                        if (subResponse.IsSuccessStatusCode)
                        {
                            var subContent = await subResponse.Content.ReadAsStringAsync(cancellationToken);
                            var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                                subContent, @"<url>\s*<loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            _logger.LogDebug("Sub-Sitemap {Index} für {Domain} enthält {Count} URLs", 
                                index + 1, domain, urlMatches.Count);
                            
                            return urlMatches.Count;
                        }
                        else
                        {
                            _logger.LogWarning("Sub-Sitemap {Index} für {Domain} fehlgeschlagen: HTTP {StatusCode}", 
                                index + 1, domain, subResponse.StatusCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Fehler beim Laden von Sub-Sitemap {Index} für {Domain}: {Url}", 
                            index + 1, domain, subSitemapUrl);
                    }
                    return 0;
                });
                
                var subSitemapCounts = await Task.WhenAll(subSitemapTasks);
                urlCount = subSitemapCounts.Sum();
                status = urlCount > 0 ? "found_index" : "found_empty";
                
                _logger.LogInformation("Sitemap-Index für {Domain}: {Total} URLs aus {SubSitemaps} Sub-Sitemaps", 
                    domain, urlCount, subSitemapUrls.Count);
            }
            else
            {
                // Reguläre Sitemap - zähle URLs direkt
                var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"<url>\s*<loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                urlCount = urlMatches.Count;
                status = "found";
                
                _logger.LogDebug("Reguläre Sitemap für {Domain}: {Count} URLs", domain, urlCount);
            }

            return new SitemapData { Status = status, UrlCount = urlCount };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning("Timeout beim Laden der Sitemap für {Domain}: {Message}", domain, ex.Message);
            return new SitemapData { Status = "error", UrlCount = 0 };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP-Fehler beim Laden der Sitemap für {Domain}", domain);
            return new SitemapData { Status = "error", UrlCount = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unerwarteter Fehler beim Laden der Sitemap für {Domain}", domain);
            return new SitemapData { Status = "error", UrlCount = 0 };
        }
    }

    /// <summary>
    /// Analysiert Crawling-Probleme für ein spezifisches Hotel.
    /// </summary>
    [HttpPost("crawl/analyze-problems")]
    [SwaggerOperation(
        Summary = "🔬 Problem-Diagnose",
        Description = "Analysiert warum ein Hotel wenig Content hat. Prüft Sitemap, JavaScript-Nutzung, Robots.txt.",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeCrawlProblems(
        [FromBody] AnalyzeProblemsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var hotel = await _hotelRepository.GetByIdAsync(request.HotelId, cancellationToken);
            if (hotel == null)
            {
                return NotFound(new { error = $"Hotel {request.HotelId} nicht gefunden" });
            }

            var diagnostics = new List<string>();
            var recommendations = new List<string>();

            // 1. Prüfe Chunk-Anzahl
            var chunkCount = await _vectorStore.GetChunkCountAsync(request.HotelId, cancellationToken);
            diagnostics.Add($"Gecrawlte Chunks: {chunkCount}");

            // 2. Prüfe Sitemap
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var sitemapUrl = $"https://{hotel.Domain}/sitemap.xml";
            bool hasSitemap = false;
            int sitemapUrls = 0;
            
            try
            {
                var response = await httpClient.GetAsync(sitemapUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // Prüfe ob es ein Sitemap-Index ist
                    if (content.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase))
                    {
                        var subSitemapMatches = System.Text.RegularExpressions.Regex.Matches(
                            content, @"<loc>(.*?)</loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        
                        diagnostics.Add($"✓ Sitemap-Index gefunden mit {subSitemapMatches.Count} Sub-Sitemaps");
                        
                        foreach (System.Text.RegularExpressions.Match match in subSitemapMatches)
                        {
                            try
                            {
                                var subSitemapUrl = match.Groups[1].Value.Trim();
                                var subResponse = await httpClient.GetAsync(subSitemapUrl, cancellationToken);
                                
                                if (subResponse.IsSuccessStatusCode)
                                {
                                    var subContent = await subResponse.Content.ReadAsStringAsync(cancellationToken);
                                    var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                                        subContent, @"<url>\s*<loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    sitemapUrls += urlMatches.Count;
                                }
                            }
                            catch
                            {
                                // Sub-Sitemap konnte nicht geladen werden
                            }
                        }
                        diagnostics.Add($"→ Gesamt in allen Sub-Sitemaps: {sitemapUrls} URLs");
                    }
                    else
                    {
                        // Reguläre Sitemap
                        var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                            content, @"<url>\s*<loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        sitemapUrls = urlMatches.Count;
                        diagnostics.Add($"✓ Sitemap gefunden: {sitemapUrls} URLs");
                    }
                    
                    hasSitemap = true;
                    
                    if (sitemapUrls > 0 && sitemapUrls > chunkCount * 2)
                    {
                        diagnostics.Add($"⚠ Coverage gering: {chunkCount}/{sitemapUrls} = {((double)chunkCount / sitemapUrls * 100):F1}%");
                        recommendations.Add("Viele URLs in Sitemap, aber wenig gecrawlt → Headless-Browser probieren");
                    }
                }
                else
                {
                    diagnostics.Add("✗ Keine sitemap.xml gefunden");
                    recommendations.Add("Keine Sitemap → Rekursives Crawling mit höherer maxDepth (3-4) versuchen");
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"✗ Sitemap-Fehler: {ex.Message}");
            }

            // 3. Prüfe ob Website erreichbar
            try
            {
                var homeResponse = await httpClient.GetAsync($"https://{hotel.Domain}", cancellationToken);
                var html = await homeResponse.Content.ReadAsStringAsync(cancellationToken);
                
                // Prüfe auf JavaScript-Frameworks
                var jsFrameworks = new List<string>();
                if (html.Contains("react", StringComparison.OrdinalIgnoreCase)) jsFrameworks.Add("React");
                if (html.Contains("vue", StringComparison.OrdinalIgnoreCase)) jsFrameworks.Add("Vue");
                if (html.Contains("angular", StringComparison.OrdinalIgnoreCase)) jsFrameworks.Add("Angular");
                if (html.Contains("next.js", StringComparison.OrdinalIgnoreCase)) jsFrameworks.Add("Next.js");
                
                if (jsFrameworks.Any())
                {
                    diagnostics.Add($"⚠ JavaScript-Framework erkannt: {string.Join(", ", jsFrameworks)}");
                    recommendations.Add($"JS-Framework erkannt → Headless-Browser (Playwright) verwenden: /api/admin/crawl-multiple-hotels-headless");
                }
                
                // Prüfe auf Lazy Loading
                if (html.Contains("loading=\"lazy\"", StringComparison.OrdinalIgnoreCase) || 
                    html.Contains("lazyload", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add("⚠ Lazy-Loading erkannt");
                    recommendations.Add("Lazy-Loading → Headless-Browser verwenden");
                }
                
                diagnostics.Add($"✓ Website erreichbar ({html.Length} Bytes HTML)");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"✗ Website nicht erreichbar: {ex.Message}");
                recommendations.Add("Website nicht erreichbar → Domain/SSL-Zertifikat prüfen");
            }

            // 4. Empfehlungen basierend auf Chunk-Anzahl
            if (chunkCount < 10)
            {
                recommendations.Add("< 10 Chunks → Dringend Headless-Crawler verwenden oder MaxPages erhöhen");
            }

            return Ok(new
            {
                hotelId = request.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                chunks = chunkCount,
                sitemapUrls = sitemapUrls,
                hasSitemap = hasSitemap,
                diagnostics = diagnostics,
                recommendations = recommendations
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Problem-Analyse");
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Aktualisiert Hotel-Daten (Name, Domain, AllowedDomains, IsActive).
    /// </summary>
    [HttpPut("hotels/{hotelId}")]
    [SwaggerOperation(
        Summary = "✏️ Hotel aktualisieren",
        Description = "Ändert Hotel-Eigenschaften (Name, Domain, Allowed Domains, Aktivstatus).",
        Tags = new[] { "🏨 Hotel-Verwaltung" }
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHotel(
        string hotelId,
        [FromBody] UpdateHotelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var hotel = await _hotelRepository.GetByIdAsync(hotelId, cancellationToken);
            if (hotel == null)
            {
                return NotFound(new { error = $"Hotel {hotelId} nicht gefunden" });
            }

            // Update properties
            if (!string.IsNullOrWhiteSpace(request.Name))
                hotel.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Domain))
                hotel.Domain = request.Domain;

            if (request.AllowedDomains != null && request.AllowedDomains.Any())
                hotel.AllowedDomains = request.AllowedDomains;

            if (request.IsActive.HasValue)
                hotel.IsActive = request.IsActive.Value;

            if (request.Location != null)
                hotel.Location = NullIfBlank(request.Location);
            if (request.Region != null)
                hotel.Region = NullIfBlank(request.Region);
            if (request.Country != null)
                hotel.Country = NullIfBlank(request.Country);
            if (request.Latitude.HasValue)
                hotel.Latitude = request.Latitude;
            if (request.Longitude.HasValue)
                hotel.Longitude = request.Longitude;
            if (request.OfficialUrl != null)
                hotel.OfficialUrl = NullIfBlank(request.OfficialUrl);
            if (request.SourceUrl != null)
                hotel.SourceUrl = NullIfBlank(request.SourceUrl);
            if (request.EditorialReviewStatus != null)
                hotel.EditorialReviewStatus = NullIfBlank(request.EditorialReviewStatus);
            if (request.EditorialReviewedAt.HasValue)
                hotel.EditorialReviewedAt = request.EditorialReviewedAt;
            if (request.Categories != null)
                hotel.Categories = request.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            await _hotelRepository.UpdateAsync(hotel);

            return Ok(new
            {
                message = "Hotel erfolgreich aktualisiert",
                hotelId = hotel.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                allowedDomains = hotel.AllowedDomains,
                isActive = hotel.IsActive,
                location = hotel.Location,
                region = hotel.Region,
                country = hotel.Country,
                latitude = hotel.Latitude,
                longitude = hotel.Longitude,
                officialUrl = hotel.OfficialUrl,
                sourceUrl = hotel.SourceUrl,
                editorialReviewStatus = hotel.EditorialReviewStatus,
                editorialReviewedAt = hotel.EditorialReviewedAt,
                categories = hotel.Categories
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren des Hotels {HotelId}", hotelId);
            return StatusCode(500, new { error = "Interner Serverfehler" });
        }
    }

    /// <summary>
    /// Ruft den Status eines Crawl-Jobs ab.
    /// </summary>
    [HttpGet("crawl/status/{jobId}")]
    [SwaggerOperation(
        Summary = "📊 Crawl-Job-Status",
        Description = "Ruft den aktuellen Status eines laufenden oder abgeschlossenen Crawl-Jobs ab.",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetCrawlStatus(string jobId)
    {
        PruneOldCrawlJobs();

        if (_crawlJobs.TryGetValue(jobId, out var status))
        {
            return Ok(status);
        }
        
        return NotFound(new { error = "Job nicht gefunden" });
    }

    private static void PruneOldCrawlJobs()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);
        foreach (var kvp in _crawlJobs)
        {
            var job = kvp.Value;
            var done = string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase);
            if (done && job.CompletedAt.HasValue && job.CompletedAt.Value < cutoff)
                _crawlJobs.TryRemove(kvp.Key, out _);
            else if (done && !job.CompletedAt.HasValue && job.StartedAt < cutoff)
                _crawlJobs.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Testet den Crawler mit automatischem Playwright-Fallback.
    /// </summary>
    [HttpPost("test-crawler")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestCrawler(
        [FromBody] TestCrawlerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = request.Url;
            
            _logger.LogInformation("🧪 CRAWLER-TEST gestartet für: {Url}", url);
            
            // Phase 1: WebCrawler (Standard)
            var webCrawlerStart = DateTime.UtcNow;
            var webChunk = await _crawlerService.CrawlPageAsync(url, cancellationToken);
            var webCrawlerDuration = (DateTime.UtcNow - webCrawlerStart).TotalMilliseconds;
            
            var webResult = new
            {
                success = webChunk != null,
                contentLength = webChunk?.Content?.Length ?? 0,
                title = webChunk?.Title ?? "N/A",
                language = webChunk?.Language ?? "N/A",
                content = webChunk?.Content ?? "Kein Content gefunden",
                durationMs = (int)webCrawlerDuration
            };
            
            _logger.LogInformation("📊 WebCrawler: {Length} Zeichen in {Duration}ms", 
                webResult.contentLength, webResult.durationMs);
            
            // Phase 2: Prüfe ob Playwright-Fallback nötig ist
            bool needsPlaywright = webChunk == null || webChunk.Content.Length < 50;
            
            object? playwrightResult = null;
            
            if (needsPlaywright)
            {
                if (_playwrightCrawlerService is NullPlaywrightCrawlerService)
                {
                    return Ok(new
                    {
                        url,
                        timestamp = DateTime.UtcNow,
                        webCrawler = webResult,
                        fallbackTriggered = true,
                        playwright = (object?)null,
                        error = NullPlaywrightCrawlerService.DisabledMessage,
                        recommendation = "Crawl-Worker mit Playwright nutzen (Port 5002)"
                    });
                }

                _logger.LogWarning("🎭 PLAYWRIGHT-FALLBACK wird gestartet (WebCrawler: {Length} Zeichen)", 
                    webResult.contentLength);
                
                var playwrightStart = DateTime.UtcNow;
                var playwrightChunk = await _playwrightCrawlerService.CrawlPageAsync(url, cancellationToken);
                var playwrightDuration = (DateTime.UtcNow - playwrightStart).TotalMilliseconds;
                
                var playwrightContentLength = playwrightChunk?.Content?.Length ?? 0;
                
                playwrightResult = new
                {
                    success = playwrightChunk != null,
                    contentLength = playwrightContentLength,
                    title = playwrightChunk?.Title ?? "N/A",
                    language = playwrightChunk?.Language ?? "N/A",
                    content = playwrightChunk?.Content ?? "Kein Content gefunden",
                    durationMs = (int)playwrightDuration
                };
                
                _logger.LogInformation("✅ Playwright: {Length} Zeichen in {Duration}ms", 
                    playwrightContentLength, 
                    (int)playwrightDuration);
            }
            else
            {
                _logger.LogInformation("✓ WebCrawler erfolgreich - kein Playwright-Fallback nötig");
            }
            
            return Ok(new
            {
                url,
                timestamp = DateTime.UtcNow,
                webCrawler = webResult,
                playwrightFallback = playwrightResult,
                fallbackTriggered = needsPlaywright,
                recommendation = needsPlaywright 
                    ? "Playwright wird empfohlen für diese Website (SPA/JavaScript-Heavy)" 
                    : "WebCrawler reicht aus für diese Website"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Fehler beim Crawler-Test");
            return StatusCode(500, new { error = "Interner Serverfehler", details = ex.Message });
        }
    }
}

/// <summary>
/// Request-DTO für Content-Indexierung.
/// </summary>
public class IndexContentRequest
{
    public required string HotelId { get; set; }
    public required List<ContentChunkDto> Chunks { get; set; }
}

public class ContentChunkDto
{
    public required string SourceUrl { get; set; }
    public required string Content { get; set; }
    public string? Title { get; set; }
    public string? Language { get; set; }
}

/// <summary>
/// Request-DTO für Test-Suche.
/// </summary>
public class TestSearchRequest
{
    public required string HotelId { get; set; }
    public required string Query { get; set; }
    public int? TopK { get; set; }
    public double? MinScore { get; set; }
}

/// <summary>
/// Request-DTO für Website-Crawling.
/// </summary>
public class CrawlRequest
{
    public required string HotelId { get; set; }
    public required string StartUrl { get; set; }
    public int? MaxDepth { get; set; }
    public int? MaxPages { get; set; }
    public List<string>? AllowedDomains { get; set; }
}

/// <summary>
/// Request-DTO für Preview-Crawling.
/// </summary>
public class CrawlPreviewRequest
{
    public required string Url { get; set; }
}

/// <summary>
/// Request-DTO für Batch-Crawling mehrerer Websites.
/// </summary>
public class CrawlMultipleRequest
{
    /// <summary>
    /// Liste der zu crawlenden Hotel-URLs
    /// </summary>
    [SwaggerSchema("Liste der Hotel-Website URLs (z.B. https://www.stock.at/)")]
    public required List<string> Urls { get; set; }
    
    /// <summary>
    /// Versucht Sitemap.xml zu verwenden (Standard: true).
    /// Bei true: Crawlt ALLE Seiten aus sitemap.xml - maxDepth und maxPages werden ignoriert!
    /// Bei false oder Fallback: Nutzt rekursives Crawling mit maxDepth/maxPages Limits.
    /// </summary>
    [SwaggerSchema("Sitemap verwenden (empfohlen!) → crawlt ALLE Seiten ohne Limits")]
    public bool? UseSitemap { get; set; } = true;
    
    /// <summary>
    /// [NUR FÜR FALLBACK] Maximale Crawling-Tiefe wenn keine Sitemap gefunden wird (Standard: 2).
    /// WIRD IGNORIERT wenn Sitemap erfolgreich geladen wird.
    /// </summary>
    [SwaggerSchema("[Fallback] Crawling-Tiefe wenn keine Sitemap")]
    public int? MaxDepth { get; set; } = 2;
    
    /// <summary>
    /// [NUR FÜR FALLBACK] Maximale Anzahl Seiten wenn keine Sitemap gefunden wird (Standard: 200).
    /// WIRD IGNORIERT wenn Sitemap erfolgreich geladen wird.
    /// </summary>
    [SwaggerSchema("[Fallback] Max. Seiten wenn keine Sitemap")]
    public int? MaxPages { get; set; } = 200;

    /// <summary>
    /// Nur neue URLs crawlen (überspringe bereits indexierte URLs).
    /// Standard: false (alle URLs werden neu gecrawlt, alte Daten werden gelöscht).
    /// Bei true: Behält bestehende Chunks und crawlt nur neue URLs aus der Sitemap.
    /// </summary>
    [SwaggerSchema("Nur neue URLs crawlen (inkrementelles Crawling)")]
    public bool? SkipExistingUrls { get; set; } = false;
}

/// <summary>
/// Request-DTO für Headless-Batch-Crawling mehrerer Websites.
/// </summary>
public class CrawlMultipleHeadlessRequest
{
    /// <summary>
    /// Liste der zu crawlenden Hotel-URLs
    /// </summary>
    [SwaggerSchema("Liste der Hotel-Website URLs")]
    public required List<string> Urls { get; set; }

    /// <summary>
    /// Versucht Sitemap.xml zu verwenden (Standard: true).
    /// </summary>
    [SwaggerSchema("Sitemap verwenden (empfohlen!) → crawlt ALLE Seiten ohne Limits")]
    public bool? UseSitemap { get; set; } = true;

    /// <summary>
    /// [NUR FÜR FALLBACK] Maximale Crawling-Tiefe wenn keine Sitemap gefunden wird (Standard: 2).
    /// </summary>
    [SwaggerSchema("[Fallback] Crawling-Tiefe wenn keine Sitemap")]
    public int? MaxDepth { get; set; } = 2;

    /// <summary>
    /// [NUR FÜR FALLBACK] Maximale Anzahl Seiten wenn keine Sitemap gefunden wird (Standard: 200).
    /// </summary>
    [SwaggerSchema("[Fallback] Max. Seiten wenn keine Sitemap")]
    public int? MaxPages { get; set; } = 200;

    /// <summary>
    /// Nur neue URLs crawlen (überspringe bereits indexierte URLs).
    /// Standard: false (alle URLs werden neu gecrawlt, alte Daten werden gelöscht).
    /// Bei true: Behält bestehende Chunks und crawlt nur neue URLs aus der Sitemap.
    /// </summary>
    [SwaggerSchema("Nur neue URLs crawlen (inkrementelles Crawling)")]
    public bool? SkipExistingUrls { get; set; } = false;
}

/// <summary>
/// Response für Batch-Crawling.
/// </summary>
public class CrawlMultipleResponse
{
    public string Message { get; set; } = string.Empty;
    public int TotalUrls { get; set; }
    public List<CrawlResult> Results { get; set; } = new();
}

public class CrawlResult
{
    public string Url { get; set; } = string.Empty;
    public string? HotelId { get; set; }
    public bool Success { get; set; }
    public int PagesProcessed { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Request-DTO für Crawler-Test.
/// </summary>
public class TestCrawlerRequest
{
    public required string Url { get; set; }
}

/// <summary>
/// Request-DTO für Problem-Analyse.
/// </summary>
public class AnalyzeProblemsRequest
{
    public required string HotelId { get; set; }
}

/// <summary>
/// Request-DTO für Hotel-Update.
/// </summary>
public class UpdateHotelRequest
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public List<string>? AllowedDomains { get; set; }
    public bool? IsActive { get; set; }
    public string? Location { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? OfficialUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string? EditorialReviewStatus { get; set; }
    public DateTime? EditorialReviewedAt { get; set; }
    public List<string>? Categories { get; set; }
}

/// <summary>
/// Status eines Crawl-Jobs.
/// </summary>
public class CrawlJobStatus
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "running"; // running, completed, failed
    public int TotalHotels { get; set; }
    public int ProcessedHotels { get; set; }
    public string? CurrentHotel { get; set; }
    public int CurrentHotelPages { get; set; }
    public int CurrentHotelTotalPages { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<CrawlResult> Results { get; set; } = new();
    
    // Detaillierte Statistiken
    public int PagesAttempted { get; set; }       // Gesamt versucht
    public int PagesSuccessful { get; set; }      // Erfolgreich gecrawlt
    public int PagesSkipped { get; set; }         // Übersprungen (existiert bereits)
    public int PagesLowContent { get; set; }      // Zu wenig Content (< 50 Zeichen)
    public int PagesError { get; set; }           // Fehler beim Crawlen
}

/// <summary>
/// Gecachte Sitemap-Daten für Performance-Optimierung.
/// </summary>
internal class SitemapData
{
    public string Status { get; set; } = string.Empty;
    public int UrlCount { get; set; }
}
