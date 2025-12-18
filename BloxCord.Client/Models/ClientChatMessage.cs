using BloxCord.Client.Helpers;

namespace BloxCord.Client.Models;

public class ClientChatMessage
{
    private const string OwnerUsername = "p0mp0mpur_1NN";

    public string? Id { get; init; }

    public required string Username { get; init; }

    public string? DisplayName { get; init; }

    public string? CountryCode { get; init; }

    public bool IsOwner => string.Equals(Username?.Trim(), OwnerUsername, StringComparison.OrdinalIgnoreCase);

    public string SenderLabel
    {
        get
        {
            var flag = FlagEmoji.FromCountryCode(CountryCode);
            var prefix = string.IsNullOrEmpty(flag) ? string.Empty : flag + " ";

            if (string.IsNullOrWhiteSpace(DisplayName))
                return prefix + Username;

            if (string.Equals(DisplayName.Trim(), Username, StringComparison.OrdinalIgnoreCase))
                return prefix + Username;

            return $"{prefix}{DisplayName} (@{Username})";
        }
    }

    public required string Content { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string JobId { get; init; }

    public long? UserId { get; init; }

    public string AvatarUrl { get; init; } = string.Empty;

    public string? ImageUrl { get; set; }

    public IReadOnlyList<string>? ImageUrls { get; init; }

    public IReadOnlyList<string>? EmojiImageUrls { get; init; }

    public bool IsSystemMessage { get; init; }

    public bool IsContinuation { get; init; }

    public string? RawContent { get; init; }

    public string? TranslatedContent { get; init; }

    public string? ReplyToId { get; init; }

    public string? ReplyPreview { get; init; }

    public IReadOnlyList<ReactionBadge>? ReactionBadges { get; init; }

    public DateTimeOffset? EditedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public Dictionary<string, ReactionBucket>? Reactions { get; init; }
}

public sealed class ReactionBucket
{
    public List<string> Usernames { get; init; } = new();
    public List<long> UserIds { get; init; } = new();
}

public sealed class ReactionBadge
{
    public required string Emoji { get; init; }
    public int Count { get; init; }
}
