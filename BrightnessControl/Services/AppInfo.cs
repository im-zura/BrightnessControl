namespace BrightnessControl.Services;

/// <summary>
/// Single source of truth for the app's public identity (name, author, version, links).
/// Referenced by the UI footer, window titles, tray tooltip, config folder, and startup key
/// so branding is defined once.
/// </summary>
internal static class AppInfo
{
    public const string Name = "Brightness Control";
    public const string Brand = "IMZURA";
    public const string Author = "zura";
    public const string Site = "imzura.com";
    public const string SiteUrl = "https://imzura.com";
    public const string Version = "1.1.0";
    public const string Copyright = "© 2026 zura · imzura.com";

    /// <summary>e.g. "Brightness Control v1.1.0".</summary>
    public static string NameWithVersion => $"{Name} v{Version}";
}
