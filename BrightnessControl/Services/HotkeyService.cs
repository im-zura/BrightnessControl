using System.Windows.Interop;
using BrightnessControl.Models;
using BrightnessControl.Native;

namespace BrightnessControl.Services;

/// <summary>
/// Registers the user's global brightness hotkeys against a hidden message-only window and nudges
/// every responsive monitor up/down by the configured step when they fire. Safe to re-apply after
/// the user edits the hotkeys in Settings.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int IdUp = 1;
    private const int IdDown = 2;

    private readonly MonitorService _monitorService;
    private readonly Func<AppConfig> _config;
    private HwndSource? _source;
    private bool _upRegistered;
    private bool _downRegistered;

    public HotkeyService(MonitorService monitorService, Func<AppConfig> config)
    {
        _monitorService = monitorService;
        _config = config;
    }

    public void Initialize()
    {
        var p = new HwndSourceParameters("BrightnessControlHotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — a message-only window (no UI, no taskbar).
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        ApplyConfig();
    }

    /// <summary>Unregister and (if enabled) re-register both hotkeys from current config.</summary>
    public void ApplyConfig()
    {
        if (_source is null)
            return;

        var hwnd = _source.Handle;
        if (_upRegistered) { User32Interop.UnregisterHotKey(hwnd, IdUp); _upRegistered = false; }
        if (_downRegistered) { User32Interop.UnregisterHotKey(hwnd, IdDown); _downRegistered = false; }

        var h = _config().Hotkeys;
        if (!h.Enabled)
            return;

        _upRegistered = User32Interop.RegisterHotKey(hwnd, IdUp, h.Up.Modifiers | User32Interop.MOD_NOREPEAT, h.Up.Key);
        _downRegistered = User32Interop.RegisterHotKey(hwnd, IdDown, h.Down.Modifiers | User32Interop.MOD_NOREPEAT, h.Down.Key);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32Interop.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == IdUp) { _ = AdjustAllAsync(+_config().Hotkeys.Step); handled = true; }
            else if (id == IdDown) { _ = AdjustAllAsync(-_config().Hotkeys.Step); handled = true; }
        }

        return IntPtr.Zero;
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
        if (_source is null)
            return;

        var hwnd = _source.Handle;
        if (_upRegistered) User32Interop.UnregisterHotKey(hwnd, IdUp);
        if (_downRegistered) User32Interop.UnregisterHotKey(hwnd, IdDown);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
