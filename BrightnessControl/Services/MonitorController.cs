using System.Runtime.InteropServices;
using BrightnessControl.Native;

namespace BrightnessControl.Services;

internal sealed record PhysicalMonitorHandle(
    IntPtr Handle, string Description, int DisplayNumber, bool IsPrimary, string DeviceName, string DevicePath);

/// <summary>
/// Thin wrapper around the DDC/CI P/Invoke surface. Every native call that can block
/// (real hardware round-trip over the display cable) is wrapped with a timeout so a
/// slow/unresponsive monitor never freezes the caller.
/// </summary>
internal static class MonitorController
{
    private static readonly TimeSpan NativeCallTimeout = TimeSpan.FromMilliseconds(1500);

    private const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>Extracts the trailing number from a GDI device name like "\\.\DISPLAY1" to 1; 0 if none.</summary>
    public static int ParseDisplayNumber(string device)
    {
        if (string.IsNullOrEmpty(device))
            return 0;

        int i = device.Length;
        while (i > 0 && char.IsDigit(device[i - 1])) i--;
        return i < device.Length && int.TryParse(device[i..], out var n) ? n : 0;
    }

    public static List<PhysicalMonitorHandle> EnumeratePhysicalMonitors()
    {
        var result = new List<PhysicalMonitorHandle>();

        bool EnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            if (!Dxva2Interop.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                return true;

            var physicalMonitors = new PHYSICAL_MONITOR[count];
            if (!Dxva2Interop.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                return true;

            // Pull the Windows display number (the "1"/"2" shown in Settings > Display) and primary flag
            // from the device name, e.g. "\\.\DISPLAY1", so labels match what the user sees in Windows.
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            int displayNumber = 0;
            bool isPrimary = false;
            string deviceName = "";
            if (Dxva2Interop.GetMonitorInfo(hMonitor, ref mi))
            {
                isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                displayNumber = ParseDisplayNumber(mi.szDevice);
                deviceName = mi.szDevice;
            }

            // Stable per-monitor identity (survives power-cycling the display, unlike enumeration order).
            var devicePaths = DisplayDeviceInterop.MonitorDevicePaths(deviceName);

            for (int i = 0; i < physicalMonitors.Length; i++)
            {
                var pm = physicalMonitors[i];
                var devicePath = i < devicePaths.Count ? devicePaths[i] : "";
                result.Add(new PhysicalMonitorHandle(
                    pm.hPhysicalMonitor, pm.szPhysicalMonitorDescription, displayNumber, isPrimary, deviceName, devicePath));
            }

            return true;
        }

        try
        {
            Dxva2Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("monitor enumeration failed", ex);
        }

        return result;
    }

    public static void DestroyAll(IEnumerable<PhysicalMonitorHandle> handles)
    {
        var array = handles
            .Select(h => new PHYSICAL_MONITOR { hPhysicalMonitor = h.Handle, szPhysicalMonitorDescription = h.Description })
            .ToArray();

        if (array.Length == 0)
            return;

        try { Dxva2Interop.DestroyPhysicalMonitors((uint)array.Length, array); }
        catch (COMException) { /* handle already invalid, e.g. monitor unplugged */ }
        catch (Exception ex) { Log.Warn($"DestroyPhysicalMonitors failed: {ex.Message}"); }
    }

    // ---- Brightness / contrast -------------------------------------------------

    public static Task<(bool Success, uint Min, uint Current, uint Max)> TryGetBrightnessAsync(IntPtr handle) =>
        RunAsync(() =>
        {
            bool ok = Dxva2Interop.GetMonitorBrightness(handle, out uint min, out uint current, out uint max);
            return (ok, min, current, max);
        }, (false, 0u, 0u, 0u), NativeCallTimeout);

    public static Task<bool> TrySetBrightnessAsync(IntPtr handle, uint rawValue) =>
        RunAsync(() => Dxva2Interop.SetMonitorBrightness(handle, rawValue), false, NativeCallTimeout);

    public static Task<(bool Success, uint Min, uint Current, uint Max)> TryGetContrastAsync(IntPtr handle) =>
        RunAsync(() =>
        {
            bool ok = Dxva2Interop.GetMonitorContrast(handle, out uint min, out uint current, out uint max);
            return (ok, min, current, max);
        }, (false, 0u, 0u, 0u), NativeCallTimeout);

    public static Task<bool> TrySetContrastAsync(IntPtr handle, uint rawValue) =>
        RunAsync(() => Dxva2Interop.SetMonitorContrast(handle, rawValue), false, NativeCallTimeout);

    // ---- Timeout plumbing ------------------------------------------------------

    /// <summary>Runs a blocking native call off the caller's thread and gives up after <paramref name="timeout"/>.
    /// A monitor that never answers (asleep, DDC disabled) must not be able to hang the app.</summary>
    private static async Task<T> RunAsync<T>(Func<T> nativeCall, T fallback, TimeSpan timeout)
    {
        var task = Task.Run(() =>
        {
            try { return nativeCall(); }
            catch { return fallback; }
        });

        using var cts = new CancellationTokenSource();
        var completed = await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
        if (completed != task)
            return fallback;

        cts.Cancel(); // release the timer instead of leaving it to expire
        try { return await task.ConfigureAwait(false); }
        catch { return fallback; }
    }
}
