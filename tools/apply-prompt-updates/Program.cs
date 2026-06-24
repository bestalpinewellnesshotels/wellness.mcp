using System.Text.Json;
using HotelChatbot.Infrastructure.Repositories;
using Npgsql;

var keysToUpdate = new HashSet<string>(StringComparer.Ordinal)
{
    "pipeline.translate_to_german",
    "mcp.get_response.description",
    "pipeline.logical_check",
    "pipeline.relevance_check",
    "pipeline.answer",
};

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var appsettingsPath = Path.Combine(repoRoot, "src", "HotelChatbot.Api", "appsettings.json");
using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
var cs = doc.RootElement.GetProperty("ConnectionStrings").GetProperty("PostgreSQL").GetString()!;

var seeds = PostgreSQLSystemPromptRepository.GetDefaultPrompts()
    .Where(p => keysToUpdate.Contains(p.Key))
    .ToList();

await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

const string sql = """
    UPDATE system_prompts
    SET content = @content,
        content_de = @contentDe,
        description = @description,
        updated_at = NOW()
    WHERE key = @key
    """;

foreach (var prompt in seeds)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("key", prompt.Key);
    cmd.Parameters.AddWithValue("content", prompt.Content);
    cmd.Parameters.AddWithValue("contentDe", prompt.ContentDe);
    cmd.Parameters.AddWithValue("description", prompt.Description);
    var rows = await cmd.ExecuteNonQueryAsync();
    Console.WriteLine(rows > 0 ? $"Updated {prompt.Key}" : $"NOT FOUND: {prompt.Key}");
}

Console.WriteLine($"Done. Applied {seeds.Count} prompt updates.");
