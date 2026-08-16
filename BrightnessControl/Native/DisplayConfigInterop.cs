using System.Runtime.InteropServices;

namespace BrightnessControl.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate;
    public uint scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)]
    public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

/// <summary>
/// Mode entries are passed straight back to Windows untouched, so the union payload is carried as
/// opaque 8-byte words rather than modelled member by member.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    public uint infoType;
    public uint id;
    public LUID adapterId;
    public ulong union0, union1, union2, union3, union4, union5;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public uint type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

/// <summary>
/// The display-topology API Windows Settings itself uses. The older ChangeDisplaySettingsEx route
/// for detaching a display is rejected outright by current drivers (DISP_CHANGE_FAILED) even though
/// the same call still works for re-attaching, so this is the only usable path.
/// </summary>
internal static class DisplayConfigInterop
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

    public const int ERROR_SUCCESS = 0;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    /// <summary>The GDI device name ("\\.\DISPLAY2") a path's source maps to, or "" if unknown.</summary>
    public static string SourceDeviceName(DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = source.adapterId,
                id = source.id,
            },
            viewGdiDeviceName = "",
        };

        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request.viewGdiDeviceName : "";
    }
}
