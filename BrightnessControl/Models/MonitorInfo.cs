using BrightnessControl.Native;

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

    // ---- Contrast (optional; not all monitors expose it over DDC/CI) ----
    public bool SupportsContrast { get; set; }
    public uint ContrastMin { get; set; }
    public uint ContrastCurrent { get; set; }
    public uint ContrastMax { get; set; }

    public int ContrastPercent =>
        ContrastMax > ContrastMin ? (int)Math.Round((ContrastCurrent - ContrastMin) * 100.0 / (ContrastMax - ContrastMin)) : 0;

    // ---- Color temperature (optional; preset-based over DDC/CI) ----
    public bool SupportsTemperature { get; set; }
    public MC_COLOR_TEMPERATURE Temperature { get; set; } = MC_COLOR_TEMPERATURE.Unknown;
}
