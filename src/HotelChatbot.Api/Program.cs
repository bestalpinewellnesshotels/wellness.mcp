using HotelChatbot.Application.Services;
using HotelChatbot.Domain.Interfaces;
using HotelChatbot.Infrastructure.Repositories;
using HotelChatbot.Infrastructure.Services;
using HotelChatbot.Infrastructure.VectorStore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Memory Cache für Performance-Optimierungen (z.B. Sitemap-Caching)
builder.Services.AddMemoryCache();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Hotel Chatbot API",
        Version = "v1",
        Description = "API für Hotel-Chatbot mit RAG, Vector Search, Voice und Web-Crawling",
        Contact = new()
        {
            Name = "Hotel Chatbot"
        }
    });
    options.EnableAnnotations();
});

// CORS Configuration - erlaubt Widget-Einbindung von verschiedenen Domains
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowChatbotWidget", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// HttpClient für Services
builder.Services.AddHttpClient<ITextToSpeechService, TextToSpeechService>();
builder.Services.AddHttpClient<ICrawlerService, WebCrawlerService>();
builder.Services.AddHttpClient();  // IHttpClientFactory für PlaywrightCrawlerService

// Domain Services
builder.Services.AddSingleton<IChatCompletionService, OpenAIChatCompletionService>();
builder.Services.AddScoped<ISpeechToTextService, SpeechToTextService>();
builder.Services.AddScoped<ITextToSpeechService, TextToSpeechService>();
builder.Services.AddSingleton<IEmbeddingService, AzureOpenAIEmbeddingService>();
builder.Services.AddScoped<ICrawlerService, WebCrawlerService>();
builder.Services.AddSingleton<IPlaywrightCrawlerService, PlaywrightCrawlerService>();

// Infrastructure Services - PostgreSQL mit pgvector für persistente Speicherung
builder.Services.AddSingleton<IVectorStore, PostgreSQLVectorStore>();
builder.Services.AddSingleton<IHotelRepository, PostgreSQLHotelRepository>();
builder.Services.AddSingleton<IChatSessionRepository, PostgreSQLChatSessionRepository>();
builder.Services.AddSingleton<ISystemPromptRepository, PostgreSQLSystemPromptRepository>();

// Query-Logger – schreibt .txt-Protokolle in 'log/' (neben 'wwwroot/')
var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "log");
builder.Services.AddSingleton<IQueryLogger>(new HotelChatbot.Infrastructure.Services.FileQueryLogger(logDirectory));

// Application Services
builder.Services.AddSingleton<SystemPromptService>();
builder.Services.AddSingleton<IPromptTranslationService, PromptTranslationService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddScoped<VoiceService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();  // wwwroot ausliefern (z.B. /admin/prompts.html)
app.UseCors("AllowChatbotWidget");
app.UseAuthorization();

// Bequemer Redirect: /admin  →  /admin/prompts.html
app.MapGet("/admin", () => Results.Redirect("/admin/prompts.html")).ExcludeFromDescription();

// Bequemer Redirect: /demo  →  /demo/index.html
app.MapGet("/demo", () => Results.Redirect("/demo/index.html")).ExcludeFromDescription();

// Root Endpoint mit Anleitung
app.MapGet("/", () => Results.Json(new
{
    message = "Hotel Chatbot API",
    version = "1.0",
    documentation = "/swagger",
    endpoints = new
    {
        swagger = "/swagger",
        admin_cms = "/admin",
        demo = "/demo",
        health = "/health",
        crawling = new
        {
            crawlMultipleHotels = "POST /api/admin/crawl-multiple-hotels",
            description = "Crawlt mehrere Hotel-Websites und indexiert sie automatisch",
            examplePayload = new
            {
                urls = new[] { "https://www.stock.at/", "https://www.post-lermoos.at/" },
                maxDepth = 2,
                maxPages = 50
            }
        },
        chat = "POST /api/chat",
        voice = new
        {
            transcribe = "POST /api/voice/transcribe",
            synthesize = "POST /api/voice/synthesize"
        }
    },
    quickStart = new
    {
        step1 = "Qdrant starten: docker run -p 6333:6333 qdrant/qdrant:latest",
        step2 = "Swagger öffnen: /swagger",
        step3 = "Hotels crawlen: POST /api/admin/crawl-multiple-hotels",
        step4 = "Chatbot testen: POST /api/chat"
    }
})).ExcludeFromDescription();

// Health Check Endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    services = new
    {
        api = "running",
        qdrant = "configure at /swagger"
    }
})).ExcludeFromDescription();

app.MapControllers();

// Seed Demo-Daten (deaktiviert - aktiviere dies wenn Azure OpenAI konfiguriert ist)
// await SeedDemoData(app.Services);

app.Run();

// Demo-Daten für Testing
static async Task SeedDemoData(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var hotelRepo = scope.ServiceProvider.GetRequiredService<IHotelRepository>();
    var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();

    // Demo Hotel anlegen
    var demoHotel = new HotelChatbot.Domain.Entities.Hotel
    {
        HotelId = "hotel_demo",
        Name = "Demo Hotel & Spa",
        Domain = "localhost",
        AllowedDomains = new List<string> { "localhost", "127.0.0.1" },
        ApiKey = "demo_key",
        IsActive = true
    };
    await hotelRepo.AddAsync(demoHotel);

    // Demo Content-Chunks anlegen
    var chunks = new[]
    {
        new HotelChatbot.Domain.Entities.ContentChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            HotelId = "hotel_demo",
            SourceUrl = "https://demo-hotel.com/zimmer",
            Title = "Unsere Zimmer",
            Content = "Wir bieten komfortable Doppelzimmer, luxuriöse Suiten und familienfreundliche Apartments. Alle Zimmer verfügen über kostenloses WLAN, Klimaanlage und einen Flachbildfernseher. Check-in ab 15:00 Uhr, Check-out bis 11:00 Uhr.",
            Language = "de"
        },
        new HotelChatbot.Domain.Entities.ContentChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            HotelId = "hotel_demo",
            SourceUrl = "https://demo-hotel.com/restaurant",
            Title = "Restaurant & Bar",
            Content = "Unser hauseigenes Restaurant serviert internationale und regionale Küche. Frühstück von 7:00-10:30 Uhr, Abendessen von 18:00-22:00 Uhr. Die Hotelbar ist täglich bis 23:00 Uhr geöffnet.",
            Language = "de"
        },
        new HotelChatbot.Domain.Entities.ContentChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            HotelId = "hotel_demo",
            SourceUrl = "https://demo-hotel.com/wellness",
            Title = "Wellness & Spa",
            Content = "Entspannen Sie in unserem Wellnessbereich mit Sauna, Dampfbad und Innenpool. Massagen und Beauty-Behandlungen nach Vereinbarung. Geöffnet täglich von 9:00-21:00 Uhr.",
            Language = "de"
        },
        new HotelChatbot.Domain.Entities.ContentChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            HotelId = "hotel_demo",
            SourceUrl = "https://demo-hotel.com/kontakt",
            Title = "Kontakt & Anreise",
            Content = "Demo Hotel & Spa, Musterstraße 123, 12345 Musterstadt. Telefon: +49 123 456789, E-Mail: info@demo-hotel.com. Kostenlose Parkplätze verfügbar. 10 Minuten vom Hauptbahnhof entfernt.",
            Language = "de"
        }
    };

    await vectorStore.AddChunksAsync(chunks);
}
