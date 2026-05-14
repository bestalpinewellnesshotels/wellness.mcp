namespace HotelChatbot.Domain.Interfaces;

/// <summary>
/// Repository Interface für Chat-Sessions.
/// </summary>
public interface IChatSessionRepository
{
    Task<Entities.ChatSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<List<Entities.ChatSession>> GetByHotelIdAsync(string hotelId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.ChatSession session, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.ChatSession session, CancellationToken cancellationToken = default);
    Task AddMessageAsync(Entities.ChatMessage message, CancellationToken cancellationToken = default);
    Task<List<Entities.ChatMessage>> GetSessionMessagesAsync(string sessionId, CancellationToken cancellationToken = default);
}
