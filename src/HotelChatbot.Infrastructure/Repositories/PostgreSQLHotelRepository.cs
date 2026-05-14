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
        ";

        await using var cmd = new NpgsqlCommand(createTableSql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _isInitialized = true;
        _logger.LogInformation("PostgreSQL HotelRepository initialisiert");
    }

    public async Task<Hotel?> GetByIdAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT hotel_id, name, domain, allowed_domains, api_key, is_active FROM hotels WHERE hotel_id = $1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotelId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Hotel
            {
                HotelId = reader.GetString(0),
                Name = reader.GetString(1),
                Domain = reader.GetString(2),
                AllowedDomains = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ApiKey = reader.GetString(4),
                IsActive = reader.GetBoolean(5)
            };
        }

        return null;
    }

    public async Task<Hotel?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT hotel_id, name, domain, allowed_domains, api_key, is_active FROM hotels WHERE domain = $1 AND is_active = TRUE;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(domain);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Hotel
            {
                HotelId = reader.GetString(0),
                Name = reader.GetString(1),
                Domain = reader.GetString(2),
                AllowedDomains = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ApiKey = reader.GetString(4),
                IsActive = reader.GetBoolean(5)
            };
        }

        return null;
    }

    public async Task<Hotel?> GetByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT hotel_id, name, domain, allowed_domains, api_key, is_active FROM hotels WHERE api_key = $1 AND is_active = TRUE;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(apiKey);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Hotel
            {
                HotelId = reader.GetString(0),
                Name = reader.GetString(1),
                Domain = reader.GetString(2),
                AllowedDomains = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ApiKey = reader.GetString(4),
                IsActive = reader.GetBoolean(5)
            };
        }

        return null;
    }

    public async Task<List<Hotel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = "SELECT hotel_id, name, domain, allowed_domains, api_key, is_active FROM hotels;";

        await using var cmd = new NpgsqlCommand(sql, conn);

        var hotels = new List<Hotel>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hotels.Add(new Hotel
            {
                HotelId = reader.GetString(0),
                Name = reader.GetString(1),
                Domain = reader.GetString(2),
                AllowedDomains = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ApiKey = reader.GetString(4),
                IsActive = reader.GetBoolean(5)
            });
        }

        return hotels;
    }

    public async Task AddAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            INSERT INTO hotels (hotel_id, name, domain, allowed_domains, api_key, is_active)
            VALUES ($1, $2, $3, $4::jsonb, $5, $6)
            ON CONFLICT (hotel_id) 
            DO UPDATE SET
                name = EXCLUDED.name,
                domain = EXCLUDED.domain,
                allowed_domains = EXCLUDED.allowed_domains,
                is_active = EXCLUDED.is_active,
                updated_at = NOW();
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(hotel.HotelId);
        cmd.Parameters.AddWithValue(hotel.Name);
        cmd.Parameters.AddWithValue(hotel.Domain);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(hotel.AllowedDomains));
        cmd.Parameters.AddWithValue(hotel.ApiKey);
        cmd.Parameters.AddWithValue(hotel.IsActive);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Hotel {HotelId} gespeichert", hotel.HotelId);
    }

    public async Task UpdateAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        await AddAsync(hotel, cancellationToken); // Upsert
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
