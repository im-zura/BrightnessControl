namespace BrightnessControl.Models;

public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ProcessName { get; set; } = "";

    /// <summary>Full path to the game's executable, if known — used to show its real icon on the tile.
    /// May be null for profiles created from the running-process picker on a protected process.</summary>
    public string? ExePath { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Monitor id -> target brightness percent (0-100).</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();
}
