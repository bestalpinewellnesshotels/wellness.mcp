using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace HotelChatbot.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL Repository für Hotel-Entitäten.
/// </summary>
public class PostgreSQLHotelRepository : IHotelRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLHotelRepository> _logger;
    private bool _isInitialized = false;

    private const string SelectColumns = @"
        hotel_id, name, domain, allowed_domains, api_key, is_active,
        location, region, country, official_url, source_url,
        editorial_review_status, editorial_reviewed_at, categories,
        created_at, updated_at, latitude, longitude";

    public PostgreSQLHotelRepository(
        IConfiguration configuration,
        ILogger<PostgreSQLHotelRepository> logger)
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
            CREATE TABLE IF NOT EXISTS hotels (
                hotel_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                domain TEXT NOT NULL,
                allowed_domains JSONB NOT NULL,
                api_key TEXT NOT NULL,
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_domain ON hotels(domain);
            CREATE INDEX IF NOT EXISTS idx_api_key ON hotels(api_key);

            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS location TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS region TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS country TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS official_url TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS source_url TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS editorial_review_status TEXT;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS editorial_reviewed_at TIMESTAMP;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS categories JSONB NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS latitude DOUBLE PRECISION;
            ALTER TABLE hotels ADD COLUMN IF NOT EXISTS longitude DOUBLE PRECISION;
        ";

        await using var cmd = new NpgsqlCommand(createTableSql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _isInitialized = true;
        _logger.LogInformation("PostgreSQL HotelRepository initialisiert");
    }

    private static Hotel MapHotel(NpgsqlDataReader reader)
    {
        var categoriesJson = reader.IsDBNull(13) ? "[]" : reader.GetString(13);
        return new Hotel
        {
            HotelId = reader.GetString(0),
            Name = reader.GetString(1),
            Domain = reader.GetString(2),
            AllowedDomains = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
            ApiKey = reader.IsDBNull(4) ? null : reader.GetString(4),
            IsActive = reader.GetBoolean(5),
            Location = reader.IsDBNull(6) ? null : reader.GetString(6),
            Region = reader.IsDBNull(7) ? null : reader.GetString(7),
            Country = reader.IsDBNull(8) ? null : reader.GetString(8),
            OfficialUrl = reader.IsDBNull(9) ? null : reader.GetString(9),
            SourceUrl = reader.IsDBNull(10) ? null : reader.GetString(10),
            EditorialReviewStatus = reader.IsDBNull(11) ? null : reader.GetString(11),
            EditorialReviewedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            Categories = JsonSerializer.Deserialize<List<string>>(categoriesJson) ?? new List<string>(),
            CreatedAt = reader.IsDBNull(14) ? DateTime.UtcNow : reader.GetDateTime(14),
            UpdatedAt = reader.IsDBNull(15) ? DateTime.UtcNow : reader.GetDateTime(15),
            Latitude = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetDouble(16) : null,
            Longitude = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetDouble(17) : null
        };
    }

    public async Task<Hotel?> GetByIdAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = $"SELECT {SelectColumns} FROM hotels WHERE hotel_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return MapHotel(reader);

        return null;
    }

    public async Task<List<Hotel>> GetByIdsAsync(IEnumerable<string> hotelIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var ids = hotelIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
            return new List<Hotel>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = $"SELECT {SelectColumns} FROM hotels WHERE hotel_id = ANY($1);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(ids.ToArray());

        var hotels = new List<Hotel>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            hotels.Add(MapHotel(reader));

        return hotels;
    }

    public async Task<Hotel?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = $"SELECT {SelectColumns} FROM hotels WHERE domain = $1 AND is_active = TRUE;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(domain);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return MapHotel(reader);

        return null;
    }

    public async Task<Hotel?> GetByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = $"SELECT {SelectColumns} FROM hotels WHERE api_key = $1 AND is_active = TRUE;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(apiKey);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return MapHotel(reader);

        return null;
    }

    public async Task<List<Hotel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = $"SELECT {SelectColumns} FROM hotels;";

        await using var cmd = new NpgsqlCommand(sql, conn);

        var hotels = new List<Hotel>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            hotels.Add(MapHotel(reader));

        return hotels;
    }

    public async Task AddAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            INSERT INTO hotels (
                hotel_id, name, domain, allowed_domains, api_key, is_active,
                location, region, country, official_url, source_url,
                editorial_review_status, editorial_reviewed_at, categories,
                latitude, longitude
            )
            VALUES (
                $1, $2, $3, $4::jsonb, $5, $6,
                $7, $8, $9, $10, $11,
                $12, $13, $14::jsonb,
                $15, $16
            )
            ON CONFLICT (hotel_id)
            DO UPDATE SET
                name = EXCLUDED.name,
                domain = EXCLUDED.domain,
                allowed_domains = EXCLUDED.allowed_domains,
                api_key = COALESCE(EXCLUDED.api_key, hotels.api_key),
                is_active = EXCLUDED.is_active,
                location = EXCLUDED.location,
                region = EXCLUDED.region,
                country = EXCLUDED.country,
                official_url = EXCLUDED.official_url,
                source_url = EXCLUDED.source_url,
                editorial_review_status = EXCLUDED.editorial_review_status,
                editorial_reviewed_at = EXCLUDED.editorial_reviewed_at,
                categories = EXCLUDED.categories,
                latitude = COALESCE(EXCLUDED.latitude, hotels.latitude),
                longitude = COALESCE(EXCLUDED.longitude, hotels.longitude),
                updated_at = NOW();
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotel.HotelId);
        cmd.Parameters.AddWithValue(hotel.Name);
        cmd.Parameters.AddWithValue(hotel.Domain);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(hotel.AllowedDomains));
        cmd.Parameters.AddWithValue(hotel.ApiKey ?? string.Empty);
        cmd.Parameters.AddWithValue(hotel.IsActive);
        cmd.Parameters.AddWithValue((object?)hotel.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.Region ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.Country ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.OfficialUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.EditorialReviewStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.EditorialReviewedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(hotel.Categories ?? new List<string>()));
        cmd.Parameters.AddWithValue((object?)hotel.Latitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)hotel.Longitude ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Hotel {HotelId} gespeichert", hotel.HotelId);
    }

    public async Task UpdateAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        await AddAsync(hotel, cancellationToken);
    }

    public async Task DeleteAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "DELETE FROM hotels WHERE hotel_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Hotel {HotelId} gelöscht", hotelId);
    }
}
