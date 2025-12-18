using System.Text.Json.Serialization;

namespace BloxCord.Client.Models;

public class BannerDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("cta")]
    public BannerCtaDto? Cta { get; set; }

    [JsonPropertyName("dismissible")]
    public bool Dismissible { get; set; }

    [JsonPropertyName("colors")]
    public BannerColorsDto? Colors { get; set; }

    // Optional version targeting
    // - If omitted, banner is eligible for all versions.
    // - versions: exact matches against app version string/semver.
    // - minVersion/maxVersion: inclusive semver bounds.

    [JsonPropertyName("versions")]
    public string[]? Versions { get; set; }

    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; set; }

    [JsonPropertyName("maxVersion")]
    public string? MaxVersion { get; set; }
}

public class BannerCtaDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class BannerColorsDto
{
    [JsonPropertyName("background")]
    public string Background { get; set; } = string.Empty;

    [JsonPropertyName("border")]
    public string Border { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("buttonBackground")]
    public string ButtonBackground { get; set; } = string.Empty;

    [JsonPropertyName("buttonText")]
    public string ButtonText { get; set; } = string.Empty;
}
