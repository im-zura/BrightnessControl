using Microsoft.Win32;

namespace BrightnessControl.Services;

/// <summary>Registers/unregisters the app to launch at Windows login via the HKCU Run key (no elevation).</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BrightnessControl";
    private const string LegacyValueName = "BrightnessControl";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>The command line currently stored in the Run key, or null when there is none.</summary>
    private static string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    private static string? CurrentCommand()
    {
        var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        return exePath is null ? null : $"\"{exePath}\"";
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        // Clean up the value written by earlier pre-rebrand builds.
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
        {
            var command = CurrentCommand();
            if (command != null)
                key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps the registry in sync with the config value — including after the app moves
    /// (installed over a build run from a different folder), where a key that merely exists would
    /// still be launching the old executable at every login.</summary>
    public static void Reconcile(bool shouldBeEnabled)
    {
        var registered = RegisteredCommand();

        if (!shouldBeEnabled)
        {
            if (registered != null)
                SetEnabled(false);
            return;
        }

        var current = CurrentCommand();
        if (registered == null || (current != null && !string.Equals(registered, current, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info($"startup entry updated: {registered ?? "(none)"} → {current}");
            SetEnabled(true);
        }
    }
}
