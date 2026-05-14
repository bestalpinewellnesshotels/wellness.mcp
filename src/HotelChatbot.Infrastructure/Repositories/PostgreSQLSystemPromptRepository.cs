using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HotelChatbot.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL Repository für System-Prompts.
/// Erstellt die Tabelle automatisch und befüllt sie beim ersten Start mit Standardwerten.
/// </summary>
public class PostgreSQLSystemPromptRepository : ISystemPromptRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLSystemPromptRepository> _logger;
    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PostgreSQLSystemPromptRepository(
        IConfiguration configuration,
        ILogger<PostgreSQLSystemPromptRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string nicht konfiguriert");
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Initialisierung & Seeding
    // -------------------------------------------------------------------------

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS system_prompts (
                    id          SERIAL PRIMARY KEY,
                    key         TEXT NOT NULL UNIQUE,
                    name        TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    content     TEXT NOT NULL,
                    content_de  TEXT NOT NULL DEFAULT '',
                    language    TEXT NULL,
                    is_active   BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at  TIMESTAMP NOT NULL DEFAULT NOW(),
                    updated_at  TIMESTAMP NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_system_prompts_key ON system_prompts(key);
            ";

            await using (var cmd = new NpgsqlCommand(createTableSql, conn))
                await cmd.ExecuteNonQueryAsync(cancellationToken);

            await SeedDefaultPromptsAsync(conn, cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("PostgreSQL SystemPromptRepository initialisiert");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task SeedDefaultPromptsAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING: bestehende (ggf. angepasste) Einträge bleiben erhalten,
        // neue Keys (z.B. nach einem Update) werden dennoch eingefügt.
        foreach (var prompt in GetDefaultPrompts())
        {
            const string sql = @"
                INSERT INTO system_prompts (key, name, description, content, content_de, language, is_active, created_at, updated_at)
                VALUES (@key, @name, @description, @content, @contentDe, @language, TRUE, NOW(), NOW())
                ON CONFLICT (key) DO NOTHING";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("key", prompt.Key);
            cmd.Parameters.AddWithValue("name", prompt.Name);
            cmd.Parameters.AddWithValue("description", prompt.Description);
            cmd.Parameters.AddWithValue("content", prompt.Content);
            cmd.Parameters.AddWithValue("contentDe", prompt.ContentDe);
            cmd.Parameters.AddWithValue("language", (object?)prompt.Language ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Standard-SystemPrompts in Datenbank gespeichert ({Count} Einträge)", GetDefaultPrompts().Count);
    }

    // -------------------------------------------------------------------------
    // CRUD-Operationen
    // -------------------------------------------------------------------------

    public async Task<List<SystemPrompt>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, key, name, description, content, content_de, language, is_active, created_at, updated_at
            FROM system_prompts
            ORDER BY key";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var results = new List<SystemPrompt>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));

        return results;
    }

    public async Task<SystemPrompt?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, key, name, description, content, content_de, language, is_active, created_at, updated_at
            FROM system_prompts
            WHERE key = @key";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<SystemPrompt?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, key, name, description, content, content_de, language, is_active, created_at, updated_at
            FROM system_prompts
            WHERE id = @id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<SystemPrompt> CreateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO system_prompts (key, name, description, content, content_de, language, is_active, created_at, updated_at)
            VALUES (@key, @name, @description, @content, @contentDe, @language, @isActive, NOW(), NOW())
            RETURNING id, key, name, description, content, content_de, language, is_active, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", prompt.Key);
        cmd.Parameters.AddWithValue("name", prompt.Name);
        cmd.Parameters.AddWithValue("description", prompt.Description);
        cmd.Parameters.AddWithValue("content", prompt.Content);
        cmd.Parameters.AddWithValue("contentDe", prompt.ContentDe);
        cmd.Parameters.AddWithValue("language", (object?)prompt.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isActive", prompt.IsActive);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return MapRow(reader);
    }

    public async Task<SystemPrompt?> UpdateAsync(SystemPrompt prompt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE system_prompts
            SET name        = @name,
                description = @description,
                content     = @content,
                content_de  = @contentDe,
                language    = @language,
                is_active   = @isActive,
                updated_at  = NOW()
            WHERE id = @id
            RETURNING id, key, name, description, content, content_de, language, is_active, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", prompt.Id);
        cmd.Parameters.AddWithValue("name", prompt.Name);
        cmd.Parameters.AddWithValue("description", prompt.Description);
        cmd.Parameters.AddWithValue("content", prompt.Content);
        cmd.Parameters.AddWithValue("contentDe", prompt.ContentDe);
        cmd.Parameters.AddWithValue("language", (object?)prompt.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isActive", prompt.IsActive);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = "DELETE FROM system_prompts WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    private static SystemPrompt MapRow(NpgsqlDataReader reader) => new()
    {
        Id          = reader.GetInt32(0),
        Key         = reader.GetString(1),
        Name        = reader.GetString(2),
        Description = reader.GetString(3),
        Content     = reader.GetString(4),
        ContentDe   = reader.GetString(5),
        Language    = reader.IsDBNull(6) ? null : reader.GetString(6),
        IsActive    = reader.GetBoolean(7),
        CreatedAt   = reader.GetDateTime(8),
        UpdatedAt   = reader.GetDateTime(9),
    };

    // -------------------------------------------------------------------------
    // Standard-Prompts (Seed-Daten)
    // Alle LLM-Prompts sind auf Englisch - GPT-4.1 liefert damit zuverlässigere Ergebnisse.
    // ON CONFLICT DO NOTHING: Änderungen im Admin-CMS bleiben erhalten.
    // -------------------------------------------------------------------------

    private static List<SystemPrompt> GetDefaultPrompts() =>
    [

        new()
        {
            Key         = "pipeline.language_detect",
            Name        = "Pipeline: LanguageAgent",
            Description = "Erkennt die Sprache der User-Anfrage. Gibt einen ISO-639-1 Code zurück (z.B. 'de', 'en').",
            Language    = null,
            Content     = """
You are a language detector. Identify the language of the following text.
Reply with ONLY the ISO 639-1 two-letter language code in lowercase (e.g., "de", "en", "fr", "it", "es").
No other text. No punctuation. Just the code.
""",
            ContentDe   = """
Du bist ein Spracherkennungssystem. Identifiziere die Sprache des folgenden Textes.
Antworte NUR mit dem ISO 639-1 zweistelligen Sprachcode in Kleinbuchstaben (z. B. "de", "en", "fr", "it", "es").
Kein weiterer Text. Keine Satzzeichen. Nur der Code.
"""
        },
        new()
        {
            Key         = "pipeline.ethical_check",
            Name        = "Pipeline: EthicalAgent",
            Description = "Prüft ob die User-Anfrage ethisch vertretbar ist (höflich, nicht diskriminierend). Antwortet nur OK oder REJECT.",
            Language    = null,
            Content     = """
You are an ethical content validator for a hotel search assistant.
Evaluate whether the following user message is appropriate to respond to.
Reply ONLY with "OK" if the message is polite and appropriate.
Reply ONLY with "REJECT" if the message is rude, offensive, discriminatory, hateful, or harassing.
No other text. No explanation. Just: OK or REJECT.
""",
            ContentDe   = """
Du bist ein ethischer Inhaltsvalidator für einen Hotelsuch-Assistenten.
Beurteile, ob die folgende Benutzernachricht angemessen beantwortet werden kann.
Antworte NUR mit "OK", wenn die Nachricht höflich und angemessen ist.
Antworte NUR mit "REJECT", wenn die Nachricht unhöflich, beleidigend, diskriminierend, hasserfüllt oder belästigend ist.
Kein weiterer Text. Keine Erklärung. Nur: OK oder REJECT.
"""
        },
        new()
        {
            Key         = "pipeline.logical_check",
            Name        = "Pipeline: LogicAgent",
            Description = "Prüft ob die Anfrage thematisch zu Hotels/Urlaub/Wellness passt. Antwortet nur YES oder NO.",
            Language    = null,
            Content     = """
You are a topic relevance classifier for a wellness hotel search assistant.
Determine whether the following user message could relate to: hotels, accommodation, wellness, spa, vacation, leisure, skiing, hiking, horse riding, culinary experiences, adults-only resorts, relaxation, recovery, or similar topics.
Be inclusive: a message may seem unrelated at first but still be relevant to a hotel stay or vacation.
Reply ONLY with "YES" if the message could relate to the above topics.
Reply ONLY with "NO" if the message has no conceivable connection to hotels or vacation.
No other text. No explanation. Just: YES or NO.
""",
            ContentDe   = """
Du bist ein Themenrelevanz-Klassifikator für einen Wellness-Hotelsuch-Assistenten.
Bestimme, ob die folgende Benutzernachricht in Zusammenhang stehen könnte mit: Hotels, Unterkunft, Wellness, Spa, Urlaub, Freizeit, Skifahren, Wandern, Reiten, kulinarischen Erlebnissen, Adults-Only-Resorts, Entspannung, Erholung oder ähnlichen Themen.
Sei großzügig: Eine Nachricht mag zunächst unrelated erscheinen, kann aber dennoch für einen Hotelaufenthalt oder Urlaub relevant sein.
Antworte NUR mit "YES", wenn die Nachricht einen Bezug zu den oben genannten Themen haben könnte.
Antworte NUR mit "NO", wenn die Nachricht keinerlei denkbaren Zusammenhang mit Hotels oder Urlaub hat.
Kein weiterer Text. Keine Erklärung. Nur: YES oder NO.
"""
        },
        new()
        {
            Key         = "pipeline.translate_to_german",
            Name        = "Pipeline: SearchAgent – Query-Optimierung für Vektorsuche",
            Description = "Übersetzt die User-Anfrage ins Deutsche UND extrahiert die relevanten Suchbegriffe inkl. Synonyme für optimale Vektordatenbank-Trefferquote.",
            Language    = null,
            Content     = """
Your task is to optimize a user query for semantic vector database search.
The database contains German hotel descriptions (features, location, amenities, activities).

Step 1 – Translate: If the input is not in German, translate it to German first.
Step 2 – Extract: Remove question words and sentence structure (wer, was, welches, wie weit, nächste, closest, best, etc.). Keep only the meaningful search nouns and topics.
Step 3 – Expand: Add 3–5 closely related German synonyms or terms that plausibly appear in hotel descriptions.

Examples:
Input: "Welches Hotel ist Zell am See am nächsten?" → Output: "Zell am See Hotel Nähe Salzburger Land Pinzgau Kaprun"
Input: "Reitangebote" → Output: "Reiten Reitangebote Reitstall Pferde Reiturlaub Pferdesport Ausritte Reithalle"
Input: "adults only hotel" → Output: "Adults Only Hotel Erwachsene Keine Kinder Ruhig Exklusiv Paarurlaub"
Input: "Wellness und Spa" → Output: "Wellness Spa Sauna Massagen Therme Erholung Entspannung Dampfbad"
Input: "hotel near the sea" → Output: "Hotel Meer Küste Strand Meeresblick Nordsee Ostsee Seeblick"

Output ONLY the optimized German search terms — no sentence, no explanation, just the key terms separated by spaces.
""",
            ContentDe   = """
Deine Aufgabe ist es, eine Benutzeranfrage für die semantische Vektordatenbanksuche zu optimieren.
Die Datenbank enthält deutsche Hotelbeschreibungen (Ausstattung, Lage, Annehmlichkeiten, Aktivitäten).

Schritt 1 – Übersetzen: Falls die Eingabe nicht auf Deutsch ist, übersetze sie zunächst ins Deutsche.
Schritt 2 – Extrahieren: Entferne Fragewörter und Satzstruktur (wer, was, welches, wie weit, nächste, closest, best, etc.). Behalte nur die bedeutsamen Suchbegriffe und Themen.
Schritt 3 – Erweitern: Füge 3–5 eng verwandte deutsche Synonyme oder Begriffe hinzu, die plausiblerweise in Hotelbeschreibungen vorkommen.

Beispiele:
Eingabe: "Welches Hotel ist Zell am See am nächsten?" → Ausgabe: "Zell am See Hotel Nähe Salzburger Land Pinzgau Kaprun"
Eingabe: "Reitangebote" → Ausgabe: "Reiten Reitangebote Reitstall Pferde Reiturlaub Pferdesport Ausritte Reithalle"
Eingabe: "adults only hotel" → Ausgabe: "Adults Only Hotel Erwachsene Keine Kinder Ruhig Exklusiv Paarurlaub"
Eingabe: "Wellness und Spa" → Ausgabe: "Wellness Spa Sauna Massagen Therme Erholung Entspannung Dampfbad"
Eingabe: "hotel near the sea" → Ausgabe: "Hotel Meer Küste Strand Meeresblick Nordsee Ostsee Seeblick"

Gib NUR die optimierten deutschen Suchbegriffe aus – keinen Satz, keine Erklärung, nur die Schlüsselbegriffe durch Leerzeichen getrennt.
"""
        },
        new()
        {
            Key         = "pipeline.relevance_check",
            Name        = "Pipeline: RelevanceAgent – Kontextprüfung nach Vektorsuche",
            Description = "Prüft ob die Vektordatenbank-Ergebnisse die Benutzeranfrage inhaltlich beantworten können. Antwortet nur YES oder NO.",
            Language    = null,
            Content     = """
You are a relevance validator for a hotel search assistant.
You will receive a user query and a context containing hotel database excerpts.
Determine whether the context contains factual information that directly answers or addresses the user's query.

Reply ONLY with "YES" if the context contains relevant information that answers the user's query.
Reply ONLY with "NO" if the context does NOT contain relevant information for the user's query (e.g. the user asks about horse riding but the context only mentions fitness and pilates).

Be strict: if the specific feature, activity, or property the user is asking about is not mentioned in the context, answer NO.
No other text. No explanation. Just: YES or NO.
""",
            ContentDe   = """
Du bist ein Relevanz-Validator für einen Hotelsuch-Assistenten.
Du erhältst eine Benutzeranfrage und einen Kontext mit Auszügen aus der Hoteldatenbank.
Bestimme, ob der Kontext sachliche Informationen enthält, die die Anfrage des Benutzers direkt beantworten oder ansprechen.

Antworte NUR mit "YES", wenn der Kontext relevante Informationen enthält, die die Anfrage beantworten.
Antworte NUR mit "NO", wenn der Kontext KEINE relevanten Informationen für die Anfrage enthält (z. B. der Benutzer fragt nach Reiten, aber der Kontext erwähnt nur Fitness und Pilates).

Sei streng: Wenn das spezifische Merkmal, die Aktivität oder die Eigenschaft, nach der der Benutzer fragt, im Kontext nicht erwähnt wird, antworte mit NO.
Kein weiterer Text. Keine Erklärung. Nur: YES oder NO.
"""
        },
        new()
        {
            Key         = "pipeline.answer",
            Name        = "Pipeline: AnswerAgent / Antwort bei Treffern",
            Description = "Formuliert eine freundliche Antwort in der Sprache des Users auf Basis der Vektordatenbank-Ergebnisse. Platzhalter: {language}",
            Language    = null,
            Content     = """
You are a friendly hotel search assistant for BestWellness wellness hotels.
You will receive database search results for a user query.
Compose a helpful, friendly response in {language} using the provided database results.

STRICT rule - hotel recommendations:
- You must NEVER recommend, mention, or suggest hotels that are NOT present in the database results.
- Every hotel name you mention must come directly from the database results.

ALLOWED - general world knowledge:
- You MAY use your general world knowledge to answer factual or logistical questions ABOUT the hotels found in the results.
- Examples: distance from a hotel to a city, airport, or landmark; nearby restaurants, ski resorts, or points of interest; travel time; regional geography.
- If the user asks which hotel in the results is closest to a location, use your geographic knowledge to estimate and answer - state it as an approximation if exact data is unavailable.

Formatting:
- Be concise, helpful, and professional.
- Do not repeat the source URLs - they will be appended automatically after your response.
""",
            ContentDe   = """
Du bist ein freundlicher Hotelsuch-Assistent für BestWellness Wellnesshotels.
Du erhältst Datenbanksuchergebnisse zu einer Benutzeranfrage.
Verfasse eine hilfreiche, freundliche Antwort auf {language} auf Basis der bereitgestellten Datenbankergebnisse.

STRIKTE Regel – Hotelempfehlungen:
- Du darfst NIEMALS Hotels empfehlen, erwähnen oder vorschlagen, die NICHT in den Datenbankergebnissen enthalten sind.
- Jeder von dir genannte Hotelname muss direkt aus den Datenbankergebnissen stammen.

ERLAUBT – Allgemeinwissen:
- Du DARFST dein allgemeines Weltwissen nutzen, um sachliche oder logistische Fragen ZU den in den Ergebnissen gefundenen Hotels zu beantworten.
- Beispiele: Entfernung von einem Hotel zu einer Stadt, einem Flughafen oder einem Wahrzeichen; nahegelegene Restaurants, Skigebiete oder Sehenswürdigkeiten; Reisezeit; regionale Geografie.
- Falls der Benutzer fragt, welches Hotel in den Ergebnissen einer Lage am nächsten liegt, nutze dein geografisches Wissen für eine Schätzung – weise darauf hin, wenn es sich um eine Näherung handelt.

Formatierung:
- Sei präzise, hilfreich und professionell.
- Wiederhole nicht die Quell-URLs – sie werden automatisch nach deiner Antwort angehängt.
"""
        },
        new()
        {
            Key         = "pipeline.no_results",
            Name        = "Pipeline: AnswerAgent / Antwort bei keinen Treffern",
            Description = "Formuliert eine freundliche Meldung in der Sprache des Users, wenn die Vektorsuche keine Ergebnisse liefert. Platzhalter: {language}",
            Language    = null,
            Content     = """
Compose a brief, friendly message in {language} informing the user that the BestWellness database currently contains no hotels matching their specific request.
Keep the tone helpful and encouraging — suggest they rephrase their question or ask about a different feature, activity, or region.
You MUST NOT name or recommend any specific hotel that is not in the database results (which are empty at this point).
Output ONLY the message to the user. No other text.
""",
            ContentDe   = """
Verfasse eine kurze, freundliche Meldung auf {language}, die den Benutzer darüber informiert, dass die BestWellness-Datenbank derzeit keine Hotels enthält, die seiner spezifischen Anfrage entsprechen.
Halte den Ton hilfsbereit und ermutigend – schlage vor, die Frage umzuformulieren oder nach einem anderen Merkmal, einer Aktivität oder Region zu fragen.
Du darfst KEIN konkretes Hotel nennen oder empfehlen, das nicht in den Datenbankergebnissen enthalten ist (die an dieser Stelle leer sind).
Gib NUR die Nachricht an den Benutzer aus. Kein weiterer Text.
"""
        },


        new()
        {
            Key         = "config.search.max_results",
            Name        = "Konfiguration: Maximale Suchergebnisse",
            Description = "Anzahl der Hotels, die die Vektordatenbank maximal zurückliefern soll. Ganzzahl zwischen 1 und 20. Im Admin-Bereich änderbar.",
            Language    = null,
            Content     = "3",
            ContentDe   = "3"
        },

        // Diese Prompts steuern das Verhalten des OpenAI Custom GPT.
        // Der MCP-Server lädt sie beim Start und alle 5 Minuten neu.

        new()
        {
            Key         = "mcp.get_response.description",
            Name        = "MCP: Tool-Beschreibung (get_response)",
            Description = "Anweisung die ChatGPT bei jedem Tool-Aufruf sieht. Änderungen beeinflussen direkt das Verhalten des OpenAI Custom GPT.",
            Language    = null,
            Content     = """
You are an assistant for the BestWellness hotel platform. This tool MUST be called for EVERY user message.

STRICT RULES for hotel recommendations - NO EXCEPTIONS:
1. You must NEVER recommend, mention, or suggest hotels that are NOT returned by this tool.
2. Every hotel name in your response MUST come from the tool response.
3. Do NOT invent hotels. Do NOT guess hotel properties. Do NOT suggest hotels outside the database.
4. If the tool response contains "status": "no_match" - output ONLY the "answer" field. Do NOT name any hotels. STOP.
5. If the tool response contains "status": "error" - output ONLY the "answer" field. STOP.

ALLOWED - general world knowledge:
6. You MAY use your general world knowledge to answer factual or logistical questions ABOUT the hotels that appear in the tool response.
7. Examples: distance from a city to a hotel in the results, nearby airports, restaurants, travel time, regional geography.
8. Always make clear when you are using an approximation (e.g. "approximately X km").

Output:
9. Output the exact text from the "answer" field — then you may add brief general-knowledge context about the hotel(s) in the results if it directly answers the user's question. - NO EXCEPTIONS:
1. The BestWellness tool response is the ONLY allowed source of hotel information.
2. You must NEVER mention or suggest hotels that are not returned by this tool.
3. You must NEVER use general world knowledge, training data, or assumptions about hotels.
4. Only hotels that appear in the tool response may appear in your answer.
5. If the tool response contains "status": "no_match" — output ONLY the "answer" field. Do NOT suggest alternatives. Do NOT name any hotels. STOP.
6. If the tool response contains "status": "error" — output ONLY the "answer" field. Do NOT answer from your own knowledge.
7. Do NOT invent hotels. Do NOT guess hotel properties. Do NOT try to be helpful by suggesting alternatives outside the database.
8. Every hotel name in your response MUST come from the last tool response.
9. Output ONLY the exact text from the "answer" field. Word for word. Nothing added.
""",
            ContentDe   = """
Du bist ein Assistent für die BestWellness-Hotelplattform. Dieses Tool MUSS für JEDE Benutzernachricht aufgerufen werden.

STRENGE REGELN für Hotelempfehlungen – KEINE AUSNAHMEN:
1. Du darfst NIEMALS Hotels empfehlen, erwähnen oder vorschlagen, die von diesem Tool NICHT zurückgegeben werden.
2. Jeder Hotelname in deiner Antwort MUSS aus der Tool-Antwort stammen.
3. Erfinde KEINE Hotels. Rate KEINE Hoteleigenschaften. Schlage KEINE Hotels außerhalb der Datenbank vor.
4. Wenn die Tool-Antwort "status": "no_match" enthält – gib NUR das Feld "answer" aus. Nenne KEINE Hotels. STOP.
5. Wenn die Tool-Antwort "status": "error" enthält – gib NUR das Feld "answer" aus. STOP.

ERLAUBT – Allgemeinwissen:
6. Du DARFST dein allgemeines Weltwissen nutzen, um sachliche oder logistische Fragen ZU den Hotels aus der Tool-Antwort zu beantworten.
7. Beispiele: Entfernung von einer Stadt zu einem Hotel in den Ergebnissen, nahegelegene Flughäfen, Restaurants, Reisezeit, regionale Geografie.
8. Weise immer darauf hin, wenn du eine Näherung verwendest (z. B. "ca. X km").

Ausgabe:
9. Gib den genauen Text aus dem Feld "answer" aus – danach kannst du bei Bedarf kurzen Allgemeinwissen-Kontext zu den in der Antwort enthaltenen Hotels hinzufügen, wenn es die Frage des Benutzers direkt beantwortet.
"""
        },
        new()
        {
            Key         = "mcp.get_hotel_details.description",
            Name        = "MCP: Tool-Beschreibung (get_hotel_details)",
            Description = "Anweisung für Detailabfragen zu einem bestimmten Hotel. Nur aufrufen wenn eine Hotel-ID aus einem vorherigen Ergebnis bekannt ist.",
            Language    = null,
            Content     = """
Returns details for a specific hotel from the BestWellness database.
Only call this when a hotel ID from a previous get_response result is available.
STRICT RULES: Output ONLY the "answer" field. Never add hotel names, alternatives or information from training data.
""",
            ContentDe   = """
Gibt Details zu einem bestimmten Hotel aus der BestWellness-Datenbank zurück.
Nur aufrufen, wenn eine Hotel-ID aus einem vorherigen get_response-Ergebnis vorhanden ist.
STRENGE REGELN: Gib NUR das Feld "answer" aus. Füge niemals Hotelnamen, Alternativen oder Informationen aus Trainingsdaten hinzu.
"""
        },
        new()
        {
            Key         = "mcp.result.instruction.ok",
            Name        = "MCP: Ergebnis-Anweisung (Treffer)",
            Description = "Wird jeder erfolgreichen Tool-Antwort als _instruction-Feld beigefügt. Sagt ChatGPT: nur den 'answer'-Text ausgeben, nichts hinzufügen.",
            Language    = null,
            Content     = "Output the exact text in 'answer'. Every hotel NAME you mention must come from this tool response — never add hotels from outside the database. You MAY add brief general-knowledge context (distances, nearby airports, restaurants) about the hotels already present in the answer.",
            ContentDe   = "Gib den genauen Text in 'answer' aus. Jeder Hotelname, den du nennst, muss aus dieser Tool-Antwort stammen – füge niemals Hotels hinzu, die nicht in der Datenbank sind. Du DARFST kurzen Allgemeinwissen-Kontext (Entfernungen, nahegelegene Flughäfen, Restaurants) zu den bereits in der Antwort enthaltenen Hotels hinzufügen."
        },
        new()
        {
            Key         = "mcp.result.instruction.no_match",
            Name        = "MCP: Ergebnis-Anweisung (kein Treffer)",
            Description = "Wird einer 'kein Treffer'-Antwort beigefügt. Verhindert dass ChatGPT eigenes Hotelwissen einsetzt oder Alternativen erfindet.",
            Language    = null,
            Content     = "STATUS=no_match: Output ONLY the exact text in 'answer'. You are NOT allowed to name any hotel, suggest alternatives, or use training knowledge. This is the complete and final response. STOP.",
            ContentDe   = "STATUS=no_match: Gib NUR den genauen Text in 'answer' aus. Du darfst KEIN Hotel nennen, keine Alternativen vorschlagen oder Trainingswissen verwenden. Dies ist die vollständige und abschließende Antwort. STOP."
        },
    ];
}
