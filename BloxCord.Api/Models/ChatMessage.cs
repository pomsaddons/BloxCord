using System.Text.Json.Serialization;

namespace BloxCord.Api.Models;

public class ChatMessage
{
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
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
        = null;
}
