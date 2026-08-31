namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Repository Interface für Hotel-Entitäten.
/// </summary>
public interface IHotelRepository
{
    Task<Entities.Hotel?> GetByIdAsync(string hotelId, CancellationToken cancellationToken = default);
    Task<List<Entities.Hotel>> GetByIdsAsync(IEnumerable<string> hotelIds, CancellationToken cancellationToken = default);
    Task<Entities.Hotel?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default);
    Task<List<Entities.Hotel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Entities.Hotel hotel, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.Hotel hotel, CancellationToken cancellationToken = default);
    Task DeleteAsync(string hotelId, CancellationToken cancellationToken = default);
}
