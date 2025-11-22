using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BloxCord.Api.Models;

public class PostChatMessageRequest
{
    [Required]
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long? UserId { get; set; }
        = null;

    [Required]
    [JsonPropertyName("content")]
    [StringLength(500)]
    public string Content { get; set; } = string.Empty;
}
