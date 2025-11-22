using System.Text.Json.Serialization;

namespace BloxCord.Api.Models;

public class ChannelParticipant
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }
        = null;

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
        = null;
}
