using System.Collections.Concurrent;
using System.Linq;

namespace BloxCord.Api.Models;

public class ChannelRecord
{
    private const int MaxHistoryEntries = 200;
    private readonly ConcurrentQueue<ChatMessage> _history = new();
    private readonly ConcurrentDictionary<string, ChannelParticipant> _participants = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _typingParticipants = new(StringComparer.OrdinalIgnoreCase);

    public ChannelRecord(string jobId, string createdBy, long? userId, string? avatarUrl)
    {
        JobId = jobId;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
        _participants.TryAdd(createdBy, new ChannelParticipant
        {
            Username = createdBy,
            UserId = userId,
            AvatarUrl = avatarUrl
        });
    }

    public string JobId { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<ChannelParticipant> GetParticipants()
        => _participants.Values.ToList();

    public IReadOnlyCollection<ChatMessage> GetHistory()
        => _history.ToList();

    public void AppendMessage(ChatMessage message)
    {
        _history.Enqueue(message);

        while (_history.Count > MaxHistoryEntries && _history.TryDequeue(out _))
        {
            // discard overflow
        }
    }

    public void AddParticipant(string username, long? userId, string? avatarUrl)
    {
        _participants.AddOrUpdate(username,
            _ => new ChannelParticipant { Username = username, UserId = userId, AvatarUrl = avatarUrl },
            (_, existing) =>
            {
                existing.UserId ??= userId;
                if (string.IsNullOrEmpty(existing.AvatarUrl) && !string.IsNullOrEmpty(avatarUrl))
                    existing.AvatarUrl = avatarUrl;
                return existing;
            });
    }

    public void RemoveParticipant(string username)
    {
        _participants.TryRemove(username, out _);
        _typingParticipants.TryRemove(username, out _);
    }

    public ChannelParticipant? GetParticipant(string username)
    {
        _participants.TryGetValue(username, out var participant);
        return participant;
    }

    public void SetTypingState(string username, bool isTyping)
    {
        if (isTyping)
            _typingParticipants.TryAdd(username, 0);
        else
            _typingParticipants.TryRemove(username, out _);
    }

    public IReadOnlyCollection<string> GetTypingParticipants()
        => _typingParticipants.Keys.ToList();
}
