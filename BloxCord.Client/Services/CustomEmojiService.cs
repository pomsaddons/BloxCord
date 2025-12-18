using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BloxCord.Client.Services;

internal static class CustomEmojiService
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, long>? _emojiMap;

    public static void Reload()
    {
        lock (Sync)
        {
            _emojiMap = LoadEmojiMap();
        }
    }

    private static IReadOnlyDictionary<string, long> GetEmojiMap()
    {
        var map = _emojiMap;
        if (map is not null) return map;

        lock (Sync)
        {
            return _emojiMap ??= LoadEmojiMap();
        }
    }

    public static string ExpandToRbxassetIds(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Direct form: :rbx123456:
        text = Regex.Replace(text, @":rbx(?<id>\d{1,20}):", match => $"emoji://{match.Groups["id"].Value}");

        var map = GetEmojiMap();
        if (map.Count == 0)
            return text;

        // Named form: :some_name:
        return Regex.Replace(text, @":(?<name>[A-Za-z0-9_]{1,32}):", match =>
        {
            var name = match.Groups["name"].Value;
            return map.TryGetValue(name, out var assetId)
                ? $"emoji://{assetId}"
                : match.Value;
        });
    }

    private static IReadOnlyDictionary<string, long> LoadEmojiMap()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "RoChat");
            var filePath = Path.Combine(folder, "custom_emojis.json");

            if (!File.Exists(filePath))
                return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            return parsed is null
                ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, long>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
