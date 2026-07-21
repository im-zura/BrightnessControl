using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BrightnessControl.Models;
using BrightnessControl.Native;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace BrightnessControl.Services;

/// <summary>
/// Lets the user change brightness by scrolling the mouse wheel over the tray icon (like Twinkle
/// Tray). Uses a low-level mouse hook and resolves the icon's on-screen rectangle via
/// Shell_NotifyIconGetRect. Best-effort: if the icon's window/id can't be resolved (e.g. hidden in
/// the overflow) it simply does nothing — it never throws.
/// </summary>
internal sealed class TrayScrollService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MonitorService _monitorService;
    private readonly Func<AppConfig> _config;
    private readonly Dispatcher _dispatcher;

    // Keep a reference so the delegate isn't garbage-collected while the hook is installed.
    private LowLevelMouseProc? _proc;
    private IntPtr _hook;

    // Icon window+id resolved once by reflection and cached; the low-level hook callback must stay
    // fast (Windows silently drops a slow hook and that stutters the whole system's mouse).
    private bool _iconResolved;
    private IntPtr _iconHwnd;
    private uint _iconId;

    public TrayScrollService(NotifyIcon notifyIcon, MonitorService monitorService, Func<AppConfig> config)
    {
        _notifyIcon = notifyIcon;
        _monitorService = monitorService;
        _config = config;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Initialize()
    {
        _proc = HookProc;
        _hook = User32Interop.SetWindowsHookEx(
            User32Interop.WH_MOUSE_LL, _proc, User32Interop.GetModuleHandle(null), 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // A low-level hook callback must never throw and must return promptly, or Windows drops the
        // hook and mouse input stutters system-wide. Guard everything and fall through to the chain.
        try
        {
            if (nCode >= 0 && wParam.ToInt32() == User32Interop.WM_MOUSEWHEEL && _config().TrayScrollEnabled)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (CursorOverTrayIcon(data.pt))
                {
                    int delta = (short)(data.mouseData >> 16);
                    int step = Math.Max(1, _config().TrayScrollStep) * Math.Sign(delta);
                    _dispatcher.BeginInvoke(() => _ = AdjustAllAsync(step));
                    return new IntPtr(1); // swallow: the wheel shouldn't scroll whatever is behind the tray
                }
            }
        }
        catch { /* never let the hook throw */ }

        return User32Interop.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool CursorOverTrayIcon(POINT cursor)
    {
        if (!TryGetIconRect(out var r))
            return false;

        return cursor.X >= r.Left && cursor.X < r.Right && cursor.Y >= r.Top && cursor.Y < r.Bottom;
    }

    /// <summary>Resolve the tray icon's screen rect from its (hidden) owner window + id. The reflection
    /// dig into the WinForms NotifyIcon is done once and cached; per-call work is just the native rect
    /// query. Returns false if anything is unavailable.</summary>
    private bool TryGetIconRect(out RECT rect)
    {
        rect = default;
        if (!EnsureIconResolved())
            return false;

        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _iconHwnd,
            uID = _iconId,
        };
        return User32Interop.Shell_NotifyIconGetRect(ref id, out rect) == 0; // S_OK
    }

    private bool EnsureIconResolved()
    {
        if (_iconResolved)
            return true;

        try
        {
            var t = typeof(NotifyIcon);
            var windowField = t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            var idField = t.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            if (windowField?.GetValue(_notifyIcon) is not System.Windows.Forms.NativeWindow nw || idField is null)
                return false;

            var hWnd = nw.Handle;
            if (hWnd == IntPtr.Zero)
                return false;

            _iconHwnd = hWnd;
            _iconId = (uint)(int)(idField.GetValue(_notifyIcon) ?? 0);
            _iconResolved = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task AdjustAllAsync(int delta)
    {
        var config = _config();
        foreach (var monitor in _monitorService.Monitors.Where(m => m.IsResponsive))
        {
            var target = Math.Clamp(monitor.CurrentPercent + delta, 0, 100);
            await _monitorService.SetBrightnessPercentAsync(monitor.Id, target);
            config.IdleProfile.MonitorBrightness[monitor.Id] = target;
        }
        ConfigStore.Save(config);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            User32Interop.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }
}
