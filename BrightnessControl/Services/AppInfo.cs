using System.Reflection;

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
    public const string Copyright = "© 2026 zura · imzura.com";

    /// <summary>Taken from the assembly so the csproj is the only place a release number is typed.</summary>
    public static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>e.g. "Brightness Control v1.3.0".</summary>
    public static string NameWithVersion => $"{Name} v{Version}";
}
