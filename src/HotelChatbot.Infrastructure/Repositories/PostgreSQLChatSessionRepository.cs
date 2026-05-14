using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace HotelChatbot.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL Repository für Chat-Sessions.
/// </summary>
public class PostgreSQLChatSessionRepository : IChatSessionRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLChatSessionRepository> _logger;
    private bool _isInitialized = false;

    public PostgreSQLChatSessionRepository(
        IConfiguration configuration,
        ILogger<PostgreSQLChatSessionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS chat_sessions (
                session_id TEXT PRIMARY KEY,
                hotel_id TEXT NOT NULL,
                messages JSONB NOT NULL DEFAULT '[]',
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_session_hotel ON chat_sessions(hotel_id);
            CREATE INDEX IF NOT EXISTS idx_session_updated ON chat_sessions(updated_at);
        ";

        await using var cmd = new NpgsqlCommand(createTableSql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _isInitialized = true;
        _logger.LogInformation("PostgreSQL ChatSessionRepository initialisiert");
    }

    public async Task<ChatSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT session_id, hotel_id, messages FROM chat_sessions WHERE session_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ChatSession
            {
                SessionId = reader.GetString(0),
                HotelId = reader.GetString(1),
                Messages = JsonSerializer.Deserialize<List<ChatMessage>>(reader.GetString(2)) ?? new List<ChatMessage>()
            };
        }

        return null;
    }

    public async Task SaveAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            INSERT INTO chat_sessions (session_id, hotel_id, messages)
            VALUES ($1, $2, $3)
            ON CONFLICT (session_id)
            DO UPDATE SET
                messages = EXCLUDED.messages,
                updated_at = NOW();
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(session.SessionId);
        cmd.Parameters.AddWithValue(session.HotelId);
        cmd.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(session.Messages), NpgsqlDbType = NpgsqlDbType.Jsonb });

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await SaveAsync(session, cancellationToken);
    }

    public async Task UpdateAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        await SaveAsync(session, cancellationToken);
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        // Session laden, Message hinzufügen, speichern
        var session = await GetByIdAsync(message.SessionId ?? string.Empty, cancellationToken);
        if (session != null)
        {
            session.Messages.Add(message);
            await SaveAsync(session, cancellationToken);
        }
    }

    public async Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetByIdAsync(sessionId, cancellationToken);
        return session?.Messages ?? new List<ChatMessage>();
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "DELETE FROM chat_sessions WHERE session_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(sessionId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<ChatSession>> GetByHotelIdAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT session_id, hotel_id, messages FROM chat_sessions WHERE hotel_id = $1 ORDER BY updated_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        var sessions = new List<ChatSession>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new ChatSession
            {
                SessionId = reader.GetString(0),
                HotelId = reader.GetString(1),
                Messages = JsonSerializer.Deserialize<List<ChatMessage>>(reader.GetString(2)) ?? new List<ChatMessage>()
            });
        }

        return sessions;
    }

    public async Task CleanupOldSessionsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow - maxAge;
        var sql = "DELETE FROM chat_sessions WHERE updated_at < $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(cutoffDate);

        var deletedCount = await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("{Count} alte Chat-Sessions gelöscht", deletedCount);
    }
}
