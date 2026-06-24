using HotelChatbot.Domain.Entities;

namespace HotelChatbot.Domain.Interfaces;

public interface IFeedbackRepository
{
    Task<List<Feedback>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Feedback?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Feedback> CreateAsync(Feedback feedback, CancellationToken cancellationToken = default);
    Task<Feedback?> UpdateAsync(Feedback feedback, CancellationToken cancellationToken = default);
    Task<Feedback?> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken = default);
}
