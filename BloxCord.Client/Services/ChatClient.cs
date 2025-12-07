using System.Net.Http;
using System.Net.Http.Json;
using SocketIOClient;
using BloxCord.Client.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BloxCord.Client.Services;

public sealed class ChatClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private SocketIOClient.SocketIO? _socket;
    private string? _jobId;
    private string? _username;
    private long? _userId;

    public ChatClient(string backendUrl)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
            throw new ArgumentException("Backend URL is required", nameof(backendUrl));

        if (!backendUrl.EndsWith('/'))
            backendUrl += "/";

        BackendUrl = backendUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BackendUrl + "/")
        };
    }

    public string BackendUrl { get; }

    public event EventHandler<ChatMessageDto>? MessageReceived;
    public event EventHandler<List<ChannelParticipantDto>>? ParticipantsChanged;
    public event EventHandler<List<ChatMessageDto>>? HistoryReceived;
    public event EventHandler<TypingIndicatorDto>? TypingIndicatorReceived;

    public async Task ConnectAsync(string username, string jobId, long? userId, long? placeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        _username = username;
        _jobId = jobId;
        _userId = userId;

        _socket = new SocketIOClient.SocketIO(BackendUrl, new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionDelay = 1000,
            ReconnectionAttempts = int.MaxValue
        });

        _socket.On("receiveMessage", response =>
        {
            var message = response.GetValue<ChatMessageDto>();
            MessageReceived?.Invoke(this, message);
        });

        _socket.On("participantsChanged", response =>
        {
            var data = response.GetValue<ParticipantsChangedDto>();
            ParticipantsChanged?.Invoke(this, data.Participants);
        });

        _socket.On("channelSnapshot", response =>
        {
            var snapshot = response.GetValue<ChannelSnapshotDto>();
            HistoryReceived?.Invoke(this, snapshot.History ?? new List<ChatMessageDto>());
            ParticipantsChanged?.Invoke(this, snapshot.Participants ?? new List<ChannelParticipantDto>());
        });

        _socket.On("typingIndicator", response =>
        {
            var payload = response.GetValue<TypingIndicatorDto>();
            TypingIndicatorReceived?.Invoke(this, payload);
        });

        _socket.On("gamesList", response =>
        {
            try
            {
                var games = response.GetValue<List<GameDto>>();
                if (games != null)
                {
                    GamesListReceived?.Invoke(this, games);
                }
            }
            catch
            {
                // Ignore deserialization errors
            }
        });

        _socket.OnReconnected += async (sender, e) =>
        {
            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_jobId))
            {
                // Wait a bit to ensure socket is ready
                await Task.Delay(500);
                await _socket.EmitAsync("joinChannel", new
                {
                    jobId = _jobId,
                    username = _username,
                    userId = _userId
                });
            }
        };

        await _socket.ConnectAsync();

        await _socket.EmitAsync("joinChannel", new
        {
            jobId = jobId,
            username = username,
            userId = userId,
            placeId = placeId
        });
    }

    public async Task GetGamesAsync()
    {
        if (_socket is null || !_socket.Connected)
        {
            _socket = new SocketIOClient.SocketIO(BackendUrl, new SocketIOOptions
            {
                Reconnection = true,
                ReconnectionDelay = 1000,
                ReconnectionAttempts = int.MaxValue
            });
            _socket.On("gamesList", response =>
            {
                var games = response.GetValue<List<GameDto>>();
                GamesListReceived?.Invoke(this, games);
            });
            await _socket.ConnectAsync();
        }
        await _socket.EmitAsync("getGames");
    }

    public event EventHandler<List<GameDto>>? GamesListReceived;

    public async Task SendAsync(string content, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("sendMessage", new
        {
            jobId = _jobId,
            username = _username,
            userId = _userId,
            content = content
        });
    }

    public async Task NotifyTypingAsync(bool isTyping, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            return;

        await _socket.EmitAsync("notifyTyping", new
        {
            jobId = _jobId,
            username = _username,
            isTyping = isTyping
        });
    }

    private async Task CreateChannelAsync(string username, string jobId, long? userId, CancellationToken cancellationToken)
    {
        // No longer needed with Socket.IO implementation as channel is created on join
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            await _socket.DisconnectAsync();
            _socket.Dispose();
            _socket = null;
        }
        _httpClient.Dispose();
    }

    private class ParticipantsChangedDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("participants")]
        public List<ChannelParticipantDto> Participants { get; set; } = new();
    }
}
