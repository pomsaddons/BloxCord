using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BloxCord.Api.Services;

public class RobloxAvatarService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://thumbnails.roblox.com")
    };

    private readonly ConcurrentDictionary<long, string> _cache = new();

    public async Task<string?> TryGetHeadshotUrlAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;

        try
        {
            var response = await HttpClient.GetAsync($"/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<HeadshotResponse>(cancellationToken: cancellationToken);
            var imageUrl = payload?.Data?.FirstOrDefault()?.ImageUrl;

            if (!string.IsNullOrEmpty(imageUrl))
                _cache[userId] = imageUrl;

            return imageUrl;
        }
        catch
        {
            return null;
        }
    }

    private sealed class HeadshotResponse
    {
        [JsonPropertyName("data")]
        public List<HeadshotEntry> Data { get; set; } = new();
    }

    private sealed class HeadshotEntry
    {
        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
