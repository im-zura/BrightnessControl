using System.Diagnostics;
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
        GameProfile? running = null;
        Process? runningProcess = null;
        foreach (var profile in _config.GameProfiles.Where(p => p.Enabled))
        {
            var matches = Process.GetProcessesByName(
                System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName));
            if (matches.Length > 0)
            {
                running = profile;
                runningProcess = matches[0];
                for (int i = 1; i < matches.Length; i++) matches[i].Dispose();
                break;
            }
            foreach (var m in matches) m.Dispose();
        }

        if (running != null)
        {
            await ApplyGameProfileAsync(running, runningProcess);
            runningProcess?.Dispose();
        }
        else
        {
            await ApplyNonGameStateAsync();
        }
    }

    /// <summary>Re-apply the non-game brightness (schedule block or manual idle), but only when no
    /// tracked game is currently running. Called by ScheduleService on a day/night boundary and after
    /// the user edits the schedule.</summary>
    public async Task ReapplyNonGameAsync()
    {
        if (_watcher.CurrentlyRunning.Any())
            return;

        await ApplyNonGameStateAsync();
    }

    private async void OnProcessStarted(string processName, Process process)
    {
        var profile = _config.GameProfiles.FirstOrDefault(p =>
            p.Enabled && string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        if (profile != null)
            await ApplyGameProfileAsync(profile, process);
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
        {
            var matches = Process.GetProcessesByName(
                System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName));
            var proc = matches.FirstOrDefault();
            await ApplyGameProfileAsync(profile, proc);
            foreach (var m in matches) m.Dispose();
        }
        else
        {
            await ApplyNonGameStateAsync();
        }
    }

    /// <summary>Applies the profile's brightness to only the monitor the game runs on. If that monitor
    /// can't be resolved (no window found), falls back to all responsive monitors so it never no-ops.</summary>
    private async Task ApplyGameProfileAsync(GameProfile profile, Process? gameProcess)
    {
        var percent = profile.EffectiveGameBrightness;

        var monitorId = gameProcess != null
            ? await GameMonitorLocator.ResolveMonitorIdAsync(gameProcess, _monitorService.Monitors)
            : null;

        if (monitorId != null)
        {
            await _monitorService.SetBrightnessPercentAsync(monitorId, percent);
        }
        else
        {
            foreach (var monitor in _monitorService.Monitors.Where(m => m.IsResponsive))
                await _monitorService.SetBrightnessPercentAsync(monitor.Id, percent);
        }

        ActiveProfileName = profile.Name;
        ActiveProfileChanged?.Invoke(ActiveProfileName);
    }

    private async Task ApplyNonGameStateAsync()
    {
        var (brightness, label) = ScheduleService.ResolveNonGame(_config, DateTime.Now);
        await _monitorService.ApplyProfileAsync(brightness);
        ActiveProfileName = label;
        ActiveProfileChanged?.Invoke(ActiveProfileName);
    }
}
