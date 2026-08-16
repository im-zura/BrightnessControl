using BrightnessControl.Models;
using BrightnessControl.Native;

namespace BrightnessControl.Services;

/// <summary>The seam for taking a display off the desktop and putting it back. Faked in tests.</summary>
internal interface IDisplayAttacher
{
    /// <summary>Records what is needed to identify the display later. Null when it can't be found.</summary>
    DetachedDisplay? CaptureMode(string deviceName);

    /// <summary>Removes the display from the desktop. The GPU stops driving that output, so the
    /// monitor loses signal and powers its panel down.</summary>
    bool Detach(string deviceName);

    /// <summary>Brings the display back with the layout Windows remembers for it.</summary>
    bool Attach(DetachedDisplay saved);
}

/// <summary>
/// Switches a display off by deactivating its path in the Windows display topology — the same
/// mechanism Settings uses for "Show only on 1". The GPU stops driving that output, so the monitor
/// sees no signal and powers its panel down, and Windows can bring it straight back.
///
/// The obvious alternatives do not work here:
/// <list type="bullet">
/// <item>DDC power (VCP 0xD6) is either ignored — the panel blinks and wakes right back up, because
/// Windows keeps the output live — or, in its "hard off" mode, takes the monitor's DDC circuit down
/// with it, leaving the physical button as the only way back.</item>
/// <item>The legacy ChangeDisplaySettingsEx detach (an all-zero DEVMODE) is rejected outright by
/// current drivers with DISP_CHANGE_FAILED, even though the same API still re-attaches fine.</item>
/// </list>
/// </summary>
internal sealed class DisplayAttacher : IDisplayAttacher
{
    public DetachedDisplay? CaptureMode(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return null;

        if (!TryQuery(DisplayConfigInterop.QDC_ONLY_ACTIVE_PATHS, out var paths, out _))
            return null;

        if (IndexOfSource(paths, deviceName) < 0)
        {
            Log.Warn($"{deviceName}: no active display path found");
            return null;
        }

        return new DetachedDisplay { DeviceName = deviceName };
    }

    public bool Detach(string deviceName)
    {
        if (!TryQuery(DisplayConfigInterop.QDC_ONLY_ACTIVE_PATHS, out var paths, out var modes))
            return false;

        var index = IndexOfSource(paths, deviceName);
        if (index < 0)
        {
            Log.Warn($"{deviceName}: not among the active display paths, nothing to switch off");
            return false;
        }

        // Deactivate just this path and hand the whole topology back. The mode indices for the
        // path being dropped have to be invalidated or the call is rejected as inconsistent.
        paths[index].flags &= ~DisplayConfigInterop.DISPLAYCONFIG_PATH_ACTIVE;
        paths[index].sourceInfo.modeInfoIdx = DisplayConfigInterop.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        paths[index].targetInfo.modeInfoIdx = DisplayConfigInterop.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

        var result = DisplayConfigInterop.SetDisplayConfig(
            (uint)paths.Length, paths, (uint)modes.Length, modes,
            DisplayConfigInterop.SDC_APPLY
            | DisplayConfigInterop.SDC_USE_SUPPLIED_DISPLAY_CONFIG
            | DisplayConfigInterop.SDC_ALLOW_CHANGES
            | DisplayConfigInterop.SDC_SAVE_TO_DATABASE);

        if (result != DisplayConfigInterop.ERROR_SUCCESS)
        {
            Log.Warn($"{deviceName}: switching off was rejected (SetDisplayConfig returned {result})");
            return false;
        }

        Log.Info($"{deviceName}: display path deactivated");
        return true;
    }

    public bool Attach(DetachedDisplay saved)
    {
        // Re-applying the extend topology restores every display Windows knows about, each with the
        // position and mode it remembers — no need to have captured them ourselves.
        var result = DisplayConfigInterop.SetDisplayConfig(
            0, null, 0, null,
            DisplayConfigInterop.SDC_APPLY | DisplayConfigInterop.SDC_TOPOLOGY_EXTEND);

        if (result != DisplayConfigInterop.ERROR_SUCCESS)
        {
            Log.Warn($"{saved.DeviceName}: switching back on was rejected (SetDisplayConfig returned {result})");
            return false;
        }

        Log.Info($"{saved.DeviceName}: display topology restored");
        return true;
    }

    private static bool TryQuery(uint flags, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
    {
        paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();

        var sizes = DisplayConfigInterop.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
        if (sizes != DisplayConfigInterop.ERROR_SUCCESS)
        {
            Log.Warn($"GetDisplayConfigBufferSizes failed ({sizes})");
            return false;
        }

        paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        var query = DisplayConfigInterop.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (query != DisplayConfigInterop.ERROR_SUCCESS)
        {
            Log.Warn($"QueryDisplayConfig failed ({query})");
            return false;
        }

        // The counts can come back smaller than the buffers we allocated.
        Array.Resize(ref paths, (int)pathCount);
        Array.Resize(ref modes, (int)modeCount);
        return true;
    }

    private static int IndexOfSource(DISPLAYCONFIG_PATH_INFO[] paths, string deviceName)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (string.Equals(DisplayConfigInterop.SourceDeviceName(paths[i].sourceInfo), deviceName,
                    StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
