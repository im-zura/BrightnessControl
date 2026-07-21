using System.Runtime.InteropServices;
using BrightnessControl.Native;

namespace BrightnessControl.Services;

internal sealed record PhysicalMonitorHandle(IntPtr Handle, string Description, int DisplayNumber, bool IsPrimary, string DeviceName);

/// <summary>
/// Thin wrapper around the DDC/CI P/Invoke surface. Every native call that can block
/// (real hardware round-trip over the display cable) is wrapped with a timeout so a
/// slow/unresponsive monitor never freezes the caller.
/// </summary>
internal static class MonitorController
{
    private static readonly TimeSpan NativeCallTimeout = TimeSpan.FromMilliseconds(1500);

    private const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>Extracts the trailing number from a GDI device name like "\\.\DISPLAY1" → 1; 0 if none.</summary>
    private static int ParseDisplayNumber(string device)
    {
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

            // Pull the Windows display number (the "1"/"2" shown in Settings → Display) and primary flag
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

            foreach (var pm in physicalMonitors)
                result.Add(new PhysicalMonitorHandle(pm.hPhysicalMonitor, pm.szPhysicalMonitorDescription, displayNumber, isPrimary, deviceName));

            return true;
        }

        Dxva2Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero);
        return result;
    }

    public static void DestroyAll(IEnumerable<PhysicalMonitorHandle> handles)
    {
        var array = handles
            .Select(h => new PHYSICAL_MONITOR { hPhysicalMonitor = h.Handle, szPhysicalMonitorDescription = h.Description })
            .ToArray();

        if (array.Length > 0)
        {
            try { Dxva2Interop.DestroyPhysicalMonitors((uint)array.Length, array); }
            catch (COMException) { /* handle already invalid, e.g. monitor unplugged */ }
        }
    }

    public static async Task<(bool Success, uint Min, uint Current, uint Max)> TryGetBrightnessAsync(IntPtr handle)
    {
        var task = Task.Run(() =>
        {
            bool ok = Dxva2Interop.GetMonitorBrightness(handle, out uint min, out uint current, out uint max);
            return (ok, min, current, max);
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return (false, 0, 0, 0);

        try { return await task; }
        catch { return (false, 0, 0, 0); }
    }

    public static async Task<bool> TrySetBrightnessAsync(IntPtr handle, uint rawValue)
    {
        var task = Task.Run(() =>
        {
            try { return Dxva2Interop.SetMonitorBrightness(handle, rawValue); }
            catch { return false; }
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return false;

        try { return await task; }
        catch { return false; }
    }

    public static async Task<(bool Success, uint Min, uint Current, uint Max)> TryGetContrastAsync(IntPtr handle)
    {
        var task = Task.Run(() =>
        {
            bool ok = Dxva2Interop.GetMonitorContrast(handle, out uint min, out uint current, out uint max);
            return (ok, min, current, max);
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return (false, 0, 0, 0);

        try { return await task; }
        catch { return (false, 0, 0, 0); }
    }

    public static async Task<bool> TrySetContrastAsync(IntPtr handle, uint rawValue)
    {
        var task = Task.Run(() =>
        {
            try { return Dxva2Interop.SetMonitorContrast(handle, rawValue); }
            catch { return false; }
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return false;

        try { return await task; }
        catch { return false; }
    }

    public static async Task<(bool Success, MC_COLOR_TEMPERATURE Current)> TryGetColorTemperatureAsync(IntPtr handle)
    {
        var task = Task.Run(() =>
        {
            bool ok = Dxva2Interop.GetMonitorColorTemperature(handle, out var current);
            return (ok, current);
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return (false, MC_COLOR_TEMPERATURE.Unknown);

        try { return await task; }
        catch { return (false, MC_COLOR_TEMPERATURE.Unknown); }
    }

    public static async Task<bool> TrySetColorTemperatureAsync(IntPtr handle, MC_COLOR_TEMPERATURE value)
    {
        var task = Task.Run(() =>
        {
            try { return Dxva2Interop.SetMonitorColorTemperature(handle, value); }
            catch { return false; }
        });

        var completed = await Task.WhenAny(task, Task.Delay(NativeCallTimeout));
        if (completed != task)
            return false;

        try { return await task; }
        catch { return false; }
    }
}
