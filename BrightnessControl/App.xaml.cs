using System.Windows;
using BrightnessControl.Services;

namespace BrightnessControl;

public partial class App : System.Windows.Application
{
    private MonitorService? _monitorService;
    private ProcessWatcherService? _processWatcher;
    private ProfileManager? _profileManager;
    private TrayIconManager? _trayIconManager;
    private HotkeyService? _hotkeyService;
    private ScheduleService? _scheduleService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scheduleService?.Dispose();
        _hotkeyService?.Dispose();
        _trayIconManager?.Dispose();
        _processWatcher?.Dispose();
        _monitorService?.Dispose();
        base.OnExit(e);
    }
}
