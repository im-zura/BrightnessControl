using System.Text.Json.Serialization;

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

    /// <summary>Target brightness percent (0-100) applied to whichever monitor the game runs on.
    /// Null on profiles saved before per-monitor game targeting existed — migrated from the old
    /// <see cref="MonitorBrightness"/> average via <see cref="EffectiveGameBrightness"/>.</summary>
    public int? GameBrightness { get; set; }

    /// <summary>Legacy per-monitor targets (monitor id -> percent). Kept only to migrate old configs.</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();

    /// <summary>The brightness to apply, resolving legacy per-monitor profiles to a single value.</summary>
    [JsonIgnore]
    public int EffectiveGameBrightness =>
        GameBrightness ?? (MonitorBrightness.Count > 0
            ? (int)Math.Round(MonitorBrightness.Values.Average())
            : 50);
}
