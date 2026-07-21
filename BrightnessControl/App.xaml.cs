using System.Threading;
using System.Windows;
using BrightnessControl.Services;

namespace BrightnessControl;

public partial class App : System.Windows.Application
{
    private const string MutexName = "BrightnessControl.SingleInstance";
    private const string ShowEventName = "BrightnessControl.ShowFlyout";

    private MonitorService? _monitorService;
    private ProcessWatcherService? _processWatcher;
    private ProfileManager? _profileManager;
    private TrayIconManager? _trayIconManager;
    private HotkeyService? _hotkeyService;
    private ScheduleService? _scheduleService;

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: if one is already running, ask it to surface its flyout and bow out —
        // so a second launch doesn't add a second tray icon.
        _instanceMutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* first instance is mid-teardown */ }
            Shutdown();
            return;
        }

        // The running instance waits on this named event; a second launch signals it (above).
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _trayIconManager?.OpenFlyout()), null, Timeout.Infinite, false);

        // Match the app accent to the user's live Windows accent before any window is built.
        AccentColorService.Apply();

        var config = ConfigStore.Load();
        var state = new AppState(config);

        // Keep the registry in sync in case the config and actual Run-key state drifted
        // (e.g. manual reinstall).
        StartupManager.Reconcile(config.StartWithWindows);

        _monitorService = new MonitorService();
        await _monitorService.InitializeAsync();

        // Persist friendly monitor names for reference; not required for matching logic.
        config.Monitors = _monitorService.Monitors
            .Select(m => new Models.MonitorMeta { Id = m.Id, FriendlyName = m.FriendlyName })
            .ToList();

        // Seed a sensible idle-profile default (15%) for any newly detected monitor so the
        // app does something useful out of the box, before the user has opened Settings.
        foreach (var monitor in _monitorService.Monitors)
            config.IdleProfile.MonitorBrightness.TryAdd(monitor.Id, 15);

        ConfigStore.Save(config);

        _processWatcher = new ProcessWatcherService();
        _profileManager = new ProfileManager(_monitorService, _processWatcher, config);
        await _profileManager.ReconcileOnStartupAsync();

        _hotkeyService = new HotkeyService(_monitorService, () => state.Config);
        _hotkeyService.Initialize();

        _scheduleService = new ScheduleService(_profileManager, () => state.Config);
        _scheduleService.Start();

        // Applied by the Settings dialog on Save: re-register hotkeys and re-evaluate the schedule now.
        void ApplySettings()
        {
            _hotkeyService.ApplyConfig();
            _ = _profileManager.ReapplyNonGameAsync();
        }

        _trayIconManager = new TrayIconManager(_monitorService, _profileManager, state, ApplySettings);

        // On logoff/shutdown, Windows may cut the process off before OnExit finishes. Release the
        // low-level mouse hook here so it never lingers and jams system-wide mouse input.
        SessionEnding += (_, _) => _trayIconManager?.ReleaseHooks();
    }

    /// <summary>The single exit path (flyout power button, tray "Exit"). Releases the low-level mouse
    /// hook first — while the message loop is still healthy — so it's never left orphaned during the
    /// window/dispatcher teardown, which is what jams the system mouse for a few seconds after exit.</summary>
    public void RequestShutdown()
    {
        _trayIconManager?.ReleaseHooks();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showEvent?.Dispose();

        _scheduleService?.Dispose();
        _hotkeyService?.Dispose();
        _trayIconManager?.Dispose();
        _processWatcher?.Dispose();
        _monitorService?.Dispose();

        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { /* not owned on this thread */ }
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
