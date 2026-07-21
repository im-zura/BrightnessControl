namespace BrightnessControl.Models;

public sealed class MonitorMeta
{
    public string Id { get; set; } = "";
    public string FriendlyName { get; set; } = "";
}

public sealed class IdleProfile
{
    /// <summary>Monitor id -> target brightness percent (0-100).</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public int PollingIntervalMs { get; set; } = 2000;
    public bool StartWithWindows { get; set; } = true;
    public IdleProfile IdleProfile { get; set; } = new();
    public List<MonitorMeta> Monitors { get; set; } = new();
    public List<GameProfile> GameProfiles { get; set; } = new();
}
