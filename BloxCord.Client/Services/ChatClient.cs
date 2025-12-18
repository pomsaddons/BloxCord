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
    private string? _token;

    private string? _countryCode;
    private string? _preferredLanguage;
    private string? _dmPublicKey;

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
    public event EventHandler<ChatMessageDto>? MessageUpdated;
    public event EventHandler<List<ChannelParticipantDto>>? ParticipantsChanged;
    public event EventHandler<List<ChatMessageDto>>? HistoryReceived;
    public event EventHandler<TypingIndicatorDto>? TypingIndicatorReceived;
    public event EventHandler<PrivateMessageDto>? PrivateMessageReceived;
    public event EventHandler<List<ChannelParticipantDto>>? SearchResultsReceived;
    public event EventHandler<PinVoteUpdateDto>? PinVoteStateReceived;
    public event EventHandler<KickVoteUpdateDto>? KickVoteStateReceived;
    public event EventHandler<PinnedMessageChangedDto>? PinnedMessageChanged;
    public event EventHandler<KickedDto>? Kicked;
    public event EventHandler<LanguageChangedDto>? LanguageChanged;
    public event EventHandler<LanguageVoteStateDto>? LanguageVoteStateReceived;
    public event EventHandler<TokenMintedDto>? TokenMinted;
    public event EventHandler<BannedDto>? Banned;
    public event EventHandler<AuthFailedDto>? AuthFailed;

    public async Task ConnectAsync(string username, string jobId, long? userId, long? placeId, string? countryCode = null, string? preferredLanguage = null, string? dmPublicKey = null, string? token = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        _username = username;
        _jobId = jobId;
        _userId = userId;
        _token = token;

        _countryCode = countryCode;
        _preferredLanguage = preferredLanguage;
        _dmPublicKey = dmPublicKey;

        _socket = new SocketIOClient.SocketIO(BackendUrl, new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionDelay = 1000,
            ReconnectionAttempts = int.MaxValue
        });

        _socket.On("searchResults", response =>
        {
            var results = response.GetValue<List<ChannelParticipantDto>>();
            SearchResultsReceived?.Invoke(this, results);
        });

        _socket.On("receiveMessage", response =>
        {
            var message = response.GetValue<ChatMessageDto>();
            MessageReceived?.Invoke(this, message);
        });

        _socket.On("messageUpdated", response =>
        {
            var message = response.GetValue<ChatMessageDto>();
            MessageUpdated?.Invoke(this, message);
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

            if (!string.IsNullOrWhiteSpace(snapshot.LanguageCode))
            {
                LanguageChanged?.Invoke(this, new LanguageChangedDto { JobId = snapshot.JobId, LanguageCode = snapshot.LanguageCode! });
            }

            if (!string.IsNullOrWhiteSpace(snapshot.PinnedMessageId))
            {
                PinnedMessageChanged?.Invoke(this, new PinnedMessageChangedDto { JobId = snapshot.JobId, PinnedMessageId = snapshot.PinnedMessageId });
            }
        });

        _socket.On("languageChanged", response =>
        {
            var payload = response.GetValue<LanguageChangedDto>();
            LanguageChanged?.Invoke(this, payload);
        });

        _socket.On("languageVoteState", response =>
        {
            var payload = response.GetValue<LanguageVoteStateDto>();
            LanguageVoteStateReceived?.Invoke(this, payload);
        });

        _socket.On("pinVoteState", response =>
        {
            var payload = response.GetValue<PinVoteUpdateDto>();
            PinVoteStateReceived?.Invoke(this, payload);
        });

        _socket.On("pinnedMessageChanged", response =>
        {
            var payload = response.GetValue<PinnedMessageChangedDto>();
            PinnedMessageChanged?.Invoke(this, payload);
        });

        _socket.On("kickVoteState", response =>
        {
            var payload = response.GetValue<KickVoteUpdateDto>();
            KickVoteStateReceived?.Invoke(this, payload);
        });

        _socket.On("kicked", response =>
        {
            var payload = response.GetValue<KickedDto>();
            Kicked?.Invoke(this, payload);
        });

        _socket.On("typingIndicator", response =>
        {
            var payload = response.GetValue<TypingIndicatorDto>();
            TypingIndicatorReceived?.Invoke(this, payload);
        });

        _socket.On("receivePrivateMessage", response =>
        {
            var message = response.GetValue<PrivateMessageDto>();
            PrivateMessageReceived?.Invoke(this, message);
        });

        _socket.On("tokenMinted", response =>
        {
            var payload = response.GetValue<TokenMintedDto>();
            TokenMinted?.Invoke(this, payload);
        });

        _socket.On("banned", response =>
        {
            var payload = response.GetValue<BannedDto>();
            Banned?.Invoke(this, payload);
        });

        _socket.On("authFailed", response =>
        {
            var payload = response.GetValue<AuthFailedDto>();
            AuthFailed?.Invoke(this, payload);
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
                    userId = _userId,
                    countryCode = _countryCode,
                    preferredLanguage = _preferredLanguage,
                    dmPublicKey = _dmPublicKey,
                    token = _token
                });
            }
        };

        await _socket.ConnectAsync();

        await _socket.EmitAsync("joinChannel", new
        {
            jobId = jobId,
            username = username,
            userId = userId,
            placeId = placeId,
            countryCode = _countryCode,
            preferredLanguage = _preferredLanguage,
            dmPublicKey = _dmPublicKey,
            token = _token
        });
    }

    public void SetToken(string? token)
    {
        _token = token;
    }

    public async Task MintTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("mintToken");
    }

    public async Task VoteLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            return;

        await _socket.EmitAsync("voteLanguage", new
        {
            jobId = _jobId,
            username = _username,
            languageCode
        });
    }

    public async Task UpdatePresenceAsync(string? countryCode = null, string? preferredLanguage = null, string? dmPublicKey = null, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            return;

        if (countryCode != null) _countryCode = countryCode;
        if (preferredLanguage != null) _preferredLanguage = preferredLanguage;
        if (dmPublicKey != null) _dmPublicKey = dmPublicKey;

        await _socket.EmitAsync("updatePresence", new
        {
            jobId = _jobId,
            username = _username,
            countryCode = _countryCode,
            preferredLanguage = _preferredLanguage,
            dmPublicKey = _dmPublicKey
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
            content = content,
            token = _token
        });
    }

    public async Task SendReplyAsync(string content, string replyToId, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("sendMessage", new
        {
            jobId = _jobId,
            username = _username,
            userId = _userId,
            content,
            replyToId,
            token = _token
        });
    }

    public async Task EditMessageAsync(string messageId, string newContent, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("editMessage", new
        {
            jobId = _jobId,
            messageId,
            username = _username,
            userId = _userId,
            content = newContent,
            token = _token
        });
    }

    public async Task DeleteMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("deleteMessage", new
        {
            jobId = _jobId,
            messageId,
            username = _username,
            userId = _userId,
            token = _token
        });
    }

    public async Task AddReactionAsync(string messageId, string emoji, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("addReaction", new
        {
            jobId = _jobId,
            messageId,
            emoji,
            username = _username,
            userId = _userId
        });
    }

    public async Task RemoveReactionAsync(string messageId, string emoji, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("removeReaction", new
        {
            jobId = _jobId,
            messageId,
            emoji,
            username = _username,
            userId = _userId
        });
    }

    public async Task VotePinAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("votePin", new
        {
            jobId = _jobId,
            messageId,
            username = _username
        });
    }

    public async Task VoteKickAsync(string targetUsername, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _jobId is null || _username is null)
            throw new InvalidOperationException("Client is not connected");

        await _socket.EmitAsync("voteKick", new
        {
            jobId = _jobId,
            targetUsername,
            username = _username
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

    public async Task SendPrivateMessageAsync(long toUserId, string content, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !_socket.Connected || _username is null || _userId is null)
            return;

        await _socket.EmitAsync("sendPrivateMessage", new
        {
            toUserId = toUserId,
            content = content,
            fromUsername = _username,
            fromUserId = _userId
        });
    }

    public async Task SearchUsers(string query)
    {
        if (_socket is null || !_socket.Connected)
            return;

        await _socket.EmitAsync("searchUsers", query);
    }

    public async Task SendToChannelAsync(string jobId, string content)
    {
        if (_socket is null || !_socket.Connected || _username is null) return;
        
        await _socket.EmitAsync("sendMessage", new
        {
            jobId = jobId,
            username = _username,
            userId = _userId,
            content = content,
            token = _token
        });
    }

    public sealed class TokenMintedDto
    {
        [JsonPropertyName("userId")]
        public long UserId { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    public sealed class BannedDto
    {
        [JsonPropertyName("userId")]
        public long? UserId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("appealUrl")]
        public string? AppealUrl { get; set; }
    }

    public sealed class AuthFailedDto
    {
        [JsonPropertyName("userId")]
        public long? UserId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    public sealed class LanguageChangedDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = string.Empty;
    }

    public sealed class LanguageVoteStateDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = string.Empty;

        [JsonPropertyName("votes")]
        public Dictionary<string, List<string>> Votes { get; set; } = new();
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

    public sealed class PinVoteUpdateDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("pinnedMessageId")]
        public string? PinnedMessageId { get; set; }

        [JsonPropertyName("activePinVote")]
        public PinVoteStateDto? ActivePinVote { get; set; }
    }

    public sealed class PinnedMessageChangedDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("pinnedMessageId")]
        public string? PinnedMessageId { get; set; }
    }

    public sealed class KickVoteUpdateDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("activeKickVote")]
        public KickVoteStateDto? ActiveKickVote { get; set; }
    }

    public sealed class KickedDto
    {
        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
