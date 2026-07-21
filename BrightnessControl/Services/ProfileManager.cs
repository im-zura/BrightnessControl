using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Owns "which profile is currently applied" and reacts to game start/stop events by
/// applying the matching game profile's brightness, or falling back to the idle profile.
/// </summary>
internal sealed class ProfileManager
{
    private readonly MonitorService _monitorService;
    private readonly ProcessWatcherService _watcher;
    private AppConfig _config;

    public string ActiveProfileName { get; private set; } = "Idle";

    public event Action<string>? ActiveProfileChanged;

    public ProfileManager(MonitorService monitorService, ProcessWatcherService watcher, AppConfig config)
    {
        _monitorService = monitorService;
        _watcher = watcher;
        _config = config;

        _watcher.ProcessStarted += OnProcessStarted;
        _watcher.ProcessStopped += OnProcessStopped;
    }

    /// <summary>Refreshes the set of process names being watched. Call after profiles are added/edited.</summary>
    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        _watcher.UpdateTrackedProcessNames(
            config.GameProfiles.Where(p => p.Enabled).Select(p => p.ProcessName));
    }

    /// <summary>Run once at startup: checks whether a tracked game is already running and applies
    /// its profile immediately, otherwise applies the idle profile. Makes brightness state
    /// deterministic on every launch instead of "whatever it happened to be".</summary>
    public async Task ReconcileOnStartupAsync()
    {
        UpdateConfig(_config);
        _watcher.Start(_config.PollingIntervalMs);

        // Give the watcher one immediate poll's worth of a head start isn't needed: we can
        // check directly here using the same matching the watcher will use going forward.
        var running = _config.GameProfiles
            .Where(p => p.Enabled)
            .FirstOrDefault(p => System.Diagnostics.Process.GetProcessesByName(
                System.IO.Path.GetFileNameWithoutExtension(p.ProcessName)).Length > 0);

        if (running != null)
            await ApplyGameProfileAsync(running);
        else
            await ApplyIdleProfileAsync();
    }

    private async void OnProcessStarted(string processName)
    {
        var profile = _config.GameProfiles.FirstOrDefault(p =>
            p.Enabled && string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        if (profile != null)
            await ApplyGameProfileAsync(profile);
    }

    private async void OnProcessStopped(string processName)
    {
        // If another tracked game is still running (edge case), stay on it; otherwise go idle.
        var stillRunning = _watcher.CurrentlyRunning.FirstOrDefault();
        var profile = stillRunning != null
            ? _config.GameProfiles.FirstOrDefault(p => p.Enabled &&
                string.Equals(p.ProcessName, stillRunning, StringComparison.OrdinalIgnoreCase))
            : null;

        if (profile != null)
            await ApplyGameProfileAsync(profile);
        else
            await ApplyIdleProfileAsync();
    }

    private async Task ApplyGameProfileAsync(GameProfile profile)
    {
        await _monitorService.ApplyProfileAsync(profile.MonitorBrightness);
        ActiveProfileName = profile.Name;
        ActiveProfileChanged?.Invoke(ActiveProfileName);
    }

    private async Task ApplyIdleProfileAsync()
    {
        await _monitorService.ApplyProfileAsync(_config.IdleProfile.MonitorBrightness);
        ActiveProfileName = "Idle";
        ActiveProfileChanged?.Invoke(ActiveProfileName);
    }
}
