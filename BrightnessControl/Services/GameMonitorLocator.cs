using System.Diagnostics;
using System.Runtime.InteropServices;
using BrightnessControl.Models;
using BrightnessControl.Native;

namespace BrightnessControl.Services;

/// <summary>
/// Works out which physical monitor a game is displayed on, so a game profile can dim only that
/// screen. Maps the game's top-level window to a monitor via MonitorFromWindow, then matches the
/// GDI device name back to our <see cref="MonitorInfo"/> list. Best-effort: returns null if the
/// window can't be found (e.g. a headless/console process), and the caller falls back to all monitors.
/// </summary>
internal static class GameMonitorLocator
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    public static async Task<string?> ResolveMonitorIdAsync(Process process, IReadOnlyList<MonitorInfo> monitors)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var hwnd = TryGetWindow(process);
            if (hwnd != IntPtr.Zero)
            {
                var id = MonitorIdForWindow(hwnd, monitors);
                if (id != null)
                    return id;
            }

            // The game's window often doesn't exist yet at the instant the process starts; retry briefly.
            await Task.Delay(RetryDelay);
        }

        return null;
    }

    private static IntPtr TryGetWindow(Process process)
    {
        try
        {
            process.Refresh();
            var main = process.MainWindowHandle;
            if (main != IntPtr.Zero && User32Interop.IsWindowVisible(main))
                return main;

            return FindTopLevelWindow((uint)process.Id);
        }
        catch
        {
            return IntPtr.Zero; // process exited / access denied
        }
    }

    /// <summary>Fallback for games whose MainWindowHandle stays 0 (e.g. borderless launchers):
    /// scan top-level visible windows for one owned by the target process.</summary>
    private static IntPtr FindTopLevelWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;

        User32Interop.EnumWindows((hWnd, _) =>
        {
            if (!User32Interop.IsWindowVisible(hWnd))
                return true;

            User32Interop.GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == pid)
            {
                found = hWnd;
                return false; // stop enumerating
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static string? MonitorIdForWindow(IntPtr hwnd, IReadOnlyList<MonitorInfo> monitors)
    {
        var hMonitor = User32Interop.MonitorFromWindow(hwnd, User32Interop.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return null;

        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Dxva2Interop.GetMonitorInfo(hMonitor, ref mi))
            return null;

        return monitors
            .FirstOrDefault(m => m.IsResponsive &&
                string.Equals(m.DeviceName, mi.szDevice, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}
