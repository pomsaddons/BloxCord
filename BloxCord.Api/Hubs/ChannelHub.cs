using Microsoft.AspNetCore.SignalR;
using BloxCord.Api.Models;
using BloxCord.Api.Services;

namespace BloxCord.Api.Hubs;

public class ChannelHub(ChannelRegistry registry, RobloxAvatarService avatarService, ILogger<ChannelHub> logger) : Hub
{
    private readonly ChannelRegistry _registry = registry;
    private readonly RobloxAvatarService _avatarService = avatarService;
    private readonly ILogger<ChannelHub> _logger = logger;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("jobId", out var jobIdObj) && Context.Items.TryGetValue("username", out var usernameObj))
        {
            string jobId = jobIdObj?.ToString() ?? string.Empty;
            string username = usernameObj?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(jobId) && !string.IsNullOrEmpty(username))
            {
                _registry.RemoveParticipant(jobId, username);
                _registry.SetTypingState(jobId, username, false);

                await Clients.Group(jobId).SendAsync("ParticipantsChanged", jobId, _registry.GetParticipants(jobId));
                await Clients.Group(jobId).SendAsync("TypingIndicator", new TypingIndicatorPayload
                {
                    JobId = jobId,
                    Usernames = _registry.GetTypingParticipants(jobId)
                });
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChannel(ChannelJoinRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username);

        string? avatarUrl = null;
        if (request.UserId.HasValue)
            avatarUrl = await _avatarService.TryGetHeadshotUrlAsync(request.UserId.Value);

        var channel = _registry.CreateOrGetChannel(request.JobId, request.Username, request.UserId, avatarUrl);

        Context.Items["jobId"] = channel.JobId;
        Context.Items["username"] = request.Username;
        Context.Items["userId"] = request.UserId;

        await Groups.AddToGroupAsync(Context.ConnectionId, channel.JobId);

        var participants = channel.GetParticipants();
        
        await Clients.Caller.SendAsync("ChannelSnapshot", new ChannelSnapshot
        {
            JobId = channel.JobId,
            CreatedAt = channel.CreatedAt,
            CreatedBy = channel.CreatedBy,
            History = channel.GetHistory(),
            Participants = participants
        });

        await Clients.Group(channel.JobId).SendAsync("ParticipantsChanged", channel.JobId, participants);
    }

    public async Task SendMessage(PostChatMessageRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        if (!_registry.TryGetChannel(request.JobId, out var channel) || channel is null)
            throw new HubException("ChannelNotFound");

        var participant = _registry.GetParticipant(request.JobId, request.Username);
        long? userId = request.UserId ?? participant?.UserId;

        var message = new ChatMessage
        {
            JobId = request.JobId,
            Username = request.Username,
            UserId = userId,
            Content = request.Content,
            Timestamp = DateTimeOffset.UtcNow,
            AvatarUrl = participant?.AvatarUrl
        };

        channel.AppendMessage(message);

        await Clients.Group(request.JobId).SendAsync("ReceiveMessage", message);
    }

    public async Task NotifyTyping(TypingNotification notification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification.Username);

        _registry.SetTypingState(notification.JobId, notification.Username, notification.IsTyping);

        await Clients.Group(notification.JobId).SendAsync("TypingIndicator", new TypingIndicatorPayload
        {
            JobId = notification.JobId,
            Usernames = _registry.GetTypingParticipants(notification.JobId)
        });
    }
}
