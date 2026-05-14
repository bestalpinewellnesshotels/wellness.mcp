using HotelChatbot.Domain.Entities;
using HotelChatbot.Domain.Interfaces;
using System.Collections.Concurrent;

namespace HotelChatbot.Infrastructure.Repositories;

/// <summary>
/// In-Memory Repository für Chat-Sessions.
/// </summary>
public class InMemoryChatSessionRepository : IChatSessionRepository
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _messages = new();

    public Task<ChatSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<List<ChatSession>> GetByHotelIdAsync(string hotelId, CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Values
            .Where(s => s.HotelId == hotelId)
            .ToList();
        
        return Task.FromResult(sessions);
    }

    public Task AddAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        _sessions[session.SessionId] = session;
        _messages[session.SessionId] = new List<ChatMessage>();
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        if (_sessions.ContainsKey(session.SessionId))
        {
            _sessions[session.SessionId] = session;
        }
        return Task.CompletedTask;
    }

    public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(message.SessionId, out var messages))
        {
            messages.Add(message);
        }
        else
        {
            _messages[message.SessionId] = new List<ChatMessage> { message };
        }
        
        return Task.CompletedTask;
    }

    public Task<List<ChatMessage>> GetSessionMessagesAsync(
        string sessionId, 
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(sessionId, out var messages))
        {
            return Task.FromResult(messages.OrderBy(m => m.Timestamp).ToList());
        }
        
        return Task.FromResult(new List<ChatMessage>());
    }
}
