using System.Text.Json.Serialization;

namespace BloxCord.Client.Models;

public sealed class ChatMessageDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }
        = null;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
        = DateTimeOffset.UtcNow;

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; } = string.Empty;

    [JsonPropertyName("replyToId")]
    public string? ReplyToId { get; set; }

    [JsonPropertyName("editedAt")]
    public DateTimeOffset? EditedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("isSystem")]
    public bool? IsSystem { get; set; }

    [JsonPropertyName("reactions")]
    public Dictionary<string, ReactionBucketDto>? Reactions { get; set; }
}

public sealed class ReactionBucketDto
{
    [JsonPropertyName("usernames")]
    public List<string> Usernames { get; set; } = new();

    [JsonPropertyName("userIds")]
    public List<long> UserIds { get; set; } = new();
}

public sealed class PrivateMessageDto
{
    [JsonPropertyName("fromUserId")]
    public long FromUserId { get; set; }

    [JsonPropertyName("fromUsername")]
    public string FromUsername { get; set; } = string.Empty;

    [JsonPropertyName("toUserId")]
    public long ToUserId { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChannelParticipantDto
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }
        = null;

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; } = string.Empty;

    [JsonPropertyName("isTyping")]
    public bool IsTyping { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("dmPublicKey")]
    public string? DmPublicKey { get; set; }
}

public sealed class ChannelSnapshotDto
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("history")]
    public List<ChatMessageDto>? History { get; set; }
        = new();

    [JsonPropertyName("participants")]
    public List<ChannelParticipantDto>? Participants { get; set; }
        = new();

    [JsonPropertyName("pinnedMessageId")]
    public string? PinnedMessageId { get; set; }

    [JsonPropertyName("activePinVote")]
    public PinVoteStateDto? ActivePinVote { get; set; }

    [JsonPropertyName("activeKickVote")]
    public KickVoteStateDto? ActiveKickVote { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }
}

public sealed class PinVoteStateDto
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("voters")]
    public List<string> Voters { get; set; } = new();
}

public sealed class KickVoteStateDto
{
    [JsonPropertyName("targetUsername")]
    public string TargetUsername { get; set; } = string.Empty;

    [JsonPropertyName("voters")]
    public List<string> Voters { get; set; } = new();
}

public class ChannelJoinDto
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }
        = null;

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("dmPublicKey")]
    public string? DmPublicKey { get; set; }
}

public sealed class PostChatMessageDto : ChannelJoinDto
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class TypingIndicatorDto
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("usernames")]
    public IReadOnlyCollection<string> Usernames { get; set; }
        = Array.Empty<string>();
}

public sealed class TypingNotificationDto
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("isTyping")]
    public bool IsTyping { get; set; }
        = false;
}

public class GameDto
{
    [JsonPropertyName("placeId")]
    public long PlaceId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("serverCount")]
    public int ServerCount { get; set; }

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("servers")]
    public List<ServerDto> Servers { get; set; } = new();
}

public class ServerDto
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("avatarUrls")]
    public List<string> AvatarUrls { get; set; } = new();
}
