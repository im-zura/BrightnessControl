namespace BrightnessControl.Models;

/// <summary>
/// A display the app has switched off, plus everything needed to put it back exactly as it was.
/// Persisted, so a display is never stranded off because the app was closed or crashed while it
/// was detached.
/// </summary>
public sealed class DetachedDisplay
{
    /// <summary>Stable monitor id, matching <see cref="MonitorInfo.Id"/>.</summary>
    public string Id { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    /// <summary>GDI device name (e.g. "\\.\DISPLAY2") — what identifies the display path.</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>Whether this record is usable. The layout itself is not stored: Windows remembers
    /// each display's position and mode, and restoring the extend topology brings it back as it was.</summary>
    public bool IsRestorable => !string.IsNullOrEmpty(DeviceName);
}
