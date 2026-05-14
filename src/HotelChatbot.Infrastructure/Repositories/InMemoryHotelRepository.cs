using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using System.Collections.Concurrent;

namespace HotelChatbot.Infrastructure.Repositories;

/// <summary>
/// In-Memory Repository für Hotels (für Development/Testing).
/// Für Production sollte eine Datenbank (SQL Server, PostgreSQL, etc.) verwendet werden.
/// </summary>
public class InMemoryHotelRepository : IHotelRepository
{
    private readonly ConcurrentDictionary<string, Hotel> _hotels = new();

    public Task<Hotel?> GetByIdAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        _hotels.TryGetValue(hotelId, out var hotel);
        return Task.FromResult(hotel);
    }

    public Task<Hotel?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        var hotel = _hotels.Values.FirstOrDefault(h => 
            h.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            h.AllowedDomains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase)));
        
        return Task.FromResult(hotel);
    }

    public Task<List<Hotel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_hotels.Values.ToList());
    }

    public Task AddAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        _hotels[hotel.HotelId] = hotel;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Hotel hotel, CancellationToken cancellationToken = default)
    {
        if (_hotels.ContainsKey(hotel.HotelId))
        {
            hotel.UpdatedAt = DateTime.UtcNow;
            _hotels[hotel.HotelId] = hotel;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        _hotels.TryRemove(hotelId, out _);
        return Task.CompletedTask;
    }
}
