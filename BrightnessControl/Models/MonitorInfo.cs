namespace BrightnessControl.Models;

/// <summary>Runtime descriptor for a physical monitor detected via DDC/CI.</summary>
public sealed class MonitorInfo
{
    /// <summary>Stable key used in config. Derived from the monitor's device-interface path, so it
    /// survives powering the display off and on — see <c>MonitorIdentity</c>.</summary>
    public required string Id { get; init; }
    public required string FriendlyName { get; set; }

    /// <summary>GDI device name (e.g. "\\.\DISPLAY1") — used to match a game window's monitor.
    /// Unlike <see cref="Id"/> this can change when displays are added/removed.</summary>
    public string DeviceName { get; set; } = "";

    public bool IsPrimary { get; set; }

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

    // ---- Power ----

    /// <summary>Whether this display can be switched off. Any display except the primary can:
    /// it is taken off the desktop, the GPU stops driving it, and the panel powers down. A display
    /// that is off has no <see cref="MonitorInfo"/> at all — it is gone from the desktop — so there
    /// is no "is it on" state to carry here.</summary>
    public bool SupportsPower => !IsPrimary;
}
