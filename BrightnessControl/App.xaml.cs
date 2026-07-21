using System.Windows;
using BrightnessControl.Services;

namespace BrightnessControl;

public partial class App : System.Windows.Application
{
    private MonitorService? _monitorService;
    private ProcessWatcherService? _processWatcher;
    private ProfileManager? _profileManager;
    private TrayIconManager? _trayIconManager;

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

        _trayIconManager = new TrayIconManager(_monitorService, _profileManager, state);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        _processWatcher?.Dispose();
        _monitorService?.Dispose();
        base.OnExit(e);
    }
}
