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

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        // Clean up the value written by earlier pre-rebrand builds.
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
                key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps the registry in sync with the config value, e.g. after a manual reinstall
    /// where the config says enabled but the registry key is missing.</summary>
    public static void Reconcile(bool shouldBeEnabled)
    {
        if (shouldBeEnabled != IsRegistered())
            SetEnabled(shouldBeEnabled);
    }
}
