using System.Text.Json.Serialization;

namespace BloxCord.Api.Models;

public class TypingIndicatorPayload
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("usernames")]
    public IReadOnlyCollection<string> Usernames { get; set; }
        = Array.Empty<string>();
}
