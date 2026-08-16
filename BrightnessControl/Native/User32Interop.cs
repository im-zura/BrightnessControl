using System.Runtime.InteropServices;

namespace BrightnessControl.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

/// <summary>Payload of a WH_MOUSE_LL hook callback.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;   // high word = wheel delta for WM_MOUSEWHEEL
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

/// <summary>Identifies a specific tray icon for Shell_NotifyIconGetRect (by owner hWnd + id).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFYICONIDENTIFIER
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public Guid guidItem;
}

/// <summary>Header of a WM_DEVICECHANGE device-interface notification.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEV_BROADCAST_DEVICEINTERFACE
{
    public int dbcc_size;
    public int dbcc_devicetype;
    public int dbcc_reserved;
    public Guid dbcc_classguid;
    public short dbcc_name;
}

internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

/// <summary>P/Invoke surface for global hotkeys, the low-level mouse hook, and tray-icon geometry.</summary>
internal static class User32Interop
{
    // ---- Global hotkeys ----
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Low-level mouse hook (tray-icon scroll) ----
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEWHEEL = 0x020A;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ---- Tray-icon geometry ----
    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    // ---- Window -> monitor mapping (per-monitor game brightness) ----
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    // ---- Display hot-plug / monitor power notifications ----

    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_POWERBROADCAST = 0x0218;

    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;

    /// <summary>Device-interface class of monitors — fires when a display is attached or detached.</summary>
    public static readonly Guid GUID_DEVINTERFACE_MONITOR =
        new("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7");

    /// <summary>Power setting that reports the display being switched on or off.</summary>
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE =
        new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
