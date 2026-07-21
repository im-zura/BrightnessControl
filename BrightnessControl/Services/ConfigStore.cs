using System.IO;
using System.Text.Json;
using BrightnessControl.Models;

namespace BrightnessControl.Services;

internal static class ConfigStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                    return config;
            }
        }
        catch
        {
            // Corrupt or unreadable config file: fall back to defaults below rather than crash.
        }

        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        var tmpPath = ConfigPath + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(tmpPath, json);

        if (File.Exists(ConfigPath))
            File.Replace(tmpPath, ConfigPath, null);
        else
            File.Move(tmpPath, ConfigPath);
    }
}
