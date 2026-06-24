using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HotelChatbot.Infrastructure.Repositories;

public class PostgreSQLFeedbackRepository : IFeedbackRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLFeedbackRepository> _logger;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PostgreSQLFeedbackRepository(
        IConfiguration configuration,
        ILogger<PostgreSQLFeedbackRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string nicht konfiguriert");
        _logger = logger;
    }

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
                CREATE TABLE IF NOT EXISTS admin_feedback (
                    id              SERIAL PRIMARY KEY,
                    name            TEXT NOT NULL,
                    keyword         TEXT NOT NULL,
                    text            TEXT NOT NULL,
                    status          TEXT NOT NULL DEFAULT '',
                    created_by_role TEXT NOT NULL,
                    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
                    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_admin_feedback_created_at ON admin_feedback(created_at DESC);
                ALTER TABLE admin_feedback ADD COLUMN IF NOT EXISTS hotel_id TEXT;
            ";

            await using var cmd = new NpgsqlCommand(createTableSql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("PostgreSQL FeedbackRepository initialisiert");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<Feedback>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at
            FROM admin_feedback
            ORDER BY created_at DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var results = new List<Feedback>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));

        return results;
    }

    public async Task<Feedback?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at
            FROM admin_feedback
            WHERE id = @id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<Feedback> CreateAsync(Feedback feedback, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO admin_feedback (hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at)
            VALUES (@hotelId, @name, @keyword, @text, @status, @createdByRole, NOW(), NOW())
            RETURNING id, hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("hotelId", (object?)feedback.HotelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("name", feedback.Name);
        cmd.Parameters.AddWithValue("keyword", feedback.Keyword);
        cmd.Parameters.AddWithValue("text", feedback.Text);
        cmd.Parameters.AddWithValue("status", feedback.Status);
        cmd.Parameters.AddWithValue("createdByRole", feedback.CreatedByRole);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return MapRow(reader);
    }

    public async Task<Feedback?> UpdateAsync(Feedback feedback, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE admin_feedback
            SET hotel_id = @hotelId,
                name = @name,
                keyword = @keyword,
                text = @text,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", feedback.Id);
        cmd.Parameters.AddWithValue("hotelId", (object?)feedback.HotelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("name", feedback.Name);
        cmd.Parameters.AddWithValue("keyword", feedback.Keyword);
        cmd.Parameters.AddWithValue("text", feedback.Text);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    public async Task<Feedback?> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE admin_feedback
            SET status = @status, updated_at = NOW()
            WHERE id = @id
            RETURNING id, hotel_id, name, keyword, text, status, created_by_role, created_at, updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRow(reader) : null;
    }

    private static Feedback MapRow(NpgsqlDataReader reader) => new()
    {
        Id            = reader.GetInt32(0),
        HotelId       = reader.IsDBNull(1) ? null : reader.GetString(1),
        Name          = reader.GetString(2),
        Keyword       = reader.GetString(3),
        Text          = reader.GetString(4),
        Status        = reader.GetString(5),
        CreatedByRole = reader.GetString(6),
        CreatedAt     = reader.GetDateTime(7),
        UpdatedAt     = reader.GetDateTime(8),
    };
}
