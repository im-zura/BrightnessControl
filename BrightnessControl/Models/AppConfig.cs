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

/// <summary>Day/night auto-brightness. Each block holds per-monitor targets, applied whenever no
/// tracked game is running. Day runs from DayStart until NightStart; night wraps around midnight.</summary>
public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }
    public string DayStart { get; set; } = "08:00";
    public string NightStart { get; set; } = "22:00";
    public Dictionary<string, int> DayBrightness { get; set; } = new();
    public Dictionary<string, int> NightBrightness { get; set; } = new();
}

/// <summary>A global hotkey as Win32 modifier flags (MOD_*) + virtual-key code.</summary>
public sealed class HotkeyCombo
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }
}

public sealed class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public int Step { get; set; } = 10;
    // Defaults: Ctrl+Alt+Up (increase) / Ctrl+Alt+Down (decrease). MOD_ALT=1, MOD_CONTROL=2; VK_UP=0x26, VK_DOWN=0x28.
    public HotkeyCombo Up { get; set; } = new() { Modifiers = 0x0003, Key = 0x26 };
    public HotkeyCombo Down { get; set; } = new() { Modifiers = 0x0003, Key = 0x28 };
}

public sealed class AppConfig
{
    public int Version { get; set; } = 2;
    public int PollingIntervalMs { get; set; } = 2000;
    public bool StartWithWindows { get; set; } = true;
    public IdleProfile IdleProfile { get; set; } = new();
    public ScheduleSettings Schedule { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public bool TrayScrollEnabled { get; set; } = true;
    public int TrayScrollStep { get; set; } = 5;
    public List<MonitorMeta> Monitors { get; set; } = new();
    public List<GameProfile> GameProfiles { get; set; } = new();

    /// <summary>Displays currently switched off, with the mode needed to restore them. Persisted so
    /// a screen is never stranded off because the app closed while it was.</summary>
    public List<DetachedDisplay> DetachedDisplays { get; set; } = new();
}
