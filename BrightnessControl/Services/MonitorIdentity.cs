using System.Text;

namespace BrightnessControl.Services;

/// <summary>
/// Builds the stable key a monitor is known by in config. Enumeration order is not stable — a
/// display that is powered off drops out and the remaining ones shift up — so ids derived from it
/// silently re-point saved brightness at the wrong screen. The device-interface path does not shift.
/// </summary>
internal static class MonitorIdentity
{
    public const string LegacyPrefix = "monitor-";

    /// <summary>Best available id, in descending order of stability:
    /// device-interface path → description + Windows display number → enumeration index (legacy).</summary>
    public static string Resolve(string devicePath, string description, int displayNumber, int index)
    {
        var fromPath = FromDevicePath(devicePath);
        if (fromPath != null)
            return fromPath;

        var slug = Slug(description);
        if (slug.Length > 0 && displayNumber > 0)
            return $"mon-{slug}-{displayNumber}";

        return $"{LegacyPrefix}{index}";
    }

    /// <summary>"\\?\DISPLAY#GSM5B09#5&amp;1a2b&amp;0&amp;UID4353#{e6f07b5f-…}" → "mon-gsm5b09-5-1a2b-0-uid4353".
    /// Null when the path is missing or unusable.</summary>
    public static string? FromDevicePath(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

        var path = devicePath;

        // Drop the "\\?\" / "\\.\" prefix and the trailing device-interface class GUID.
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var firstHash = path.IndexOf('#');
            path = firstHash >= 0 ? path[(firstHash + 1)..] : path.TrimStart('\\', '?', '.');
        }

        var guidStart = path.IndexOf("#{", StringComparison.Ordinal);
        if (guidStart >= 0)
            path = path[..guidStart];

        var slug = Slug(path);
        return slug.Length > 0 ? $"mon-{slug}" : null;
    }

    /// <summary>True for ids produced by versions that keyed monitors by enumeration index.</summary>
    public static bool IsLegacy(string id) => id.StartsWith(LegacyPrefix, StringComparison.Ordinal);

    /// <summary>Lowercase, alphanumeric, single-dash-separated — safe as a JSON key and readable in config.</summary>
    private static string Slug(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        bool lastWasDash = true; // suppress a leading dash

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }
}
