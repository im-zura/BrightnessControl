using System.Runtime.InteropServices;

namespace BrightnessControl.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAY_DEVICE
{
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;
    public uint StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;
}

/// <summary>
/// Resolves the stable device-interface path of the monitor attached to a GDI display
/// (e.g. "\\.\DISPLAY2" -> "\\?\DISPLAY#GSM5B09#5&amp;1a2b3c4d&amp;0&amp;UID4353#{e6f07b5f-…}").
/// That path survives powering the monitor off and on, and reboots, as long as it stays on the
/// same port — unlike enumeration order, which does not.
/// </summary>
internal static class DisplayDeviceInterop
{
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
    private const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    /// <summary>Device-interface paths of the monitors attached to a GDI display, in enumeration
    /// order. Usually one entry; more only for exotic clone/daisy-chain setups. Empty on failure.</summary>
    public static List<string> MonitorDevicePaths(string gdiDeviceName)
    {
        var paths = new List<string>();
        if (string.IsNullOrEmpty(gdiDeviceName))
            return paths;

        try
        {
            for (uint i = 0; i < 16; i++)
            {
                var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(gdiDeviceName, i, ref device, EDD_GET_DEVICE_INTERFACE_NAME))
                    break;

                if ((device.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(device.DeviceID))
                    paths.Add(device.DeviceID);
            }
        }
        catch
        {
            // Interop failure on an exotic driver: fall back to the caller's secondary identity.
        }

        return paths;
    }
}
