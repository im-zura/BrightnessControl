using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
    private DisplayChangeWatcher? _displayWatcher;
    private AppState? _state;

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            // OnStartup is async void: without this, any failure here is an unhandled exception
            // that kills the process before the tray icon ever appears, with nothing to show for it.
            Log.Error("startup failed", ex);
            System.Windows.MessageBox.Show(
                $"Brightness Control failed to start:\n\n{ex.Message}\n\nDetails are in the log:\n%AppData%\\{AppInfo.Name}\\log.txt",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task StartAsync()
    {
        // Single instance: if one is already running, ask it to surface its flyout and bow out —
        // so a second launch doesn't add a second tray icon.
        _instanceMutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* first instance is mid-teardown */ }
            Shutdown();
            return;
        }

        InstallExceptionHandlers();
        Log.Info($"--- {AppInfo.NameWithVersion} starting ---");

        // The running instance waits on this named event; a second launch signals it (above).
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _trayIconManager?.OpenFlyout()), null, Timeout.Infinite, false);

        // Match the app accent to the user's live Windows accent before any window is built.
        AccentColorService.Apply();

        var config = ConfigStore.Load();
        _state = new AppState(config);

        // Keep the registry in sync in case the config and actual Run-key state drifted
        // (e.g. manual reinstall).
        StartupManager.Reconcile(config.StartWithWindows);

        _monitorService = new MonitorService();

        // A display switched off in a previous session is still off in Windows. Bring everything
        // back before doing anything else — a dark screen with no way to reach it is the worst
        // possible starting state.
        _monitorService.SeedDetached(config.DetachedDisplays);
        _monitorService.DetachedChanged += OnDetachedChanged;

        await _monitorService.InitializeAsync();
        if (config.DetachedDisplays.Count > 0)
        {
            Log.Info($"restoring {config.DetachedDisplays.Count} display(s) switched off in a previous session");
            await _monitorService.PowerOnAllAsync();
        }

        ReconcileConfigWithMonitors();

        _processWatcher = new ProcessWatcherService();
        _profileManager = new ProfileManager(_monitorService, _processWatcher, () => _state!.Config);
        await _profileManager.ReconcileOnStartupAsync();

        _hotkeyService = new HotkeyService(_monitorService, () => _state!.Config, () => _profileManager!.IsGameRunning);
        _hotkeyService.Initialize();

        _scheduleService = new ScheduleService(_profileManager, () => _state!.Config);
        _scheduleService.Start();

        // Applied by the Settings dialog on Save: re-register hotkeys and re-evaluate the schedule now.
        void ApplySettings()
        {
            _hotkeyService.ApplyConfig();
            _ = _profileManager.ReapplyNonGameAsync();
        }

        _trayIconManager = new TrayIconManager(_monitorService, _profileManager, _state, ApplySettings);

        // A display that was off at launch, woke from sleep, or was just plugged in leaves every
        // cached DDC handle stale. Re-enumerate and re-apply so it joins the app instead of being
        // invisible until the next restart.
        _displayWatcher = new DisplayChangeWatcher(OnDisplaysChangedAsync, () => _monitorService!.RetryUnresponsiveAsync());
        _displayWatcher.Start();

        // On logoff/shutdown, Windows may cut the process off before OnExit finishes. Release the
        // low-level mouse hook here so it never lingers and jams system-wide mouse input, and wake
        // any display the app switched off so it isn't left dark with nothing to turn it back on.
        SessionEnding += (_, _) =>
        {
            _trayIconManager?.ReleaseHooks();
            _monitorService?.PowerOnAllBlocking();
        };

        Log.Info("startup complete");
    }

    /// <summary>Re-runs after every display change: adopts saved values onto newly seen monitors and
    /// seeds defaults, then persists only when something actually moved.</summary>
    private void ReconcileConfigWithMonitors()
    {
        if (_state is null || _monitorService is null)
            return;

        if (ConfigMigrator.Reconcile(_state.Config, _monitorService.Monitors))
            ConfigStore.Save(_state.Config);
    }

    private async Task OnDisplaysChangedAsync()
    {
        if (_monitorService is null || _profileManager is null)
            return;

        if (!await _monitorService.RefreshAsync())
            return;

        ReconcileConfigWithMonitors();
        await _profileManager.ApplyCurrentStateAsync();
    }

    /// <summary>Persists which displays are switched off, immediately — the value of the list is
    /// that it survives the app not shutting down cleanly.</summary>
    private void OnDetachedChanged()
    {
        if (_state is null || _monitorService is null)
            return;

        _state.Config.DetachedDisplays = _monitorService.DetachedDisplays.ToList();
        ConfigStore.Save(_state.Config);
    }

    /// <summary>Nothing here should be able to take the app down silently — a dead tray icon leaves
    /// the user's monitors stuck at whatever brightness was last written.</summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("unhandled dispatcher exception", args.Exception);
            args.Handled = true; // keep the tray icon alive; the failing action is already lost
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled domain exception", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>The single exit path (flyout power button, tray "Exit"). Releases the low-level mouse
    /// hook first — while the message loop is still healthy — so it's never left orphaned during the
    /// window/dispatcher teardown, which is what jams the system mouse for a few seconds after exit.</summary>
    public void RequestShutdown()
    {
        _trayIconManager?.ReleaseHooks();
        _monitorService?.PowerOnAllBlocking();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("shutting down");

        _showWait?.Unregister(null);
        _showEvent?.Dispose();

        _displayWatcher?.Dispose();
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
