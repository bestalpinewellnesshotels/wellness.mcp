using HotelChatbot.Application.Diagnostics;
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
        CancellationToken cancellationToken = default,
        PipelineTraceCollector? trace = null)
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
        trace ??= new PipelineTraceCollector();

        try
        {
            _logger.LogInformation("Pipeline gestartet: {Requirements}", request.Requirements);

            // ─── Step 0: Session & Gesprächsverlauf ────────────────────────────────
            ChatSession session = await trace.MeasureAsync(
                "Session",
                "db",
                "Session laden/anlegen",
                async () =>
                {
                    var s = await GetOrCreatePipelineSessionAsync(request.SessionId, ct);
                    return (s, "ok", $"sessionId={s.SessionId}", (Dictionary<string, object?>?)null);
                });
            var conversationHistory = BuildConversationHistory(session);
            var previousHotelIds = LoadRecommendedHotelIds(session);

            var gates = await RunSafetyGatesAsync(
                request.Requirements,
                request.Language,
                logIntent: true,
                ct,
                trace);

            if (gates.RejectReason == "ethical_reject")
            {
                await SaveInteractionAsync(session, request.Requirements, gates.RejectMessage!, gates.Language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, gates.RejectMessage!, "ethical_reject", request.Requirements, session.SessionId), trace);
            }

            if (gates.RejectReason == "out_of_scope")
            {
                await SaveInteractionAsync(session, request.Requirements, gates.RejectMessage!, gates.Language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, gates.RejectMessage!, "out_of_scope", request.Requirements, session.SessionId), trace);
            }

            var language = gates.Language;

            // ─── Search: Query für Vektorsuche ─────────────────────────────────────
            // Bei Deutsch Original nutzen, sonst LLM-Rewrite.
            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = request.Requirements;
                _logger.LogInformation("[Search] Query bereits Deutsch – kein Translate-LLM");
                await trace.EmitStartAsync("Translate", "llm", "Skip (Query bereits Deutsch)");
                await trace.EmitEndAsync("Translate", "llm", "übersprungen", 0, "skip",
                    new Dictionary<string, object?> { ["reason"] = "language=de" });
            }
            else
            {
                queryForSearch = await trace.MeasureAsync(
                    "Translate",
                    "llm",
                    "Query → Deutsch (LLM)",
                    async () =>
                    {
                        var translatePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.translate_to_german", ct);
                        var q = await _chatCompletionService.TranslateToGermanAsync(request.Requirements, translatePrompt, ct);
                        _logger.LogInformation("[Search] Query optimiert: Original='{Original}' → Suche='{Optimized}'",
                            request.Requirements, q);
                        return (q, "ok", $"→ {Truncate(q, 80)}", new Dictionary<string, object?> { ["query"] = q });
                    });
            }

            // 4b. Vektordatenbank durchsuchen
            var maxResults = await GetMaxResultsAsync(ct);
            _logger.LogInformation("[SearchAgent] Starte VectorStore-Suche nach {Elapsed}ms (ct.IsCancellationRequested={Ct})",
                sw.ElapsedMilliseconds, ct.IsCancellationRequested);

            var allResults = await trace.MeasureAsync(
                "VectorSearch",
                "search",
                $"SearchAllHotels (topK={maxResults * 2}, minScore={request.MinConfidence})",
                async () =>
                {
                    var results = await _vectorStore.SearchAllHotelsAsync(
                        queryForSearch,
                        maxResults * 2,
                        request.MinConfidence,
                        ct);
                    var chunks = results.Values.Sum(v => v.Count);
                    return (results, "ok", $"{results.Count} Hotels, {chunks} Chunks",
                        new Dictionary<string, object?> { ["hotels"] = results.Count, ["chunks"] = chunks });
                });

            // 4c. Gezielte Detail-Suche für Hotels aus vorherigem Gesprächsverlauf
            if (previousHotelIds.Count > 0)
            {
                await trace.MeasureAsync(
                    "FollowUpSearch",
                    "search",
                    $"Detail-Suche für {Math.Min(3, previousHotelIds.Count)} Session-Hotels",
                    async () =>
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
                        return (true, "ok", "Follow-up-Suche fertig", (Dictionary<string, object?>?)null);
                    });
            }

            // Hotel-Details für gefundene Hotels laden (Batch, kein N+1)
            var hotelDetails = await trace.MeasureAsync(
                "HotelLookup",
                "db",
                $"Hotels laden ({allResults.Count} IDs)",
                async () =>
                {
                    var map = new Dictionary<string, Hotel>();
                    var hotels = await _hotelRepository.GetByIdsAsync(allResults.Keys, ct);
                    foreach (var hotel in hotels)
                    {
                        if (hotel.IsActive)
                            map[hotel.HotelId] = hotel;
                    }
                    return (map, "ok", $"{map.Count} aktive Hotels",
                        new Dictionary<string, object?> { ["activeHotels"] = map.Count });
                });

            // Nur Ergebnisse von aktiven, bekannten Hotels behalten
            var validResults = allResults
                .Where(kvp => hotelDetails.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            _logger.LogInformation("[SearchAgent] {HotelCount} Hotels, {ChunkCount} Chunks",
                validResults.Count, validResults.Values.Sum(v => v.Count));

            // ─── Step 5: AnswerAgent ──────────────────────────────────────────────
            if (validResults.Count == 0)
            {
                var noResultsAnswer = await trace.MeasureAsync(
                    "Answer",
                    "llm",
                    "No-Results Antwort (LLM)",
                    async () =>
                    {
                        var noResultsPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.no_results", ct);
                        noResultsPrompt = (noResultsPrompt ?? FallbackNoResultsPrompt).Replace("{language}", LanguageName(language));
                        var text = await _chatCompletionService.GenerateResponseAsync(
                            noResultsPrompt,
                            string.Empty,
                            request.Requirements,
                            conversationHistory,
                            language,
                            "User query: {userQuery}",
                            ct);
                        return (text, "ok", $"Antwort {text.Length} Zeichen", (Dictionary<string, object?>?)null);
                    });

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer, language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, noResultsAnswer, "no_results", request.Requirements, session.SessionId), trace);
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
                await trace.EmitStartAsync("Relevance", "llm", $"Skip (TopScore={topScore:0.00} ≥ {RelevanceScoreSkipThreshold})");
                await trace.EmitEndAsync("Relevance", "llm", "übersprungen → relevant", 0, "skip",
                    new Dictionary<string, object?> { ["topScore"] = topScore, ["threshold"] = RelevanceScoreSkipThreshold });
            }
            else
            {
                isContextRelevant = await trace.MeasureAsync(
                    "Relevance",
                    "llm",
                    $"Relevance-Check LLM (TopScore={topScore:0.00})",
                    async () =>
                    {
                        var relevancePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.relevance_check", ct);
                        var relevant = await _chatCompletionService.IsContextRelevantAsync(
                            request.Requirements, context, relevancePrompt, ct);
                        _logger.LogInformation("[RelevanceAgent] Ergebnis: {Result} (TopScore={TopScore:0.00})",
                            relevant ? "YES" : "NO", topScore);
                        return (relevant, relevant ? "ok" : "reject", relevant ? "YES" : "NO",
                            new Dictionary<string, object?> { ["topScore"] = topScore, ["relevant"] = relevant });
                    });
            }

            if (!isContextRelevant)
            {
                var noResultsAnswer2 = await trace.MeasureAsync(
                    "Answer",
                    "llm",
                    "No-Results nach Relevance=NO (LLM)",
                    async () =>
                    {
                        var noResultsPrompt2 = await _systemPromptService.GetContentOrNullAsync("pipeline.no_results", ct);
                        noResultsPrompt2 = (noResultsPrompt2 ?? FallbackNoResultsPrompt).Replace("{language}", LanguageName(language));
                        var text = await _chatCompletionService.GenerateResponseAsync(
                            noResultsPrompt2,
                            string.Empty,
                            request.Requirements,
                            conversationHistory,
                            language,
                            "User query: {userQuery}",
                            ct);
                        return (text, "ok", $"Antwort {text.Length} Zeichen", (Dictionary<string, object?>?)null);
                    });

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer2, language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, noResultsAnswer2, "no_results", request.Requirements, session.SessionId), trace);
            }

            var answer = await trace.MeasureAsync(
                "Answer",
                "llm",
                "Answer-Agent (LLM)",
                async () =>
                {
                    var answerPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.answer", ct);
                    answerPrompt = (answerPrompt ?? FallbackAnswerPrompt).Replace("{language}", LanguageName(language));
                    var text = await _chatCompletionService.GenerateResponseAsync(
                        answerPrompt,
                        context,
                        request.Requirements,
                        conversationHistory,
                        language,
                        PipelineContextWrapper,
                        ct);
                    text += BuildSourcesSection(rankedResults, hotelDetails, language);
                    return (text, "ok", $"Antwort {text.Length} Zeichen, {recommendations.Count} Hotels",
                        new Dictionary<string, object?> { ["hotels"] = recommendations.Count, ["chars"] = text.Length });
                });

            // Interaktion + empfohlene Hotel-IDs in Session speichern
            var recommendedIds = recommendations.Select(r => r.HotelId).ToList();
            await SaveInteractionAsync(session, request.Requirements, answer, language, recommendedIds, ct);

            return AttachTrace(new HotelRecommendationResponseDto
            {
                Success = true,
                FinalAnswer = answer,
                ResponseType = "recommendations",
                Requirements = request.Requirements,
                Recommendations = recommendations,
                SessionId = session.SessionId,
                Timestamp = DateTime.UtcNow
            }, trace);
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
            return AttachTrace(BuildPipelineResponse(false,
                "An error occurred while processing your request. Please try again.",
                "error", request.Requirements, null), trace);
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
        CancellationToken ct,
        PipelineTraceCollector? trace = null)
    {
        string language;
        if (!string.IsNullOrWhiteSpace(languageOverride))
        {
            language = NormalizeLanguageCode(languageOverride);
            _logger.LogInformation("[Language] vorgegeben: {Language}", language);
            if (trace != null)
            {
                await trace.EmitStartAsync("Language", "classifier", $"vorgegeben: {language}");
                await trace.EmitEndAsync("Language", "classifier", language, 0, "ok",
                    new Dictionary<string, object?> { ["language"] = language, ["source"] = "override" });
            }
        }
        else
        {
            language = await (trace?.MeasureAsync(
                "Language",
                "classifier",
                "Spracherkennung (lokal)",
                async () =>
                {
                    var code = NormalizeLanguageCode(_languageDetector.Detect(text));
                    _logger.LogInformation("[Language] erkannt: {Language}", code);
                    return (code, "ok", code, new Dictionary<string, object?> { ["language"] = code });
                }) ?? Task.FromResult(NormalizeLanguageCode(_languageDetector.Detect(text))));
            if (trace == null)
                _logger.LogInformation("[Language] erkannt: {Language}", language);
        }

        var ethicalOk = await (trace?.MeasureAsync(
            "Ethical",
            "classifier",
            "Ethical-Classifier (lokal)",
            async () =>
            {
                var ok = _ethicalClassifier.IsEthical(text);
                return (ok, ok ? "ok" : "reject", ok ? "OK" : "REJECT", (Dictionary<string, object?>?)null);
            }) ?? Task.FromResult(_ethicalClassifier.IsEthical(text)));

        if (!ethicalOk)
        {
            _logger.LogInformation("[Ethical] REJECT");
            var msg = language == "en"
                ? "Your message could not be processed. Please rephrase your question in a polite and respectful manner."
                : "Ihre Anfrage konnte nicht verarbeitet werden. Bitte formulieren Sie Ihre Frage höflich und respektvoll.";
            return new SafetyGateResult(language, "ethical_reject", msg);
        }

        _logger.LogInformation("[Ethical] OK");

        var inScope = await (trace?.MeasureAsync(
            "Intent",
            "classifier",
            "Intent-Classifier (lokal)",
            async () =>
            {
                var ok = _intentClassifier.IsHotelWellnessQuery(text);
                if (logIntent)
                {
                    await _queryLogger.LogIntentCheckAsync(
                        query: text,
                        language: language,
                        promptKey: "classifier.intent",
                        promptContent: "local BinaryTextClassifier (in_scope/out_of_scope)",
                        isHotelQuery: ok);
                }
                return (ok, ok ? "ok" : "reject", ok ? "in_scope" : "out_of_scope", (Dictionary<string, object?>?)null);
            }) ?? Task.FromResult(_intentClassifier.IsHotelWellnessQuery(text)));

        if (trace == null && logIntent)
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
        CancellationToken cancellationToken = default,
        PipelineTraceCollector? trace = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var ct = linked.Token;
        trace ??= new PipelineTraceCollector();

        try
        {
            if (string.IsNullOrWhiteSpace(request.HotelId))
            {
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = "A valid hotelId is required.",
                    FinalAnswer = "A valid hotelId is required.",
                    ResponseType = "error",
                    Success = false,
                    ErrorMessage = "hotelId is required"
                }, trace);
            }

            var hotel = await trace.MeasureAsync(
                "HotelLookup",
                "db",
                $"Hotel laden: {request.HotelId}",
                async () =>
                {
                    var h = await _hotelRepository.GetByIdAsync(request.HotelId.Trim(), ct);
                    var ok = h != null && h.IsActive;
                    return (h, ok ? "ok" : "reject", ok ? h!.Name : "nicht gefunden",
                        new Dictionary<string, object?> { ["hotelId"] = request.HotelId });
                });

            if (hotel == null || !hotel.IsActive)
            {
                var unknown = "No hotel was found for the given hotelId. Please use a hotelId from a previous search result.";
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = unknown,
                    FinalAnswer = unknown,
                    ResponseType = "error",
                    Success = false,
                    ErrorMessage = "hotel_not_found"
                }, trace);
            }

            var gates = await RunSafetyGatesAsync(
                request.Message,
                request.Language,
                logIntent: false,
                ct,
                trace);

            if (gates.RejectReason is not null)
            {
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = gates.RejectMessage!,
                    FinalAnswer = gates.RejectMessage!,
                    ResponseType = gates.RejectReason,
                    Success = false,
                    Language = gates.Language,
                    ErrorMessage = gates.RejectReason
                }, trace);
            }

            var language = gates.Language;

            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = request.Message;
                await trace.EmitStartAsync("Translate", "llm", "Skip (Query bereits Deutsch)");
                await trace.EmitEndAsync("Translate", "llm", "übersprungen", 0, "skip");
            }
            else
            {
                queryForSearch = await trace.MeasureAsync(
                    "Translate",
                    "llm",
                    "Query → Deutsch (LLM)",
                    async () =>
                    {
                        var translatePrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.translate_to_german", ct);
                        var q = await _chatCompletionService.TranslateToGermanAsync(request.Message, translatePrompt, ct);
                        return (q, "ok", Truncate(q, 80), new Dictionary<string, object?> { ["query"] = q });
                    });
            }

            var chunks = await trace.MeasureAsync(
                "VectorSearch",
                "search",
                $"Search hotel={hotel.HotelId}",
                async () =>
                {
                    var hits = await _vectorStore.SearchAsync(hotel.HotelId, queryForSearch, 8, 0.40, ct);
                    return (hits, "ok", $"{hits.Count} Chunks",
                        new Dictionary<string, object?> { ["chunks"] = hits.Count });
                });

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
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = request.SessionId ?? string.Empty,
                    Message = noData,
                    FinalAnswer = noData,
                    ResponseType = "no_data",
                    Success = true,
                    Language = language,
                    Hotel = publicHotel,
                    Sources = sources
                }, trace);
            }

            var contextParts = chunks.Select((item, index) =>
                $"[Result {index + 1}] HotelId: {hotel.HotelId} | Hotel: {hotel.Name}\n" +
                (!string.IsNullOrWhiteSpace(item.Chunk.Title) ? $"Section: {item.Chunk.Title}\n" : "") +
                $"{item.Chunk.Content}\n" +
                $"Source URL: {item.Chunk.SourceUrl}");
            var context = string.Join("\n\n---\n\n", contextParts);

            var answer = await trace.MeasureAsync(
                "Answer",
                "llm",
                "Answer-Agent (LLM)",
                async () =>
                {
                    var answerPrompt = await _systemPromptService.GetContentOrNullAsync("pipeline.answer", ct);
                    answerPrompt = (answerPrompt ?? FallbackAnswerPrompt).Replace("{language}", LanguageName(language));
                    var text = await _chatCompletionService.GenerateResponseAsync(
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
                        text += header + "\n" + string.Join("\n", sources.Select(u => $"- {hotel.Name}: {u}"));
                    }

                    return (text, "ok", $"Antwort {text.Length} Zeichen",
                        new Dictionary<string, object?> { ["chars"] = text.Length, ["sources"] = sources.Count });
                });

            return AttachTrace(new ChatResponseDto
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
            }, trace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotel-Details fehlgeschlagen für {HotelId}", request.HotelId);
            return AttachTrace(new ChatResponseDto
            {
                SessionId = request.SessionId ?? string.Empty,
                Message = "An error occurred while loading hotel details. Please try again.",
                FinalAnswer = "An error occurred while loading hotel details. Please try again.",
                ResponseType = "error",
                Success = false,
                ErrorMessage = "internal_error"
            }, trace);
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

    private static HotelRecommendationResponseDto AttachTrace(
        HotelRecommendationResponseDto response,
        PipelineTraceCollector trace)
    {
        response.PipelineTrace = trace.Steps.ToList();
        response.PipelineDurationMs = trace.ElapsedMs;
        return response;
    }

    private static ChatResponseDto AttachTrace(
        ChatResponseDto response,
        PipelineTraceCollector trace)
    {
        response.PipelineTrace = trace.Steps.ToList();
        response.PipelineDurationMs = trace.ElapsedMs;
        return response;
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

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
