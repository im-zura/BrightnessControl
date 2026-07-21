using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BrightnessControl.Services;

/// <summary>Extracts a game's executable icon as a WPF ImageSource for the profile tiles, cached by path.</summary>
internal static class IconExtractor
{
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static System.Windows.Media.ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        if (Cache.TryGetValue(exePath, out var cached))
            return cached;

        System.Windows.Media.ImageSource? result = null;
        try
        {
            if (File.Exists(exePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    result = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    result.Freeze();
                }
            }
        }
        catch
        {
            // Inaccessible/locked exe (e.g. protected game): leave null, caller shows a placeholder.
        }

        Cache[exePath] = result;
        return result;
    }
}
