using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BloxCord.Client.Services;

internal static class LibreTranslateService
{
    private static readonly HttpClient Http = new();

    public static async Task<string?> TryTranslateAsync(
        string text,
        string baseUrl,
        string sourceLang,
        string targetLang,
        string? secret = null,
        int alternatives = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        if (string.IsNullOrWhiteSpace(targetLang))
            return null;

        try
        {
            var endpoint = baseUrl.TrimEnd('/') + "/translate";

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(text), "q");
            form.Add(new StringContent(string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim()), "source");
            form.Add(new StringContent(targetLang.Trim()), "target");
            form.Add(new StringContent("text"), "format");
            form.Add(new StringContent((alternatives <= 0 ? 3 : alternatives).ToString()), "alternatives");
            form.Add(new StringContent(string.Empty), "api_key");
            form.Add(new StringContent(secret ?? string.Empty), "secret");

            using var response = await Http.PostAsync(endpoint, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: cancellationToken);
            var translated = payload?.TranslatedText;
            if (string.IsNullOrWhiteSpace(translated))
                return null;

            return translated;
        }
        catch
        {
            return null;
        }
    }

    private sealed class LibreTranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; set; }
    }
}
