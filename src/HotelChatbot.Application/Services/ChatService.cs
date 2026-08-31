using HotelChatbot.Application.DTOs;
using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Enums;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Service für Chat-Verarbeitung mit RAG.
/// Pipeline: Language → Ethical → Intent (lokal) → Search → Answer (LLM).
/// </summary>
public class ChatService
{
    private readonly IVectorStore _vectorStore;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly IHotelRepository _hotelRepository;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly SystemPromptService _systemPromptService;
    private readonly ILogger<ChatService> _logger;
    private readonly IQueryLogger _queryLogger;
    private readonly ILanguageDetector _languageDetector;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IEthicalClassifier _ethicalClassifier;

    /// <summary>
    /// Ab diesem Top-Similarity-Score wird der RelevanceAgent-LLM übersprungen.
    /// </summary>
    private const double RelevanceScoreSkipThreshold = 0.55;

    public ChatService(
        IVectorStore vectorStore,
        IChatCompletionService chatCompletionService,
        IHotelRepository hotelRepository,
        IChatSessionRepository sessionRepository,
        SystemPromptService systemPromptService,
        ILogger<ChatService> logger,
        IQueryLogger queryLogger,
        ILanguageDetector languageDetector,
        IIntentClassifier intentClassifier,
        IEthicalClassifier ethicalClassifier)
    {
        _vectorStore = vectorStore;
        _chatCompletionService = chatCompletionService;
        _hotelRepository = hotelRepository;
        _sessionRepository = sessionRepository;
        _systemPromptService = systemPromptService;
        _logger = logger;
        _queryLogger = queryLogger;
        _languageDetector = languageDetector;
        _intentClassifier = intentClassifier;
        _ethicalClassifier = ethicalClassifier;
    }



    /// <summary>
    /// Hotel-Empfehlung: Language → Ethical → Intent → Search → Answer.
    /// </summary>
    public async Task<HotelRecommendationResponseDto> ProcessHotelRecommendationAsync(
        HotelRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Nur initial prüfen ob der Request bereits abgebrochen wurde
        cancellationToken.ThrowIfCancellationRequested();

        // Pipeline bekommt ein eigenes unabhängiges Token mit 120s Timeout.
        // Kein Link zum Request-Token — verhindert, dass interne Azure SDK
        // Netzwerkfehler das Token vorzeitig canceln und alle folgenden
        // DB/OpenAI-Calls sofort fehlschlagen lassen.
        using var pipelineCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = pipelineCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Pipeline gestartet: {Requirements}", request.Requirements);

            // ─── Step 0: Session & Gesprächsverlauf ────────────────────────────────
            var session = await GetOrCreatePipelineSessionAsync(request.SessionId, ct);
            var conversationHistory = BuildConversationHistory(session);
            var previousHotelIds = LoadRecommendedHotelIds(session);

            var gates = await RunSafetyGatesAsync(
                request.Requirements,
                request.Language,
                logIntent: true,
                ct);

            if (gates.RejectReason == "ethical_reject")
            {
                await SaveInteractionAsync(session, request.Requirements, gates.RejectMessage!, gates.Language, null, ct);
                return BuildPipelineResponse(false, gates.RejectMessage!, "ethical_reject", request.Requirements, session.SessionId);
            }

            if (gates.RejectReason == "out_of_scope")
            {
                await SaveInteractionAsync(session, request.Requirements, gates.RejectMessage!, gates.Language, null, ct);
                return BuildPipelineResponse(false, gates.RejectMessage!, "out_of_scope", request.Requirements, session.SessionId);
            }

            var language = gates.Language;

            // ─── Search: Query für Vektorsuche ─────────────────────────────────────
            // Bei Deutsch Original nutzen, sonst LLM-Rewrite.
            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = request.Requirements;
                _logger.LogInformation("[Search] Query bereits Deutsch – kein Translate-LLM");
            }
            else
            {
                var translatePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.translate_to_german", ct);
                queryForSearch = await _chatCompletionService.TranslateToGermanAsync(request.Requirements, translatePrompt, ct);
                _logger.LogInformation("[Search] Query optimiert: Original='{Original}' → Suche='{Optimized}'",
                    request.Requirements, queryForSearch);
            }

            // 4b. Vektordatenbank durchsuchen
            var maxResults = await GetMaxResultsAsync(ct);
            _logger.LogInformation("[SearchAgent] Starte VectorStore-Suche nach {Elapsed}ms (ct.IsCancellationRequested={Ct})",
                sw.ElapsedMilliseconds, ct.IsCancellationRequested);
            var allResults = await _vectorStore.SearchAllHotelsAsync(
                queryForSearch,
                maxResults * 2,  // Mehr Chunks holen für bessere Kontext-Abdeckung
                request.MinConfidence,
                ct);

            // 4c. Gezielte Detail-Suche für Hotels aus vorherigem Gesprächsverlauf
            if (previousHotelIds.Count > 0)
            {
                foreach (var prevHotelId in previousHotelIds.Take(3))
                {
                    var detailChunks = await _vectorStore.SearchAsync(
                        prevHotelId, queryForSearch, maxResults, request.MinConfidence, ct);
                    if (detailChunks.Count > 0)
                    {
                        if (allResults.ContainsKey(prevHotelId))
                        {
                            var existingIds = allResults[prevHotelId].Select(r => r.Chunk.ChunkId).ToHashSet();
                            allResults[prevHotelId].AddRange(detailChunks.Where(r => !existingIds.Contains(r.Chunk.ChunkId)));
                        }
                        else
                        {
                            allResults[prevHotelId] = detailChunks;
                        }
                    }
                }
            }

            // Hotel-Details für gefundene Hotels laden (Batch, kein N+1)
            var hotelDetails = new Dictionary<string, Hotel>();
            var hotels = await _hotelRepository.GetByIdsAsync(allResults.Keys, ct);
            foreach (var hotel in hotels)
            {
                if (hotel.IsActive)
                    hotelDetails[hotel.HotelId] = hotel;
            }

            // Nur Ergebnisse von aktiven, bekannten Hotels behalten
            var validResults = allResults
                .Where(kvp => hotelDetails.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            _logger.LogInformation("[SearchAgent] {HotelCount} Hotels, {ChunkCount} Chunks",
                validResults.Count, validResults.Values.Sum(v => v.Count));

            // ─── Step 5: AnswerAgent ──────────────────────────────────────────────
            if (validResults.Count == 0)
            {
                // Kein Ergebnis → freundliche Meldung in der Sprache des Users
                var noResultsPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.no_results", ct);
                noResultsPrompt = (noResultsPrompt ?? FallbackNoResultsPrompt).Replace("{language}", LanguageName(language));

                var noResultsAnswer = await _chatCompletionService.GenerateResponseAsync(
                    noResultsPrompt,
                    string.Empty,
                    request.Requirements,
                    conversationHistory,
                    language,
                    "User query: {userQuery}",
                    ct);

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer, language, null, ct);
                return BuildPipelineResponse(false, noResultsAnswer, "no_results", request.Requirements, session.SessionId);
            }

            // Strukturierte Empfehlungen (nach Score gerankt, auf MaxResults begrenzt)
            var recommendations = BuildRecommendations(validResults, hotelDetails, maxResults);
            var rankedResults = recommendations
                .ToDictionary(
                    r => r.HotelId,
                    r => validResults[r.HotelId]);

            // Kontext aus Top-Chunks aufbauen
            var context = BuildRecommendationContext(rankedResults, hotelDetails, maxResults * 3);

            // ─── Step 4c: RelevanceAgent ──────────────────────────────────────────
            // Bei klar hohen Similarity-Scores LLM überspringen (Performance).
            var topScore = rankedResults.Values
                .SelectMany(list => list)
                .Select(r => r.Score)
                .DefaultIfEmpty(0)
                .Max();

            bool isContextRelevant;
            if (topScore >= RelevanceScoreSkipThreshold)
            {
                isContextRelevant = true;
                _logger.LogInformation(
                    "[RelevanceAgent] Übersprungen (TopScore={TopScore:0.00} >= {Threshold})",
                    topScore, RelevanceScoreSkipThreshold);
            }
            else
            {
                var relevancePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.relevance_check", ct);
                isContextRelevant = await _chatCompletionService.IsContextRelevantAsync(
                    request.Requirements, context, relevancePrompt, ct);
                _logger.LogInformation("[RelevanceAgent] Ergebnis: {Result} (TopScore={TopScore:0.00})",
                    isContextRelevant ? "YES" : "NO", topScore);
            }

            if (!isContextRelevant)
            {
                var noResultsPrompt2 = await _systemPromptService.GetContentOrNullAsync("pipeline.no_results", ct);
                noResultsPrompt2 = (noResultsPrompt2 ?? FallbackNoResultsPrompt).Replace("{language}", LanguageName(language));

                var noResultsAnswer2 = await _chatCompletionService.GenerateResponseAsync(
                    noResultsPrompt2,
                    string.Empty,
                    request.Requirements,
                    conversationHistory,
                    language,
                    "User query: {userQuery}",
                    ct);

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer2, language, null, ct);
                return BuildPipelineResponse(false, noResultsAnswer2, "no_results", request.Requirements, session.SessionId);
            }

            var answerPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.answer", ct);
            answerPrompt = (answerPrompt ?? FallbackAnswerPrompt).Replace("{language}", LanguageName(language));

            var answer = await _chatCompletionService.GenerateResponseAsync(
                answerPrompt,
                context,
                request.Requirements,
                conversationHistory,
                language,
                PipelineContextWrapper,
                ct);

            // Quellenangaben deterministisch anhängen
            answer += BuildSourcesSection(rankedResults, hotelDetails, language);

            // Interaktion + empfohlene Hotel-IDs in Session speichern
            var recommendedIds = recommendations.Select(r => r.HotelId).ToList();
            await SaveInteractionAsync(session, request.Requirements, answer, language, recommendedIds, ct);

            return new HotelRecommendationResponseDto
            {
                Success = true,
                FinalAnswer = answer,
                ResponseType = "recommendations",
                Requirements = request.Requirements,
                Recommendations = recommendations,
                SessionId = session.SessionId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Pipeline-Fehler nach {Elapsed}ms: Requirements={Requirements} | " +
                "pipelineCt.IsCancellationRequested={PipelineCt} | " +
                "requestCt.IsCancellationRequested={RequestCt} | " +
                "pipelineTimeout={TimeoutMs}ms",
                sw.ElapsedMilliseconds, request.Requirements,
                ct.IsCancellationRequested, cancellationToken.IsCancellationRequested,
                (int)TimeSpan.FromSeconds(120).TotalMilliseconds);
            return BuildPipelineResponse(false,
                "An error occurred while processing your request. Please try again.",
                "error", request.Requirements, null);
        }
    }

    // ─── Pipeline-Hilfsmethoden ───────────────────────────────────────────────

    /// <summary>
    /// Liest die konfigurierte Max-Ergebnisanzahl aus dem Admin-CMS (config.search.max_results).
    /// </summary>
    private async Task<int> GetMaxResultsAsync(CancellationToken cancellationToken)
    {
        var configValue = await _systemPromptService.GetContentOrNullAsync("config.search.max_results", cancellationToken);
        if (int.TryParse(configValue?.Trim(), out var n) && n > 0 && n <= 20)
            return n;
        return 3; // Default
    }

    private sealed record SafetyGateResult(string Language, string? RejectReason, string? RejectMessage);

    /// <summary>
    /// Gemeinsame Language-/Ethical-/Intent-Gates für Recommend und Hotel-Details.
    /// </summary>
    private async Task<SafetyGateResult> RunSafetyGatesAsync(
        string text,
        string? languageOverride,
        bool logIntent,
        CancellationToken ct)
    {
        string language;
        if (!string.IsNullOrWhiteSpace(languageOverride))
        {
            language = NormalizeLanguageCode(languageOverride);
            _logger.LogInformation("[Language] vorgegeben: {Language}", language);
        }
        else
        {
            language = NormalizeLanguageCode(_languageDetector.Detect(text));
            _logger.LogInformation("[Language] erkannt: {Language}", language);
        }

        if (!_ethicalClassifier.IsEthical(text))
        {
            _logger.LogInformation("[Ethical] REJECT");
            var msg = language == "en"
                ? "Your message could not be processed. Please rephrase your question in a polite and respectful manner."
                : "Ihre Anfrage konnte nicht verarbeitet werden. Bitte formulieren Sie Ihre Frage höflich und respektvoll.";
            return new SafetyGateResult(language, "ethical_reject", msg);
        }

        _logger.LogInformation("[Ethical] OK");

        var inScope = _intentClassifier.IsHotelWellnessQuery(text);
        if (logIntent)
        {
            await _queryLogger.LogIntentCheckAsync(
                query: text,
                language: language,
                promptKey: "classifier.intent",
                promptContent: "local BinaryTextClassifier (in_scope/out_of_scope)",
                isHotelQuery: inScope);
        }

        if (!inScope)
        {
            _logger.LogInformation("[Intent] out_of_scope");
            var msg = language == "en"
                ? "This assistant exclusively provides information about BestWellness wellness hotels. Your request is outside the scope of this service."
                : "Dieser Assistent beantwortet ausschließlich Fragen zu BestWellness-Wellnesshotels. Ihre Anfrage liegt außerhalb des Themenbereichs.";
            return new SafetyGateResult(language, "out_of_scope", msg);
        }

        _logger.LogInformation("[Intent] in_scope");
        return new SafetyGateResult(language, null, null);
    }

    /// <summary>
    /// Normalisiert den erkannten Sprachcode auf einen gültigen ISO-639-1 Code.
    /// Fallback: "de"
    /// </summary>
    private static string NormalizeLanguageCode(string raw)
    {
        var code = raw.Trim().ToLower();
        if (code.Length == 2 && code.All(char.IsLetter))
            return code;
        // z.B. "de-AT"
        if (code.Length >= 2)
            return code[..2];
        return "de";
    }

    /// <summary>
    /// Gibt den ausgeschriebenen Sprachnamen zurück (für LLM-Prompts).
    /// </summary>
    private static string LanguageName(string code) => code switch
    {
        "de" => "German",
        "en" => "English",
        "fr" => "French",
        "it" => "Italian",
        "es" => "Spanish",
        "nl" => "Dutch",
        "pl" => "Polish",
        "cs" => "Czech",
        "hu" => "Hungarian",
        "hr" => "Croatian",
        "sk" => "Slovak",
        "sl" => "Slovenian",
        _    => code
    };

    /// <summary>
    /// Verarbeitet eine Detailfrage zu genau einem Hotel (Read-only, kurzer Pfad).
    /// </summary>
    public async Task<ChatResponseDto> ProcessHotelDetailsAsync(
        ChatRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var ct = linked.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(request.HotelId))
            {
                return new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = "A valid hotelId is required.",
                    FinalAnswer = "A valid hotelId is required.",
                    ResponseType = "error",
                    Success = false,
                    ErrorMessage = "hotelId is required"
                };
            }

            var hotel = await _hotelRepository.GetByIdAsync(request.HotelId.Trim(), ct);
            if (hotel == null || !hotel.IsActive)
            {
                var unknown = "No hotel was found for the given hotelId. Please use a hotelId from a previous search result.";
                return new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = unknown,
                    FinalAnswer = unknown,
                    ResponseType = "error",
                    Success = false,
                    ErrorMessage = "hotel_not_found"
                };
            }

            var gates = await RunSafetyGatesAsync(
                request.Message,
                request.Language,
                logIntent: false,
                ct);

            if (gates.RejectReason is not null)
            {
                return new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = gates.RejectMessage!,
                    FinalAnswer = gates.RejectMessage!,
                    ResponseType = gates.RejectReason,
                    Success = false,
                    Language = gates.Language,
                    ErrorMessage = gates.RejectReason
                };
            }

            var language = gates.Language;

            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = request.Message;
            }
            else
            {
                var translatePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.translate_to_german", ct);
                queryForSearch = await _chatCompletionService.TranslateToGermanAsync(request.Message, translatePrompt, ct);
            }

            var chunks = await _vectorStore.SearchAsync(
                hotel.HotelId, queryForSearch, 8, 0.40, ct);

            var publicHotel = HotelPublicDto.FromHotel(hotel);
            var sources = chunks
                .Select(c => c.Chunk.SourceUrl)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (chunks.Count == 0)
            {
                var noData = language == "de"
                    ? $"Für „{hotel.Name}“ liegen zu dieser Frage keine freigegebenen Informationen vor."
                    : $"No approved information is available for “{hotel.Name}” for this question.";
                return new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = noData,
                    FinalAnswer = noData,
                    ResponseType = "no_data",
                    Success = true,
                    Language = language,
                    Hotel = publicHotel,
                    Sources = sources
                };
            }

            var contextParts = chunks.Select((item, index) =>
                $"[Result {index + 1}] HotelId: {hotel.HotelId} | Hotel: {hotel.Name}\n" +
                (!string.IsNullOrWhiteSpace(item.Chunk.Title) ? $"Section: {item.Chunk.Title}\n" : "") +
                $"{item.Chunk.Content}\n" +
                $"Source URL: {item.Chunk.SourceUrl}");
            var context = string.Join("\n\n---\n\n", contextParts);

            var answerPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.answer", ct);
            answerPrompt = (answerPrompt ?? FallbackAnswerPrompt).Replace("{language}", LanguageName(language));

            var answer = await _chatCompletionService.GenerateResponseAsync(
                answerPrompt,
                context,
                request.Message,
                new List<(string Role, string Content)>(),
                language,
                PipelineContextWrapper,
                ct);

            if (sources.Count > 0)
            {
                var header = language == "de" ? "\n\n---\n**Quellen:**" : "\n\n---\n**Sources:**";
                answer += header + "\n" + string.Join("\n", sources.Select(u => $"- {hotel.Name}: {u}"));
            }

            return new ChatResponseDto
            {
                SessionId = request.SessionId ?? string.Empty,
                Message = answer,
                FinalAnswer = answer,
                ResponseType = "final_answer",
                Success = true,
                Language = language,
                Hotel = publicHotel,
                Sources = sources,
                ConfidenceScore = chunks.Max(c => c.Score)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotel-Details fehlgeschlagen für {HotelId}", request.HotelId);
            return new ChatResponseDto
            {
                SessionId = request.SessionId ?? string.Empty,
                Message = "An error occurred while loading hotel details. Please try again.",
                FinalAnswer = "An error occurred while loading hotel details. Please try again.",
                ResponseType = "error",
                Success = false,
                ErrorMessage = "internal_error"
            };
        }
    }

    /// <summary>
    /// Baut den Kontext für den AnswerAgent aus den Vektorsuchergebnissen.
    /// </summary>
    private static string BuildRecommendationContext(
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> results,
        Dictionary<string, Hotel> hotelDetails,
        int maxChunks)
    {
        var topChunks = results
            .SelectMany(kvp => kvp.Value.Select(r => (HotelId: kvp.Key, r.Chunk, r.Score)))
            .OrderByDescending(x => x.Score)
            .Take(maxChunks)
            .ToList();

        var parts = topChunks.Select((item, index) =>
        {
            var hotelName = hotelDetails.TryGetValue(item.HotelId, out var hotel) ? hotel.Name : item.HotelId;
            return $"[Result {index + 1}] HotelId: {item.HotelId} | Hotel: {hotelName}\n" +
                   (!string.IsNullOrWhiteSpace(item.Chunk.Title) ? $"Section: {item.Chunk.Title}\n" : "") +
                   $"{item.Chunk.Content}\n" +
                   $"Source URL: {item.Chunk.SourceUrl}";
        });

        return string.Join("\n\n---\n\n", parts);
    }

    private static List<HotelRecommendationDto> BuildRecommendations(
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> results,
        Dictionary<string, Hotel> hotelDetails,
        int maxResults)
    {
        return results
            .Select(kvp =>
            {
                var hotel = hotelDetails[kvp.Key];
                var best = kvp.Value.OrderByDescending(x => x.Score).First();
                var pub = HotelPublicDto.FromHotel(hotel);
                var sources = kvp.Value
                    .Select(x => x.Chunk.SourceUrl)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();
                var features = kvp.Value
                    .Select(x => x.Chunk.Title)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .Take(5)
                    .Cast<string>()
                    .ToList();

                return new HotelRecommendationDto
                {
                    HotelId = hotel.HotelId,
                    HotelName = hotel.Name,
                    Domain = hotel.Domain,
                    Location = pub.Location,
                    Region = pub.Region,
                    Country = pub.Country,
                    OfficialUrl = pub.OfficialUrl,
                    SourceUrl = pub.SourceUrl,
                    EditorialReviewStatus = pub.EditorialReviewStatus,
                    EditorialReviewedAt = pub.EditorialReviewedAt,
                    Categories = pub.Categories,
                    MatchScore = best.Score,
                    Reason = !string.IsNullOrWhiteSpace(best.Chunk.Title)
                        ? best.Chunk.Title!
                        : "Matched from approved hotel content",
                    MatchingFeatures = features,
                    Sources = sources,
                    Rank = 0
                };
            })
            .OrderByDescending(r => r.MatchScore)
            .Take(maxResults)
            .Select((r, index) =>
            {
                r.Rank = index + 1;
                return r;
            })
            .ToList();
    }

    /// <summary>
    /// Hängt Quellenangaben deterministisch ans Ende der Antwort.
    /// </summary>
    private static string BuildSourcesSection(
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> results,
        Dictionary<string, Hotel> hotelDetails,
        string language)
    {
        var hotelSources = results
            .Where(kvp => hotelDetails.ContainsKey(kvp.Key))
            .Select(kvp => new
            {
                HotelName = hotelDetails[kvp.Key].Name,
                Urls = kvp.Value.Select(r => r.Chunk.SourceUrl)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList()
            })
            .Where(h => h.Urls.Count > 0)
            .ToList();

        if (hotelSources.Count == 0) return string.Empty;

        var header = language == "de" ? "\n\n---\n**Quellen:**" : "\n\n---\n**Sources:**";
        var links = hotelSources.SelectMany(h => h.Urls.Select(url => $"- {h.HotelName}: {url}"));
        return header + "\n" + string.Join("\n", links);
    }

    private static HotelRecommendationResponseDto BuildPipelineResponse(
        bool success, string finalAnswer, string responseType, string requirements, string? sessionId = null) =>
        new()
        {
            Success = success,
            FinalAnswer = finalAnswer,
            ResponseType = responseType,
            Requirements = requirements,
            SessionId = sessionId ?? string.Empty,
            Timestamp = DateTime.UtcNow
        };

    // ─── Session-Hilfsmethoden ────────────────────────────────────────────────
    private async Task<ChatSession> GetOrCreatePipelineSessionAsync(string? sessionId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var existing = await _sessionRepository.GetByIdAsync(sessionId, ct);
            if (existing != null)
            {
                existing.LastActivityAt = DateTime.UtcNow;
                await _sessionRepository.UpdateAsync(existing, ct);
                return existing;
            }
        }

        var newSession = new ChatSession
        {
            SessionId = $"rec_{Guid.NewGuid():N}",
            HotelId = "__pipeline__",
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IsActive = true
        };
        await _sessionRepository.AddAsync(newSession, ct);
        return newSession;
    }

    private static List<(string Role, string Content)> BuildConversationHistory(ChatSession session)
    {
        return session.Messages
            .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
            .OrderByDescending(m => m.Timestamp)
            .Take(10)
            .OrderBy(m => m.Timestamp)
            .Select(m => (m.Role, m.Content))
            .ToList();
    }

    private static List<string> LoadRecommendedHotelIds(ChatSession session)
    {
        const string prefix = "__recommended_hotels__:";
        var contextMsg = session.Messages
            .Where(m => m.Role == MessageRole.System && m.Content.StartsWith(prefix))
            .MaxBy(m => m.Timestamp);
        if (contextMsg is null) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(contextMsg.Content[prefix.Length..]) ?? [];
        }
        catch { return []; }
    }

    private async Task SaveInteractionAsync(
        ChatSession session,
        string userMessage,
        string assistantMessage,
        string language,
        List<string>? recommendedHotelIds,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        session.Messages.Add(new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            Role = MessageRole.User,
            Content = userMessage,
            Language = language,
            Timestamp = now
        });
        session.Messages.Add(new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            Role = MessageRole.Assistant,
            Content = assistantMessage,
            Language = language,
            Timestamp = now.AddMilliseconds(1)
        });
        if (recommendedHotelIds?.Count > 0)
        {
            session.Messages.Add(new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SessionId = session.SessionId,
                Role = MessageRole.System,
                Content = $"__recommended_hotels__:{JsonSerializer.Serialize(recommendedHotelIds)}",
                Timestamp = now.AddMilliseconds(2)
            });
        }
        session.LastActivityAt = now;
        await _sessionRepository.UpdateAsync(session, ct);
    }

    // ─── Fallback-Prompts ───────────────────────────────────────────────────────
    // Verwendet wenn die DB keinen aktiven Prompt für den jeweiligen Key liefert.
    private const string PipelineContextWrapper =
        "=== DATABASE RESULTS (SOURCE FOR HOTEL RECOMMENDATIONS) ===\n{context}\n=== END DATABASE RESULTS ===\n\nUser Query: {userQuery}\n\n" +
        "RULE 1 – Hotel recommendations (STRICT): You must NEVER recommend or mention hotels that are NOT in the DATABASE RESULTS above.\n" +
        "RULE 2 – General knowledge (ALLOWED): You MAY use general world knowledge to answer factual questions ABOUT the hotels in the results " +
        "(e.g. distances, nearby airports, restaurants, travel time, regional geography). Mark approximations clearly.";

    private const string FallbackAnswerPrompt =
        "You are a friendly hotel search assistant for BestWellness wellness hotels.\n" +
        "Compose a helpful, friendly response in {language} using the provided database results.\n" +
        "STRICT rule – hotel recommendations: You must NEVER recommend or mention hotels that are NOT in the provided database results.\n" +
        "STRICT rule – factual accuracy: State only facts, numbers, sizes, and offers from the database results. Do not invent, round, or merge conflicting values.\n" +
        "When the user asks about seasonal offers, list ONLY offers matching that season in the results.\n" +
        "ALLOWED – general knowledge: You MAY use general world knowledge for factual questions ABOUT hotels in the results (distances, geography). Mark approximations clearly.\n" +
        "Be concise, helpful, and professional.";

    private const string FallbackNoResultsPrompt =
        "Compose a brief, friendly message in {language} informing the user that no matching hotels were found in the BestWellness database for their request.\n" +
        "Do NOT suggest alternative hotels. Do NOT use your training knowledge to recommend hotels.\n" +
        "Output ONLY the message to the user. No other text.";
}
