using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using BrightnessControl.Native;
using Microsoft.Win32;

namespace BrightnessControl.Services;

/// <summary>
/// Notices when the set of usable displays changes — a monitor switched on or off at the wall, a
/// cable plugged in, a resolution change, a resume from sleep — and asks the caller to re-enumerate.
///
/// This is what makes the app work with a display that was off when it started: without it, monitor
/// handles are taken once at launch and every later DDC call goes to a handle that no longer exists.
/// Bursts of notifications (Windows sends several per topology change) are collapsed into one refresh.
/// </summary>
internal sealed class DisplayChangeWatcher : IDisposable
{
    /// <summary>Displays settle noisily; wait for quiet before re-enumerating.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);

    /// <summary>Backstop for monitors that answer DDC again without any notification at all.</summary>
    private static readonly TimeSpan RetrySweep = TimeSpan.FromSeconds(30);

    private readonly Func<Task> _onChanged;
    private readonly Func<Task> _onRetrySweep;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _sweepTimer;

    private HwndSource? _source;
    private IntPtr _deviceNotification;
    private IntPtr _powerNotification;
    private bool _running;

    public DisplayChangeWatcher(Func<Task> onChanged, Func<Task> onRetrySweep)
    {
        _onChanged = onChanged;
        _onRetrySweep = onRetrySweep;

        _debounceTimer = new DispatcherTimer { Interval = Debounce };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await SafeInvokeAsync(_onChanged, "display refresh").ConfigureAwait(true);
        };

        _sweepTimer = new DispatcherTimer { Interval = RetrySweep };
        _sweepTimer.Tick += async (_, _) => await SafeInvokeAsync(_onRetrySweep, "unresponsive re-probe").ConfigureAwait(true);
    }

    public void Start()
    {
        if (_running)
            return;
        _running = true;

        // Device-interface and power-setting notifications are delivered to a specific window, so a
        // message-only window is fine for those. Broadcasts like WM_DISPLAYCHANGE never reach a
        // message-only window — SystemEvents (which owns a real hidden top-level window) covers those.
        var parameters = new HwndSourceParameters("BrightnessControlDisplayWatcher")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        RegisterMonitorArrival(_source.Handle);
        RegisterDisplayPowerState(_source.Handle);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _sweepTimer.Start();
        Log.Info("display change watcher started");
    }

    // ---- Native registrations ----------------------------------------------

    private void RegisterMonitorArrival(IntPtr hwnd)
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = User32Interop.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = User32Interop.GUID_DEVINTERFACE_MONITOR,
        };

        var buffer = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            _deviceNotification = User32Interop.RegisterDeviceNotification(
                hwnd, buffer, User32Interop.DEVICE_NOTIFY_WINDOW_HANDLE);

            if (_deviceNotification == IntPtr.Zero)
                Log.Warn("RegisterDeviceNotification for monitors failed");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void RegisterDisplayPowerState(IntPtr hwnd)
    {
        var guid = User32Interop.GUID_CONSOLE_DISPLAY_STATE;
        _powerNotification = User32Interop.RegisterPowerSettingNotification(
            hwnd, ref guid, User32Interop.DEVICE_NOTIFY_WINDOW_HANDLE);

        if (_powerNotification == IntPtr.Zero)
            Log.Warn("RegisterPowerSettingNotification for display state failed");
    }

    // ---- Signals ------------------------------------------------------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case User32Interop.WM_DEVICECHANGE:
                var evt = wParam.ToInt32();
                if (evt is User32Interop.DBT_DEVICEARRIVAL or User32Interop.DBT_DEVICEREMOVECOMPLETE)
                    Schedule("monitor device change");
                break;

            case User32Interop.WM_POWERBROADCAST:
                var reason = wParam.ToInt32();
                if (reason is User32Interop.PBT_POWERSETTINGCHANGE
                    or User32Interop.PBT_APMRESUMESUSPEND
                    or User32Interop.PBT_APMRESUMEAUTOMATIC)
                    Schedule("display power state change");
                break;
        }

        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Schedule("display settings changed");

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Schedule("resume from sleep");
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            Schedule("session unlock");
    }

    /// <summary>Collapses a burst of notifications into a single refresh once things go quiet.</summary>
    private void Schedule(string reason)
    {
        void Restart()
        {
            Log.Info($"display change: {reason}");
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        // SystemEvents callbacks arrive on their own thread; the timers belong to the UI dispatcher.
        if (_debounceTimer.Dispatcher.CheckAccess())
            Restart();
        else
            _debounceTimer.Dispatcher.BeginInvoke(Restart);
    }

    private static async Task SafeInvokeAsync(Func<Task> action, string what)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error($"{what} failed", ex);
        }
    }

    public void Dispose()
    {
        if (!_running)
            return;
        _running = false;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _debounceTimer.Stop();
        _sweepTimer.Stop();

        if (_deviceNotification != IntPtr.Zero)
        {
            User32Interop.UnregisterDeviceNotification(_deviceNotification);
            _deviceNotification = IntPtr.Zero;
        }

        if (_powerNotification != IntPtr.Zero)
        {
            User32Interop.UnregisterPowerSettingNotification(_powerNotification);
            _powerNotification = IntPtr.Zero;
        }

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
