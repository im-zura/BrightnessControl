namespace BrightnessControl.Models;

/// <summary>Runtime descriptor for a physical monitor detected via DDC/CI.</summary>
public sealed class MonitorInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public uint Min { get; set; }
    public uint Current { get; set; }
    public uint Max { get; set; }
    public bool IsResponsive { get; set; } = true;

    public int CurrentPercent => Max > Min ? (int)Math.Round((Current - Min) * 100.0 / (Max - Min)) : 0;
}
