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
            var sessionConstraints = LoadConversationConstraints(session);

            var gates = await RunSafetyGatesAsync(
                request.Requirements,
                session,
                logIntent: true,
                ct,
                trace,
                request.Language);

            if (gates.RejectReason == "language_clarify")
            {
                await SaveInteractionAsync(session, request.Requirements, gates.RejectMessage!, gates.Language, null, ct);
                return AttachTrace(BuildPipelineResponse(true, gates.RejectMessage!, "language_clarify", request.Requirements, session.SessionId), trace);
            }

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
            var userQuery = gates.TextToProcess;

            // ─── „Weitere Quellen“ aus Session ─────────────────────────────────────
            if (RecommendationPresentation.IsMoreSourcesRequest(userQuery))
            {
                var more = await TryBuildMoreSourcesAnswerAsync(session, language, ct);
                if (more is not null)
                {
                    await SaveInteractionAsync(session, request.Requirements, more.FinalAnswer!, language, null, ct);
                    return AttachTrace(more, trace);
                }
            }

            // Aktive Hotels für Namens-Match / Regionsfilter (einmal laden)
            var allActiveHotels = (await _hotelRepository.GetAllAsync(ct))
                .Where(h => h.IsActive)
                .ToList();
            var constraints = ConversationConstraintHelper.Merge(
                sessionConstraints, userQuery, allActiveHotels);
            await SaveConversationConstraintsAsync(session, constraints, ct);
            await trace.EmitStartAsync("Constraints", "other",
                $"Region={constraints.Region ?? "—"}, Focus={constraints.FocusHotelName ?? "—"}");
            await trace.EmitEndAsync("Constraints", "other",
                $"region={constraints.Region}, focus={constraints.FocusHotelId}", 0, "ok",
                new Dictionary<string, object?>
                {
                    ["region"] = constraints.Region,
                    ["focusHotelId"] = constraints.FocusHotelId,
                    ["focusHotelName"] = constraints.FocusHotelName
                });

            // ─── Allgemeine Katalog-Frage (optional regionsgefiltert) ─────────────
            if (RecommendationPresentation.IsBroadCatalogQuery(userQuery))
            {
                return await ProcessCatalogListingAsync(
                    request, session, language, constraints, allActiveHotels, trace, ct);
            }

            // ─── Search: Query für Vektorsuche ─────────────────────────────────────
            // Bei Deutsch Original nutzen, sonst LLM-Rewrite — dann Session-Constraints anreichern.
            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = userQuery;
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
                        var q = await _chatCompletionService.TranslateToGermanAsync(userQuery, translatePrompt, ct);
                        _logger.LogInformation("[Search] Query optimiert: Original='{Original}' → Suche='{Optimized}'",
                            userQuery, q);
                        return (q, "ok", $"→ {Truncate(q, 80)}", new Dictionary<string, object?> { ["query"] = q });
                    });
            }

            var rawQuery = queryForSearch;
            queryForSearch = ConversationConstraintHelper.EnrichSearchQuery(queryForSearch, constraints);
            if (!string.Equals(rawQuery, queryForSearch, StringComparison.Ordinal))
            {
                _logger.LogInformation("[Search] Query mit Constraints: '{Raw}' → '{Enriched}'", rawQuery, queryForSearch);
                await trace.EmitStartAsync("QueryEnrich", "search", "Session-Constraints in Vektor-Query");
                await trace.EmitEndAsync("QueryEnrich", "search", Truncate(queryForSearch, 100), 0, "ok",
                    new Dictionary<string, object?>
                    {
                        ["raw"] = rawQuery,
                        ["enriched"] = queryForSearch
                    });
            }

            // 4b. Vektordatenbank durchsuchen
            var maxResults = await GetMaxResultsAsync(ct);
            _logger.LogInformation("[SearchAgent] Starte VectorStore-Suche nach {Elapsed}ms (ct.IsCancellationRequested={Ct})",
                sw.ElapsedMilliseconds, ct.IsCancellationRequested);

            var allResults = await trace.MeasureAsync(
                "VectorSearch",
                "search",
                $"Query: {Truncate(queryForSearch, 100)}",
                async () =>
                {
                    var results = await _vectorStore.SearchAllHotelsAsync(
                        queryForSearch,
                        Math.Max(maxResults * 2, 10),
                        request.MinConfidence,
                        ct);
                    var chunks = results.Values.Sum(v => v.Count);
                    return (results, "ok", $"{results.Count} Hotels, {chunks} Chunks",
                        new Dictionary<string, object?>
                        {
                            ["vectorQuery"] = queryForSearch,
                            ["hotels"] = results.Count,
                            ["chunks"] = chunks
                        });
                });

            // 4c. Gezielte Detail-Suche: Fokus-Hotel zuerst, dann letzte Empfehlungen
            var followUpIds = BuildFollowUpHotelIds(constraints, previousHotelIds, maxTake: 3);
            if (followUpIds.Count > 0)
            {
                await trace.MeasureAsync(
                    "FollowUpSearch",
                    "search",
                    $"Detail-Suche für {followUpIds.Count} Session-Hotels",
                    async () =>
                    {
                        foreach (var prevHotelId in followUpIds)
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
                        return (true, "ok", "Follow-up-Suche fertig",
                            new Dictionary<string, object?> { ["hotelIds"] = followUpIds });
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

            // Nur Ergebnisse von aktiven, bekannten Hotels behalten; optional Region aus Session
            var validResults = allResults
                .Where(kvp => hotelDetails.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            if (ConversationConstraintHelper.ShouldFilterResultsByRegion(constraints, userQuery))
            {
                var before = validResults.Count;
                validResults = validResults
                    .Where(kvp => ConversationConstraintHelper.HotelMatchesRegion(
                        hotelDetails[kvp.Key], constraints.Region))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                // Fokus-Hotel nie wegfiltern
                if (constraints.HasFocus &&
                    allResults.TryGetValue(constraints.FocusHotelId!, out var focusChunks) &&
                    hotelDetails.ContainsKey(constraints.FocusHotelId!) &&
                    !validResults.ContainsKey(constraints.FocusHotelId!))
                {
                    validResults[constraints.FocusHotelId!] = focusChunks;
                }

                await trace.EmitStartAsync("RegionFilter", "search",
                    $"Filter Region={constraints.Region}");
                await trace.EmitEndAsync("RegionFilter", "search",
                    $"{before} → {validResults.Count} Hotels", 0, "ok",
                    new Dictionary<string, object?>
                    {
                        ["region"] = constraints.Region,
                        ["before"] = before,
                        ["after"] = validResults.Count
                    });
            }

            _logger.LogInformation("[SearchAgent] {HotelCount} Hotels, {ChunkCount} Chunks",
                validResults.Count, validResults.Values.Sum(v => v.Count));

            var scoreSummary = RecommendationPresentation.SummarizeHotelScores(validResults, hotelDetails);
            await trace.EmitStartAsync("HotelScores", "search",
                $"Vektor-Treffer: {scoreSummary.Count} Hotels");
            await trace.EmitEndAsync(
                "HotelScores",
                "search",
                string.Join("; ", scoreSummary.Take(8).Select(s => $"{s.Name}={s.Score:0.00}")),
                0,
                "ok",
                new Dictionary<string, object?>
                {
                    ["vectorQuery"] = queryForSearch,
                    ["scores"] = scoreSummary
                        .Select(s => new Dictionary<string, object?>
                        {
                            ["hotelId"] = s.HotelId,
                            ["name"] = s.Name,
                            ["score"] = Math.Round(s.Score, 4)
                        })
                        .ToList()
                });

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
                            userQuery,
                            conversationHistory,
                            language,
                            "User query: {userQuery}",
                            ct);
                        return (text, "ok", $"Antwort {text.Length} Zeichen", (Dictionary<string, object?>?)null);
                    });

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer, language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, noResultsAnswer, "no_results", request.Requirements, session.SessionId), trace);
            }

            // Strukturierte Empfehlungen: bei > maxResults die nächstliegenden (höchster Score)
            var recommendations = BuildRecommendations(validResults, hotelDetails, maxResults);
            if (scoreSummary.Count > maxResults)
            {
                await trace.EmitStartAsync("TopK", "search",
                    $"{scoreSummary.Count} Treffer → Top {maxResults} nach Similarity");
                await trace.EmitEndAsync("TopK", "search",
                    string.Join(", ", recommendations.Select(r => $"{r.HotelName}={r.MatchScore:0.00}")),
                    0, "ok",
                    new Dictionary<string, object?>
                    {
                        ["selected"] = recommendations.Count,
                        ["candidates"] = scoreSummary.Count
                    });
            }

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
                            userQuery, context, relevancePrompt, ct);
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
                            userQuery,
                            conversationHistory,
                            language,
                            "User query: {userQuery}",
                            ct);
                        return (text, "ok", $"Antwort {text.Length} Zeichen", (Dictionary<string, object?>?)null);
                    });

                await SaveInteractionAsync(session, request.Requirements, noResultsAnswer2, language, null, ct);
                return AttachTrace(BuildPipelineResponse(false, noResultsAnswer2, "no_results", request.Requirements, session.SessionId), trace);
            }

            var answerText = await trace.MeasureAsync(
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
                        userQuery,
                        conversationHistory,
                        language,
                        PipelineContextWrapper,
                        ct);
                    return (text, "ok", $"Antwort {text.Length} Zeichen",
                        new Dictionary<string, object?> { ["hotels"] = recommendations.Count, ["chars"] = text.Length });
                });

            var (sourceSection, citedSources, additionalSources) = RecommendationPresentation.BuildWeightedSources(
                answerText, rankedResults, hotelDetails, language);
            var answer = answerText + sourceSection;

            await trace.EmitStartAsync("Sources", "other",
                $"Quellen gewichtet (max {RecommendationPresentation.MaxSourcesPerHotel}/Hotel)");
            await trace.EmitEndAsync(
                "Sources",
                "other",
                string.Join("; ", citedSources.Select(p => $"{p.HotelName}={p.Score:0.00}")),
                0,
                "ok",
                new Dictionary<string, object?>
                {
                    ["vectorQuery"] = queryForSearch,
                    ["cited"] = citedSources.Select(ToSourceMeta).ToList(),
                    ["additional"] = additionalSources.Select(ToSourceMeta).ToList()
                });

            // Interaktion + empfohlene Hotel-IDs + pending sources speichern
            var recommendedIds = recommendations.Select(r => r.HotelId).ToList();
            if (recommendedIds.Count == 1 && hotelDetails.TryGetValue(recommendedIds[0], out var soleHotel))
            {
                constraints.FocusHotelId = soleHotel.HotelId;
                constraints.FocusHotelName = soleHotel.Name;
                await SaveConversationConstraintsAsync(session, constraints, ct);
            }
            else if (constraints.HasFocus && recommendedIds.Contains(constraints.FocusHotelId!))
            {
                // Fokus beibehalten
                await SaveConversationConstraintsAsync(session, constraints, ct);
            }

            await SaveInteractionAsync(session, request.Requirements, answer, language, recommendedIds, ct);
            await SavePendingSourcesAsync(session, additionalSources, ct);

            return AttachTrace(new HotelRecommendationResponseDto
            {
                Success = true,
                FinalAnswer = answer,
                ResponseType = "recommendations",
                Requirements = request.Requirements,
                Recommendations = recommendations,
                SessionId = session.SessionId,
                Timestamp = DateTime.UtcNow,
                VectorQuery = queryForSearch,
                CitedSources = citedSources,
                AdditionalSources = additionalSources,
                HotelScores = scoreSummary.Select(s => new HotelScoreDto
                {
                    HotelId = s.HotelId,
                    HotelName = s.Name,
                    Score = s.Score
                }).ToList()
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

    private sealed record SafetyGateResult(
        string Language,
        string? RejectReason,
        string? RejectMessage,
        string TextToProcess);

    /// <summary>
    /// Gemeinsame Language-/Ethical-/Intent-Gates für Recommend und Hotel-Details.
    /// Sprache wird nach jeder User-Eingabe klassifiziert.
    /// <paramref name="languageHint"/> gilt nur bei Mehrdeutigkeit (kein harter Override).
    /// </summary>
    private async Task<SafetyGateResult> RunSafetyGatesAsync(
        string text,
        ChatSession session,
        bool logIntent,
        CancellationToken ct,
        PipelineTraceCollector? trace = null,
        string? languageHint = null)
    {
        var existingLanguage = LoadConversationLanguage(session);
        var details = _languageDetector.Classify(text);
        var decision = ConversationLanguagePolicy.Resolve(details, existingLanguage, text, languageHint);
        await SaveConversationLanguageAsync(session, decision.State, ct);

        var language = NormalizeLanguageCode(decision.Language);
        var rankedPreview = string.Join(", ",
            details.Ranked.Take(4).Select(s => $"{s.Code}:{s.Probability:0.00}"));
        _logger.LogInformation(
            "[Language] {Source} → {Language} ({Ranked})",
            decision.Source, language, rankedPreview);

        if (trace != null)
        {
            await trace.EmitStartAsync("Language", "classifier", "Spracherkennung (lokal, jede Eingabe)");
            await trace.EmitEndAsync(
                "Language",
                "classifier",
                $"{decision.Source}:{language}",
                0,
                "ok",
                new Dictionary<string, object?>
                {
                    ["language"] = language,
                    ["source"] = decision.Source,
                    ["ambiguous"] = details.IsAmbiguous,
                    ["unrecognized"] = details.IsUnrecognizedScript,
                    ["ranked"] = rankedPreview
                });
        }

        if (decision.NeedsClarification)
        {
            return new SafetyGateResult(language, "language_clarify", decision.ClarificationMessage, decision.TextToProcess);
        }

        var queryText = decision.TextToProcess;

        var ethicalOk = await (trace?.MeasureAsync(
            "Ethical",
            "classifier",
            "Ethical-Classifier (lokal)",
            async () =>
            {
                var ok = _ethicalClassifier.IsEthical(queryText);
                return (ok, ok ? "ok" : "reject", ok ? "OK" : "REJECT", (Dictionary<string, object?>?)null);
            }) ?? Task.FromResult(_ethicalClassifier.IsEthical(queryText)));

        if (!ethicalOk)
        {
            _logger.LogInformation("[Ethical] REJECT");
            var msg = language == "en"
                ? "Your message could not be processed. Please rephrase your question in a polite and respectful manner."
                : "Ihre Anfrage konnte nicht verarbeitet werden. Bitte formulieren Sie Ihre Frage höflich und respektvoll.";
            return new SafetyGateResult(language, "ethical_reject", msg, queryText);
        }

        _logger.LogInformation("[Ethical] OK");

        var inScope = await (trace?.MeasureAsync(
            "Intent",
            "classifier",
            "Intent-Classifier (lokal)",
            async () =>
            {
                var ok = _intentClassifier.IsHotelWellnessQuery(queryText);
                if (logIntent)
                {
                    await _queryLogger.LogIntentCheckAsync(
                        query: queryText,
                        language: language,
                        promptKey: "classifier.intent",
                        promptContent: "local BinaryTextClassifier (in_scope/out_of_scope)",
                        isHotelQuery: ok);
                }
                return (ok, ok ? "ok" : "reject", ok ? "in_scope" : "out_of_scope", (Dictionary<string, object?>?)null);
            }) ?? Task.FromResult(_intentClassifier.IsHotelWellnessQuery(queryText)));

        if (trace == null && logIntent)
        {
            await _queryLogger.LogIntentCheckAsync(
                query: queryText,
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
            return new SafetyGateResult(language, "out_of_scope", msg, queryText);
        }

        _logger.LogInformation("[Intent] in_scope");
        return new SafetyGateResult(language, null, null, queryText);
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

            var session = await GetOrCreatePipelineSessionAsync(request.SessionId, ct);

            var gates = await RunSafetyGatesAsync(
                request.Message,
                session,
                logIntent: false,
                ct,
                trace,
                request.Language);

            if (gates.RejectReason == "language_clarify")
            {
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = session.SessionId,
                    Message = gates.RejectMessage!,
                    FinalAnswer = gates.RejectMessage!,
                    ResponseType = "language_clarify",
                    Success = true,
                    Language = gates.Language
                }, trace);
            }

            if (gates.RejectReason is not null)
            {
                return AttachTrace(new ChatResponseDto
                {
                    SessionId = session.SessionId,
                    Message = gates.RejectMessage!,
                    FinalAnswer = gates.RejectMessage!,
                    ResponseType = gates.RejectReason,
                    Success = false,
                    Language = gates.Language,
                    ErrorMessage = gates.RejectReason
                }, trace);
            }

            var language = gates.Language;
            var userQuery = gates.TextToProcess;

            string queryForSearch;
            if (language.Equals("de", StringComparison.OrdinalIgnoreCase))
            {
                queryForSearch = userQuery;
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
                        var q = await _chatCompletionService.TranslateToGermanAsync(userQuery, translatePrompt, ct);
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
                    SessionId = session.SessionId,
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
                        userQuery,
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
                SessionId = session.SessionId,
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
    /// Allgemeine Katalog-Antwort: aktive Hotels (optional nach Region), zufällige Reihenfolge.
    /// Speichert keine empfohlenen Hotel-IDs (sonst Follow-up auf 3 zufällige Katalog-Hotels).
    /// </summary>
    private async Task<HotelRecommendationResponseDto> ProcessCatalogListingAsync(
        HotelRecommendationRequestDto request,
        ChatSession session,
        string language,
        ConversationConstraints constraints,
        IReadOnlyList<Hotel> allActiveHotels,
        PipelineTraceCollector trace,
        CancellationToken ct)
    {
        var region = constraints.Region
                     ?? ConversationConstraintHelper.ExtractRegion(request.Requirements);
        if (!string.IsNullOrWhiteSpace(region) && constraints.Region != region)
        {
            constraints.Region = region;
            await SaveConversationConstraintsAsync(session, constraints, ct);
        }

        var hotels = ConversationConstraintHelper.FilterByRegion(allActiveHotels, region);
        // Falls Region-Metadaten lückenhaft: Vektorsuche nach Region ergänzen
        if (!string.IsNullOrWhiteSpace(region) && hotels.Count < allActiveHotels.Count)
        {
            try
            {
                var vectorHits = await _vectorStore.SearchAllHotelsAsync(region, 20, 0.35, ct);
                var byId = allActiveHotels.ToDictionary(h => h.HotelId, StringComparer.OrdinalIgnoreCase);
                foreach (var hotelId in vectorHits.Keys)
                {
                    if (byId.TryGetValue(hotelId, out var h) &&
                        hotels.All(x => !x.HotelId.Equals(h.HotelId, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Nur aufnehmen wenn Metadaten zur Region passen ODER Metadaten leer
                        var metaEmpty = string.IsNullOrWhiteSpace(h.Region) && string.IsNullOrWhiteSpace(h.Location);
                        if (metaEmpty || ConversationConstraintHelper.HotelMatchesRegion(h, region))
                            hotels.Add(h);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Katalog: Vektor-Ergänzung für Region {Region} fehlgeschlagen", region);
            }
        }

        await trace.EmitStartAsync("Catalog", "db",
            region == null ? "Alle aktiven Hotels (Katalog)" : $"Katalog Region={region}");
        await trace.EmitEndAsync("Catalog", "db",
            $"{hotels.Count} Hotels", 0, "ok",
            new Dictionary<string, object?>
            {
                ["count"] = hotels.Count,
                ["region"] = region
            });

        var shuffled = RecommendationPresentation.Shuffle(hotels);
        await trace.EmitStartAsync("CatalogShuffle", "other", "Zufallsreihenfolge");
        await trace.EmitEndAsync("CatalogShuffle", "other",
            shuffled.Count == 0
                ? "(leer)"
                : string.Join(", ", shuffled.Take(5).Select(h => h.Name)) + (shuffled.Count > 5 ? "…" : ""),
            0, "ok");

        var answer = RecommendationPresentation.BuildCatalogAnswer(shuffled, language, region);
        var recommendations = shuffled.Select((h, index) =>
        {
            var pub = HotelPublicDto.FromHotel(h);
            return new HotelRecommendationDto
            {
                HotelId = h.HotelId,
                HotelName = h.Name,
                Domain = h.Domain,
                Location = pub.Location,
                Region = pub.Region,
                Country = pub.Country,
                OfficialUrl = pub.OfficialUrl,
                SourceUrl = pub.SourceUrl,
                EditorialReviewStatus = pub.EditorialReviewStatus,
                EditorialReviewedAt = pub.EditorialReviewedAt,
                Categories = pub.Categories,
                MatchScore = 0,
                Reason = "catalog",
                MatchingFeatures = [],
                Sources = string.IsNullOrWhiteSpace(h.ResolveOfficialUrl())
                    ? []
                    : [h.ResolveOfficialUrl()!],
                Rank = index + 1
            };
        }).ToList();

        // Keine __recommended_hotels__ für den ganzen Katalog — Follow-up nutzt Constraints.
        await SaveInteractionAsync(session, request.Requirements, answer, language, null, ct);

        return AttachTrace(new HotelRecommendationResponseDto
        {
            Success = true,
            FinalAnswer = answer,
            ResponseType = "catalog",
            Requirements = request.Requirements,
            Recommendations = recommendations,
            SessionId = session.SessionId,
            Timestamp = DateTime.UtcNow,
            VectorQuery = null,
            CitedSources = [],
            AdditionalSources = [],
            HotelScores = []
        }, trace);
    }

    private async Task<HotelRecommendationResponseDto?> TryBuildMoreSourcesAnswerAsync(
        ChatSession session,
        string language,
        CancellationToken ct)
    {
        var pending = LoadPendingSources(session);
        if (pending.Count == 0)
            return null;

        await ClearPendingSourcesAsync(session, ct);

        var header = language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "**Additional sources:**"
            : "**Weitere Quellen:**";
        var lines = pending.Select(c =>
            $"- {c.HotelName} ({c.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}): {c.Url}");
        var answer = header + "\n" + string.Join("\n", lines);

        return new HotelRecommendationResponseDto
        {
            Success = true,
            FinalAnswer = answer,
            ResponseType = "more_sources",
            SessionId = session.SessionId,
            CitedSources = pending,
            AdditionalSources = [],
            Timestamp = DateTime.UtcNow
        };
    }

    private const string PendingSourcesPrefix = "__pending_sources__:";

    private static List<SourceCitationDto> LoadPendingSources(ChatSession session)
    {
        var msg = session.Messages
            .Where(m => m.Role == MessageRole.System && m.Content.StartsWith(PendingSourcesPrefix))
            .MaxBy(m => m.Timestamp);
        if (msg is null) return [];
        try
        {
            return JsonSerializer.Deserialize<List<SourceCitationDto>>(msg.Content[PendingSourcesPrefix.Length..])
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SavePendingSourcesAsync(
        ChatSession session,
        List<SourceCitationDto> additional,
        CancellationToken ct)
    {
        session.Messages.RemoveAll(m =>
            m.Role == MessageRole.System && m.Content.StartsWith(PendingSourcesPrefix));

        if (additional.Count == 0)
        {
            await _sessionRepository.UpdateAsync(session, ct);
            return;
        }

        session.Messages.Add(new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            Role = MessageRole.System,
            Content = PendingSourcesPrefix + JsonSerializer.Serialize(additional),
            Timestamp = DateTime.UtcNow
        });
        session.LastActivityAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, ct);
    }

    private async Task ClearPendingSourcesAsync(ChatSession session, CancellationToken ct)
    {
        session.Messages.RemoveAll(m =>
            m.Role == MessageRole.System && m.Content.StartsWith(PendingSourcesPrefix));
        await _sessionRepository.UpdateAsync(session, ct);
    }

    private static Dictionary<string, object?> ToSourceMeta(SourceCitationDto c) => new()
    {
        ["hotelId"] = c.HotelId,
        ["hotelName"] = c.HotelName,
        ["url"] = c.Url,
        ["score"] = Math.Round(c.Score, 4)
    };

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
    /// Deprecated: nutze RecommendationPresentation.BuildWeightedSources.
    /// </summary>
    private static string BuildSourcesSection(
        Dictionary<string, List<(ContentChunk Chunk, double Score)>> results,
        Dictionary<string, Hotel> hotelDetails,
        string language)
    {
        var answerStub = string.Join(" ", hotelDetails.Values.Select(h => h.Name));
        var (section, _, _) = RecommendationPresentation.BuildWeightedSources(
            answerStub, results, hotelDetails, language);
        return section;
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

    private static ConversationConstraints? LoadConversationConstraints(ChatSession session)
    {
        var msg = session.Messages
            .Where(m => m.Role == MessageRole.System &&
                        m.Content.StartsWith(ConversationConstraintHelper.SessionPrefix, StringComparison.Ordinal))
            .MaxBy(m => m.Timestamp);
        if (msg is null) return null;
        return ConversationConstraintHelper.Deserialize(
            msg.Content[ConversationConstraintHelper.SessionPrefix.Length..]);
    }

    private static ConversationLanguageState? LoadConversationLanguage(ChatSession session)
    {
        var msg = session.Messages
            .Where(m => m.Role == MessageRole.System &&
                        m.Content.StartsWith(ConversationLanguagePolicy.SessionPrefix, StringComparison.Ordinal))
            .MaxBy(m => m.Timestamp);
        if (msg is null) return null;
        return ConversationLanguagePolicy.Deserialize(
            msg.Content[ConversationLanguagePolicy.SessionPrefix.Length..]);
    }

    private async Task SaveConversationLanguageAsync(
        ChatSession session,
        ConversationLanguageState state,
        CancellationToken ct)
    {
        session.Messages.RemoveAll(m =>
            m.Role == MessageRole.System &&
            m.Content.StartsWith(ConversationLanguagePolicy.SessionPrefix, StringComparison.Ordinal));

        session.Messages.Add(new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            Role = MessageRole.System,
            Content = ConversationLanguagePolicy.SessionPrefix +
                      ConversationLanguagePolicy.Serialize(state),
            Timestamp = DateTime.UtcNow
        });
        session.LastActivityAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, ct);
    }

    private async Task SaveConversationConstraintsAsync(
        ChatSession session,
        ConversationConstraints constraints,
        CancellationToken ct)
    {
        session.Messages.RemoveAll(m =>
            m.Role == MessageRole.System &&
            m.Content.StartsWith(ConversationConstraintHelper.SessionPrefix, StringComparison.Ordinal));

        session.Messages.Add(new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            Role = MessageRole.System,
            Content = ConversationConstraintHelper.SessionPrefix +
                      ConversationConstraintHelper.Serialize(constraints),
            Timestamp = DateTime.UtcNow
        });
        session.LastActivityAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, ct);
    }

    private static List<string> BuildFollowUpHotelIds(
        ConversationConstraints constraints,
        List<string> previousHotelIds,
        int maxTake)
    {
        var ids = new List<string>();
        if (constraints.HasFocus)
            ids.Add(constraints.FocusHotelId!);
        foreach (var id in previousHotelIds)
        {
            if (ids.Count >= maxTake) break;
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                ids.Add(id);
        }
        return ids.Take(maxTake).ToList();
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
        "(e.g. distances, nearby airports, restaurants, travel time, regional geography). Mark approximations clearly.\n" +
        "RULE 3 – Conversation grounding: Respect prior user constraints from the conversation history (region, previously discussed hotel, “dort/there”). " +
        "If the user asked about a region earlier, stay within that region unless they clearly change topic. Prefer the focused hotel when they use deixis.";

    private const string FallbackAnswerPrompt =
        "You are a friendly hotel search assistant for BestWellness wellness hotels.\n" +
        "Compose a helpful, friendly response in {language} using the provided database results.\n" +
        "STRICT rule – hotel recommendations: You must NEVER recommend or mention hotels that are NOT in the provided database results.\n" +
        "STRICT rule – factual accuracy: State only facts, numbers, sizes, and offers from the database results. Do not invent, round, or merge conflicting values.\n" +
        "When the user asks about seasonal offers, list ONLY offers matching that season in the results.\n" +
        "ALLOWED – general knowledge: You MAY use general world knowledge for factual questions ABOUT hotels in the results (distances, geography). Mark approximations clearly.\n" +
        "CONVERSATION GROUNDING: Honor prior constraints in the chat history (region, focused hotel, deixis like “dort”). Do not widen to other regions unless the user asks.\n" +
        "Be concise, helpful, and professional.";

    private const string FallbackNoResultsPrompt =
        "Compose a brief, friendly message in {language} informing the user that no matching hotels were found in the BestWellness database for their request.\n" +
        "Do NOT suggest alternative hotels. Do NOT use your training knowledge to recommend hotels.\n" +
        "Output ONLY the message to the user. No other text.";
}
