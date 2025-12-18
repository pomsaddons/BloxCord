using System.IO;
using System.Text.Json;

namespace BloxCord.Client.Services;

public class AppConfig
{
    public string BackendUrl { get; set; } = "https://rochat.pompompurin.tech";
    public string Username { get; set; } = string.Empty;
    public bool UseGradient { get; set; } = true;
    public string SolidColor { get; set; } = "#0F172A";
    public string GradientStart { get; set; } = "#0F172A";
    public string GradientEnd { get; set; } = "#334155";

    public List<string> MutedConversations { get; set; } = new();

    public string CountryCode { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = string.Empty;

    public string UserToken { get; set; } = string.Empty;

    public bool EnableE2eeDirectMessages { get; set; } = false;
}

public static class ConfigService
{
    private const string ConfigFileName = "config.json";
    private static AppConfig _currentConfig = new();

    public static AppConfig Current => _currentConfig;

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigFileName))
            {
                var json = File.ReadAllText(ConfigFileName);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    _currentConfig = config;
                }
            }
        }
        catch
        {
            // Ignore errors, use defaults
        }
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_currentConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFileName, json);
        }
        catch
        {
            // Ignore errors
        }
    }
}
