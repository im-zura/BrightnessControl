using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace BrightnessControl.Services;

/// <summary>
/// Reads the user's live Windows accent color and injects it into the app's theme resources so the
/// UI matches the system (the way the Start menu does). Falls back to the Win11 default blue.
/// </summary>
internal static class AccentColorService
{
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>Overwrites AccentBrush / AccentSoftBrush / AccentTextBrush in the app resources.
    /// Call once at startup before any window is built.</summary>
    public static void Apply()
    {
        var accent = ReadSystemAccent() ?? FallbackAccent;

        // On dark backgrounds the raw accent is often too dark for text/rings; Windows lightens it.
        var accentLight = Blend(accent, Colors.White, 0.4);
        var accentSoft = Color.FromArgb(0x33, accent.R, accent.G, accent.B);

        var res = Application.Current.Resources;
        res["AccentColor"] = accent;
        res["AccentBrush"] = Freeze(new SolidColorBrush(accent));
        res["AccentLightBrush"] = Freeze(new SolidColorBrush(accentLight));
        res["AccentTextBrush"] = Freeze(new SolidColorBrush(accentLight));
        res["AccentSoftBrush"] = Freeze(new SolidColorBrush(accentSoft));
    }

    private static Color? ReadSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                // Stored as 0xAABBGGRR (ABGR). We only need the RGB channels.
                byte r = (byte)(raw & 0xFF);
                byte g = (byte)((raw >> 8) & 0xFF);
                byte b = (byte)((raw >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch
        {
            // Registry unavailable / unexpected value: fall back to default.
        }

        return null;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
