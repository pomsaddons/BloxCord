using System.Text.Json.Serialization;

namespace BloxCord.Api.Models;

public class ChannelResponse
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("participants")]
    public IReadOnlyCollection<ChannelParticipant> Participants { get; set; }
        = Array.Empty<ChannelParticipant>();
}
