using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
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

    // In-Memory Status-Store für Crawl-Jobs
    private static readonly ConcurrentDictionary<string, CrawlJobStatus> _crawlJobs = new();

    public AdminController(
        IVectorStore vectorStore,
        IHotelRepository hotelRepository,
        ICrawlerService crawlerService,
        IPlaywrightCrawlerService playwrightCrawlerService,
        ILogger<AdminController> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _vectorStore = vectorStore;
        _hotelRepository = hotelRepository;
        _crawlerService = crawlerService;
        _playwrightCrawlerService = playwrightCrawlerService;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
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
                isActive = hotel.IsActive
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
                var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<AdminController>>();
                
                try
                {
                    for (int i = 0; i < request.Urls.Count; i++)
                    {
                        var url = request.Urls[i];
                        
                        try
                        {
                            scopedLogger.LogInformation("Starte Crawling für {Url} ({Index}/{Total})", url, i + 1, request.Urls.Count);

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

                            // Alte Chunks löschen vor Re-Crawling
                            scopedLogger.LogInformation("Lösche alte Chunks für {HotelId} vor Re-Crawling", hotelId);
                            await scopedVectorStore.DeleteHotelChunksAsync(hotelId, CancellationToken.None);

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
                            const int batchSize = 50;
                            int totalCrawled = 0;

                            // Callback für sofortige Verarbeitung + Status-Update
                            async Task ProcessChunk(ContentChunk chunk)
                            {
                                chunk.HotelId = hotelId;
                                chunkBatch.Add(chunk);
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
                                scopedLogger.LogInformation("Versuche Sitemap-basiertes Crawling für {HotelId}", hotelId);
                                crawledCount = await scopedCrawlerService.CrawlFromSitemapAsync(
                                    url,
                                    new List<string> { baseHost, $"www.{baseHost}" },
                                    ProcessChunk,
                                    CancellationToken.None);

                                // Fallback zu rekursivem Crawling, wenn Sitemap leer ist
                                if (crawledCount == 0)
                                {
                                    scopedLogger.LogInformation("Sitemap lieferte keine URLs. Fallback zu rekursivem Crawling für {HotelId}", hotelId);
                                    crawledCount = await scopedCrawlerService.CrawlWebsiteAsync(
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
                                scopedLogger.LogInformation("Verwende rekursives Crawling für {HotelId}", hotelId);
                                crawledCount = await scopedCrawlerService.CrawlWebsiteAsync(
                                    url,
                                    request.MaxDepth ?? 2,
                                    request.MaxPages ?? 200,
                                    new List<string> { baseHost, $"www.{baseHost}" },
                                    ProcessChunk,
                                    CancellationToken.None);
                            }

                            // Restliche Chunks im Batch speichern
                            if (chunkBatch.Count > 0)
                            {
                                scopedLogger.LogInformation("Speichere finalen Batch von {Count} Chunks für {HotelId}", chunkBatch.Count, hotelId);
                                await scopedVectorStore.AddChunksAsync(chunkBatch, CancellationToken.None);
                            }

                            jobStatus.Results.Add(new CrawlResult
                            {
                                Url = url,
                                HotelId = hotelId,
                                Success = true,
                                PagesProcessed = totalCrawled
                            });

                            scopedLogger.LogInformation(
                                "Crawling abgeschlossen für {HotelId}: {Count} Seiten",
                                hotelId, totalCrawled);
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
        Description = "Analysiert Crawling-Status für alle Hotels: gecrawlte Chunks, Sitemap-URLs, Probleme.",
        Tags = new[] { "🌐 Crawling & Indexierung" }
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHotelsAnalysis(CancellationToken cancellationToken)
    {
        try
        {
            var hotels = await _hotelRepository.GetAllAsync(cancellationToken);
            var analysis = new List<object>();

            foreach (var hotel in hotels)
            {
                try
                {
                    var chunkCount = await _vectorStore.GetChunkCountAsync(hotel.HotelId, cancellationToken);
                    
                    // Versuche Sitemap zu laden, um Seitenanzahl zu ermitteln
                    int sitemapUrlCount = 0;
                    string? sitemapStatus = null;
                    
                    try
                    {
                        var sitemapUrl = $"https://{hotel.Domain}/sitemap.xml";
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = TimeSpan.FromSeconds(10);
                        var response = await httpClient.GetAsync(sitemapUrl, cancellationToken);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync(cancellationToken);
                            
                            // Prüfe ob es ein Sitemap-Index ist
                            if (content.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase))
                            {
                                // Extrahiere alle Sub-Sitemap URLs
                                var subSitemapMatches = System.Text.RegularExpressions.Regex.Matches(
                                    content, @"<loc>(.*?)</loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
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
                                            sitemapUrlCount += urlMatches.Count;
                                        }
                                    }
                                    catch
                                    {
                                        // Sub-Sitemap konnte nicht geladen werden, ignorieren
                                    }
                                }
                                sitemapStatus = sitemapUrlCount > 0 ? "found_index" : "found_empty";
                            }
                            else
                            {
                                // Reguläre Sitemap - zähle URLs direkt
                                var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                                    content, @"<url>\s*<loc>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                sitemapUrlCount = urlMatches.Count;
                                sitemapStatus = "found";
                            }
                        }
                        else
                        {
                            sitemapStatus = "not_found";
                        }
                    }
                    catch
                    {
                        sitemapStatus = "error";
                    }

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

                    analysis.Add(new
                    {
                        hotelId = hotel.HotelId,
                        name = hotel.Name,
                        domain = hotel.Domain,
                        isActive = hotel.IsActive,
                        chunks = chunkCount,
                        sitemapUrls = sitemapUrlCount,
                        sitemapStatus = sitemapStatus,
                        status = status,
                        coverage = sitemapUrlCount > 0 ? ((double)chunkCount / sitemapUrlCount * 100).ToString("F1") + "%" : "n/a"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fehler bei Analyse von {HotelId}", hotel.HotelId);
                    analysis.Add(new
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
                    });
                }
            }

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

            await _hotelRepository.UpdateAsync(hotel);

            return Ok(new
            {
                message = "Hotel erfolgreich aktualisiert",
                hotelId = hotel.HotelId,
                name = hotel.Name,
                domain = hotel.Domain,
                allowedDomains = hotel.AllowedDomains,
                isActive = hotel.IsActive
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
        if (_crawlJobs.TryGetValue(jobId, out var status))
        {
            return Ok(status);
        }
        
        return NotFound(new { error = "Job nicht gefunden" });
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
}
