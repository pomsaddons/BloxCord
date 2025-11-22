using System.Collections.Concurrent;
using BloxCord.Api.Models;

namespace BloxCord.Api.Services;

public class ChannelRegistry
{
    private readonly ConcurrentDictionary<string, ChannelRecord> _channels = new(StringComparer.OrdinalIgnoreCase);

    public ChannelRecord CreateOrGetChannel(string jobId, string username, long? userId, string? avatarUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var channel = _channels.GetOrAdd(jobId, _ => new ChannelRecord(jobId, username, userId, avatarUrl));
        channel.AddParticipant(username, userId, avatarUrl);
        return channel;
    }

    public bool TryGetChannel(string jobId, out ChannelRecord? channel)
    {
        channel = null;
        if (string.IsNullOrWhiteSpace(jobId))
            return false;

        if (_channels.TryGetValue(jobId, out var value))
        {
            channel = value;
            return true;
        }

        return false;
    }

    public IReadOnlyCollection<ChannelParticipant> GetParticipants(string jobId)
    {
        return TryGetChannel(jobId, out var channel)
            ? channel!.GetParticipants()
            : Array.Empty<ChannelParticipant>();
    }

    public void RemoveParticipant(string jobId, string username)
    {
        if (TryGetChannel(jobId, out var channel))
            channel!.RemoveParticipant(username);
    }

    public ChannelParticipant? GetParticipant(string jobId, string username)
    {
        return TryGetChannel(jobId, out var channel)
            ? channel!.GetParticipant(username)
            : null;
    }

    public IReadOnlyCollection<string> GetTypingParticipants(string jobId)
    {
        return TryGetChannel(jobId, out var channel)
            ? channel!.GetTypingParticipants()
            : Array.Empty<string>();
    }

    public void SetTypingState(string jobId, string username, bool isTyping)
    {
        if (TryGetChannel(jobId, out var channel))
            channel!.SetTypingState(username, isTyping);
    }
}
