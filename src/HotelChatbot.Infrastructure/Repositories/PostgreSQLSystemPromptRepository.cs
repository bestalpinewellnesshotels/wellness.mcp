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

        // Soft-Upgrade: Conversation-Grounding in pipeline.answer nachziehen (ON CONFLICT DO NOTHING lässt alte Seeds stehen)
        await EnsurePromptFragmentAsync(
            conn,
            "pipeline.answer",
            "CONVERSATION GROUNDING",
            """

CONVERSATION GROUNDING:
- Honor constraints from the conversation history: region (e.g. Tirol), focused hotel, and deixis (“dort”, “there”, “this hotel”).
- If the user previously limited the search to a region, do not widen to other regions unless they clearly ask.
- When they refer to a previously discussed hotel, keep that hotel in focus.
""",
            "GESPRÄCHSKONTEXT",
            """

GESPRÄCHSKONTEXT:
- Respektiere Einschränkungen aus dem Gesprächsverlauf: Region (z. B. Tirol), Fokus-Hotel und Deixis („dort“, „dieses Hotel“).
- Hat der Benutzer die Suche zuvor auf eine Region beschränkt, weite nicht auf andere Regionen aus, es sei denn, er fragt klar danach.
- Bezieht er sich auf ein zuvor besprochenes Hotel, bleibe bei diesem Hotel.
""",
            cancellationToken);
    }

    /// <summary>
    /// Hängt fehlende Prompt-Abschnitte an bestehende Inhalte an (ohne sie zu ersetzen).
    /// </summary>
    private async Task EnsurePromptFragmentAsync(
        NpgsqlConnection conn,
        string key,
        string markerEn,
        string fragmentEn,
        string markerDe,
        string fragmentDe,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE system_prompts
            SET
                content = CASE
                    WHEN content NOT LIKE '%' || @markerEn || '%' THEN content || @fragmentEn
                    ELSE content
                END,
                content_de = CASE
                    WHEN content_de NOT LIKE '%' || @markerDe || '%' THEN content_de || @fragmentDe
                    ELSE content_de
                END,
                updated_at = NOW()
            WHERE key = @key
              AND (
                    content NOT LIKE '%' || @markerEn || '%'
                 OR content_de NOT LIKE '%' || @markerDe || '%'
              )";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("markerEn", markerEn);
        cmd.Parameters.AddWithValue("fragmentEn", fragmentEn);
        cmd.Parameters.AddWithValue("markerDe", markerDe);
        cmd.Parameters.AddWithValue("fragmentDe", fragmentDe);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows > 0)
            _logger.LogInformation("SystemPrompt '{Key}' um Conversation-Grounding ergänzt", key);
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

    public static List<SystemPrompt> GetDefaultPrompts() =>
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

ALWAYS reply YES if the message:
- Mentions, refers to, or asks about a hotel name, property name, brand, or common short form (e.g. "Stock", "das Stock", "Nesslerhof", "Gmachl", "Krallerhof")
- Asks where, how to find, or about the location of a named property
- Is a short query that could be a hotel name or nickname, even without the word "hotel"

Reply ONLY with "YES" if the message could relate to the above topics.
Reply ONLY with "NO" only if the message clearly has no conceivable connection to hotels, vacation, wellness, or a hotel property.
No other text. No explanation. Just: YES or NO.
""",
            ContentDe   = """
Du bist ein Themenrelevanz-Klassifikator für einen Wellness-Hotelsuch-Assistenten.
Bestimme, ob die folgende Benutzernachricht in Zusammenhang stehen könnte mit: Hotels, Unterkunft, Wellness, Spa, Urlaub, Freizeit, Skifahren, Wandern, Reiten, kulinarischen Erlebnissen, Adults-Only-Resorts, Entspannung, Erholung oder ähnlichen Themen.
Sei großzügig: Eine Nachricht mag zunächst unrelated erscheinen, kann aber dennoch für einen Hotelaufenthalt oder Urlaub relevant sein.

Antworte IMMER mit "YES", wenn die Nachricht:
- Einen Hotelnamen, Objektnamen, Markennamen oder eine gängige Kurzform erwähnt oder danach fragt (z. B. „Stock“, „das Stock“, „Nesslerhof“, „Gmachl“, „Krallerhof“)
- Fragt, wo sich ein benanntes Haus befindet oder wie man es findet
- Eine kurze Anfrage ist, die ein Hotelname oder Spitzname sein könnte – auch ohne das Wort „Hotel“

Antworte NUR mit "YES", wenn die Nachricht einen Bezug zu den oben genannten Themen haben könnte.
Antworte NUR mit "NO", wenn die Nachricht eindeutig keinerlei denkbaren Zusammenhang mit Hotels, Urlaub, Wellness oder einem Hotelobjekt hat.
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
Step 4 – Preserve: Keep explicit hotel names, regions, and season or holiday terms from the input (Winter, Sommer, Frühling, Herbst, Weihnachten, Ostern, Ski, etc.).

Examples:
Input: "Welches Hotel ist Zell am See am nächsten?" → Output: "Zell am See Hotel Nähe Salzburger Land Pinzgau Kaprun"
Input: "Reitangebote" → Output: "Reiten Reitangebote Reitstall Pferde Reiturlaub Pferdesport Ausritte Reithalle"
Input: "adults only hotel" → Output: "Adults Only Hotel Erwachsene Keine Kinder Ruhig Exklusiv Paarurlaub"
Input: "Wellness und Spa" → Output: "Wellness Spa Sauna Massagen Therme Erholung Entspannung Dampfbad"
Input: "hotel near the sea" → Output: "Hotel Meer Küste Strand Meeresblick Nordsee Ostsee Seeblick"
Input: "Winterangebote Hotel Nesslerhof" → Output: "Winterangebote Winter Ski Schnee Wintersport Nesslerhof Winterpauschale Skiurlaub"
Input: "wo ist das Stock" → Output: "Stock Hotel Lage Standort Adresse Anfahrt"

Output ONLY the optimized German search terms — no sentence, no explanation, just the key terms separated by spaces.
""",
            ContentDe   = """
Deine Aufgabe ist es, eine Benutzeranfrage für die semantische Vektordatenbanksuche zu optimieren.
Die Datenbank enthält deutsche Hotelbeschreibungen (Ausstattung, Lage, Annehmlichkeiten, Aktivitäten).

Schritt 1 – Übersetzen: Falls die Eingabe nicht auf Deutsch ist, übersetze sie zunächst ins Deutsche.
Schritt 2 – Extrahieren: Entferne Fragewörter und Satzstruktur (wer, was, welches, wie weit, nächste, closest, best, etc.). Behalte nur die bedeutsamen Suchbegriffe und Themen.
Schritt 3 – Erweitern: Füge 3–5 eng verwandte deutsche Synonyme oder Begriffe hinzu, die plausiblerweise in Hotelbeschreibungen vorkommen.
Schritt 4 – Bewahren: Behalte explizite Hotelnamen, Regionen sowie Saison- oder Feiertagsbegriffe aus der Eingabe (Winter, Sommer, Frühling, Herbst, Weihnachten, Ostern, Ski, etc.).

Beispiele:
Eingabe: "Welches Hotel ist Zell am See am nächsten?" → Ausgabe: "Zell am See Hotel Nähe Salzburger Land Pinzgau Kaprun"
Eingabe: "Reitangebote" → Ausgabe: "Reiten Reitangebote Reitstall Pferde Reiturlaub Pferdesport Ausritte Reithalle"
Eingabe: "adults only hotel" → Ausgabe: "Adults Only Hotel Erwachsene Keine Kinder Ruhig Exklusiv Paarurlaub"
Eingabe: "Wellness und Spa" → Ausgabe: "Wellness Spa Sauna Massagen Therme Erholung Entspannung Dampfbad"
Eingabe: "Winterangebote Hotel Nesslerhof" → Ausgabe: "Winterangebote Winter Ski Schnee Wintersport Nesslerhof Winterpauschale Skiurlaub"
Eingabe: "wo ist das Stock" → Ausgabe: "Stock Hotel Lage Standort Adresse Anfahrt"

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

Season and offer matching (be strict):
- If the user asks about winter offers/ski/snow, the context must mention winter, ski, snow, or explicitly winter-themed offers. Summer or generic wellness offers alone are NOT sufficient → answer NO.
- If the user asks about summer offers, the context must mention summer or summer-themed offers. Winter-only offers are NOT sufficient → answer NO.
- Apply the same logic for other seasonal or holiday-specific requests (Christmas, Easter, etc.).

Factual specificity:
- If the user asks for a specific number, size, or measurement, the context must contain that information or a clearly matching value — loosely related facts are NOT sufficient.

No other text. No explanation. Just: YES or NO.
""",
            ContentDe   = """
Du bist ein Relevanz-Validator für einen Hotelsuch-Assistenten.
Du erhältst eine Benutzeranfrage und einen Kontext mit Auszügen aus der Hoteldatenbank.
Bestimme, ob der Kontext sachliche Informationen enthält, die die Anfrage des Benutzers direkt beantworten oder ansprechen.

Antworte NUR mit "YES", wenn der Kontext relevante Informationen enthält, die die Anfrage beantworten.
Antworte NUR mit "NO", wenn der Kontext KEINE relevanten Informationen für die Anfrage enthält (z. B. der Benutzer fragt nach Reiten, aber der Kontext erwähnt nur Fitness und Pilates).

Sei streng: Wenn das spezifische Merkmal, die Aktivität oder die Eigenschaft, nach der der Benutzer fragt, im Kontext nicht erwähnt wird, antworte mit NO.

Saison- und Angebotsabgleich (streng):
- Fragt der Benutzer nach Winterangeboten/Ski/Schnee, muss der Kontext Winter, Ski, Schnee oder ausdrücklich winterliche Angebote erwähnen. Sommer- oder generische Wellnessangebote allein reichen NICHT → antworte NO.
- Fragt der Benutzer nach Sommerangeboten, muss der Kontext Sommer oder sommerliche Angebote erwähnen. Rein winterliche Angebote reichen NICHT → antworte NO.
- Gleiche Logik für andere saisonale oder feiertagsbezogene Anfragen (Weihnachten, Ostern, etc.).

Sachliche Genauigkeit:
- Fragt der Benutzer nach einer konkreten Zahl, Größe oder einem Maß, muss der Kontext diese Information oder einen eindeutig passenden Wert enthalten — lose verwandte Fakten reichen NICHT.

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

CONVERSATION GROUNDING:
- Honor constraints from the conversation history: region (e.g. Tirol), focused hotel, and deixis (“dort”, “there”, “this hotel”).
- If the user previously limited the search to a region, do not widen to other regions unless they clearly ask.
- When they refer to a previously discussed hotel, keep that hotel in focus.

ALLOWED - general world knowledge:
- You MAY use your general world knowledge to answer factual or logistical questions ABOUT the hotels found in the results.
- Examples: distance from a hotel to a city, airport, or landmark; nearby restaurants, ski resorts, or points of interest; travel time; regional geography.
- If the user asks which hotel in the results is closest to a location, use your geographic knowledge to estimate and answer - state it as an approximation if exact data is unavailable.

STRICT rule - factual accuracy:
- State only facts, numbers, sizes, amenities, and offers that appear in the database results. Do NOT invent, round, estimate, or merge conflicting values.
- If the context shows different numbers for the same property (e.g. 40,000 vs 41,000 sqm), use the exact wording from the results or omit the number — never guess.
- When the user asks about seasonal offers (winter, summer, etc.), list ONLY packages/offers that match that season in the database results. Do not present off-season offers as answers. If no matching seasonal offers appear in the results, say so honestly.

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

GESPRÄCHSKONTEXT:
- Respektiere Einschränkungen aus dem Gesprächsverlauf: Region (z. B. Tirol), Fokus-Hotel und Deixis („dort“, „dieses Hotel“).
- Hat der Benutzer die Suche zuvor auf eine Region beschränkt, weite nicht auf andere Regionen aus, es sei denn, er fragt klar danach.
- Bezieht er sich auf ein zuvor besprochenes Hotel, bleibe bei diesem Hotel.

ERLAUBT – Allgemeinwissen:
- Du DARFST dein allgemeines Weltwissen nutzen, um sachliche oder logistische Fragen ZU den in den Ergebnissen gefundenen Hotels zu beantworten.
- Beispiele: Entfernung von einem Hotel zu einer Stadt, einem Flughafen oder einem Wahrzeichen; nahegelegene Restaurants, Skigebiete oder Sehenswürdigkeiten; Reisezeit; regionale Geografie.
- Falls der Benutzer fragt, welches Hotel in den Ergebnissen einer Lage am nächsten liegt, nutze dein geografisches Wissen für eine Schätzung – weise darauf hin, wenn es sich um eine Näherung handelt.

STRIKTE Regel – sachliche Genauigkeit:
- Nenne nur Fakten, Zahlen, Größen, Ausstattungen und Angebote, die in den Datenbankergebnissen vorkommen. Erfinde, runde, schätze oder vermische widersprüchliche Werte NICHT.
- Zeigt der Kontext unterschiedliche Zahlen für dieselbe Eigenschaft (z. B. 40.000 vs. 41.000 qm), verwende die exakte Formulierung aus den Ergebnissen oder lasse die Zahl weg – rate niemals.
- Fragt der Benutzer nach saisonalen Angeboten (Winter, Sommer, etc.), nenne NUR Pakete/Angebote, die in den Ergebnissen zur Saison passen. Präsentiere keine Angebote aus einer anderen Saison als Antwort. Wenn keine passenden saisonalen Angebote in den Ergebnissen stehen, sage das ehrlich.

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
You are an assistant for the BestWellness hotel platform. This tool MUST be called silently for EVERY user message. Output the "answer" field without announcing the tool call.

STRICT RULES – NO EXCEPTIONS:
1. Never recommend, mention, or suggest hotels that are NOT returned by this tool.
2. Every hotel name in your response MUST come from the tool response.
3. Do NOT invent hotels. Do NOT guess hotel properties. Do NOT suggest hotels outside the database.
4. If the tool response contains "status": "no_match" — output ONLY the "answer" field. Do NOT name any hotels. STOP.
5. If the tool response contains "status": "error" — output ONLY the "answer" field. STOP.

ALLOWED – general world knowledge:
6. You MAY use general world knowledge for factual or logistical questions ABOUT hotels in the tool response (distances, nearby airports, restaurants, travel time, regional geography).
7. Always mark approximations clearly (e.g. "approximately X km").

FORBIDDEN – hotel facts from training data:
8. Facts ABOUT the hotel itself (amenities, sizes, offers, room types) must ONLY come from the tool response. Surrounding-area information (distances, geography) is allowed.

Output:
9. Output the exact text from the "answer" field — then you may add brief general-knowledge context about hotel(s) already in the answer if it directly answers the user's question.
""",
            ContentDe   = """
Du bist ein Assistent für die BestWellness-Hotelplattform. Dieses Tool MUSS für JEDE Benutzernachricht still aufgerufen werden. Gib das Feld „answer“ aus, ohne den Toolaufruf anzukündigen.

STRIKTE REGELN – KEINE AUSNAHMEN:
1. Empfiehl, erwähne oder schlage niemals Hotels vor, die NICHT von diesem Tool zurückgegeben werden.
2. Jeder Hotelname in deiner Antwort MUSS aus der Toolantwort stammen.
3. Erfinde keine Hotels. Rate keine Hoteleigenschaften. Schlage keine Hotels außerhalb der Datenbank vor.
4. Wenn die Toolantwort „status“: „no_match“ enthält – gib NUR das Feld „answer“ aus. Nenne keine Hotels. STOPP.
5. Wenn die Toolantwort „status“: „error“ enthält – gib NUR das Feld „answer“ aus. STOPP.

ERLAUBT – allgemeines Weltwissen:
6. Du DARFST dein allgemeines Weltwissen nutzen, um sachliche oder logistische Fragen ÜBER die Hotels in der Toolantwort zu beantworten (Entfernungen, nahegelegene Flughäfen, Restaurants, Reisezeit, regionale Geografie).
7. Mache immer deutlich, wenn du eine Näherung verwendest (z. B. „ca. X km“).

VERBOTEN – Hotelfakten aus Trainingswissen:
8. Fakten ÜBER das Hotel selbst (Ausstattung, Größen, Angebote, Zimmertypen) dürfen NUR aus der Toolantwort stammen. Infos rund um das Hotel (Entfernungen, Geografie) sind erlaubt.

Ausgabe:
9. Gib den exakten Text aus dem Feld „answer“ aus – danach darfst du kurzen Allgemeinwissen-Kontext zu den bereits in der Antwort enthaltenen Hotels ergänzen, wenn das die Frage des Nutzers direkt beantwortet.
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
