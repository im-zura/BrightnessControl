using System.Diagnostics;
using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Owns "which profile is currently applied" and reacts to game start/stop events by
/// applying the matching game profile's brightness, or falling back to the idle profile.
///
/// Every apply runs under an intent generation taken from <see cref="MonitorService"/>. Locating a
/// game's monitor can take seconds, and without that stamp a game profile that was still resolving
/// when the game exited would overwrite the idle brightness that had already been restored — the
/// "brightness stayed at the game's level after quitting" bug.
/// </summary>
internal sealed class ProfileManager
{
    private readonly MonitorService _monitorService;
    private readonly ProcessWatcherService _watcher;
    private readonly Func<AppConfig> _config;

    private CancellationTokenSource? _gameApplyCts;
    private readonly object _ctsLock = new();

    public string ActiveProfileName { get; private set; } = "Idle";

    /// <summary>True while a tracked game is running — manual brightness changes are not remembered
    /// as the everyday level then.</summary>
    public bool IsGameRunning => _watcher.CurrentlyRunning.Any();

    public event Action<string>? ActiveProfileChanged;

    public ProfileManager(MonitorService monitorService, ProcessWatcherService watcher, Func<AppConfig> config)
    {
        _monitorService = monitorService;
        _watcher = watcher;
        _config = config;

        _watcher.ProcessStarted += OnProcessStarted;
        _watcher.ProcessStopped += OnProcessStopped;
    }

    /// <summary>Refreshes the set of process names being watched. Call after profiles are added/edited.</summary>
    public void UpdateTrackedProcesses()
    {
        _watcher.UpdateTrackedProcessNames(
            _config().GameProfiles.Where(p => p.Enabled).Select(p => p.ProcessName));
    }

    /// <summary>Run once at startup: checks whether a tracked game is already running and applies
    /// its profile immediately, otherwise applies the idle profile. Makes brightness state
    /// deterministic on every launch instead of "whatever it happened to be".</summary>
    public async Task ReconcileOnStartupAsync()
    {
        UpdateTrackedProcesses();
        _watcher.Start(_config().PollingIntervalMs);
        await ApplyCurrentStateAsync().ConfigureAwait(false);
    }

    /// <summary>Re-applies whatever should be showing right now — the running game's profile, or the
    /// schedule/idle level. Called at startup, and again whenever the monitor set changes so a display
    /// that just woke up gets the right brightness instead of whatever it powered on with.</summary>
    public async Task ApplyCurrentStateAsync()
    {
        var config = _config();

        GameProfile? running = null;
        Process? runningProcess = null;
        var processes = new List<Process>();

        try
        {
            foreach (var profile in config.GameProfiles.Where(p => p.Enabled))
            {
                Process[] matches;
                try { matches = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName)); }
                catch { continue; }

                processes.AddRange(matches);
                if (matches.Length > 0 && running == null)
                {
                    running = profile;
                    runningProcess = matches[0];
                }
            }

            if (running != null)
                await ApplyGameProfileAsync(running, runningProcess).ConfigureAwait(false);
            else
                await ApplyNonGameStateAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    /// <summary>Re-apply the non-game brightness (schedule block or manual idle), but only when no
    /// tracked game is currently running. Called by ScheduleService on a day/night boundary and after
    /// the user edits the schedule.</summary>
    public async Task ReapplyNonGameAsync()
    {
        if (_watcher.CurrentlyRunning.Any())
            return;

        await ApplyNonGameStateAsync().ConfigureAwait(false);
    }

    private async void OnProcessStarted(string processName, Process? process)
    {
        try
        {
            var profile = _config().GameProfiles.FirstOrDefault(p =>
                p.Enabled && string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
                await ApplyGameProfileAsync(profile, process).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"applying profile for {processName} failed", ex);
        }
    }

    private async void OnProcessStopped(string processName)
    {
        try
        {
            // A game profile may still be resolving this game's monitor; cancel it before restoring.
            CancelPendingGameApply();

            // If another tracked game is still running (edge case), stay on it; otherwise go idle.
            var stillRunning = _watcher.CurrentlyRunning.FirstOrDefault();
            var profile = stillRunning != null
                ? _config().GameProfiles.FirstOrDefault(p => p.Enabled &&
                    string.Equals(p.ProcessName, stillRunning, StringComparison.OrdinalIgnoreCase))
                : null;

            if (profile == null)
            {
                await ApplyNonGameStateAsync().ConfigureAwait(false);
                return;
            }

            Process[] matches;
            try { matches = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName)); }
            catch { matches = Array.Empty<Process>(); }

            try { await ApplyGameProfileAsync(profile, matches.FirstOrDefault()).ConfigureAwait(false); }
            finally { foreach (var m in matches) m.Dispose(); }
        }
        catch (Exception ex)
        {
            Log.Error($"restoring brightness after {processName} failed", ex);
        }
    }

    /// <summary>Applies the profile's brightness to only the monitor the game runs on. If that monitor
    /// can't be resolved (no window found), falls back to all responsive monitors so it never no-ops.</summary>
    private async Task ApplyGameProfileAsync(GameProfile profile, Process? gameProcess)
    {
        var percent = profile.EffectiveGameBrightness;
        var generation = _monitorService.BeginIntent();

        var cancellation = ResetPendingGameApply();

        string? monitorId = null;
        if (gameProcess != null)
        {
            monitorId = await GameMonitorLocator
                .ResolveMonitorIdAsync(gameProcess, _monitorService.Monitors, cancellation)
                .ConfigureAwait(false);
        }

        // The game may have exited while we waited for its window — the idle profile has already
        // been restored by then, and writing the game's level now is exactly what got stuck.
        if (!_monitorService.IsCurrentIntent(generation) || cancellation.IsCancellationRequested)
        {
            Log.Info($"game profile '{profile.Name}' abandoned — superseded before it could apply");
            return;
        }

        Log.Info($"applying game profile '{profile.Name}' at {percent}% to {monitorId ?? "all monitors"}");

        if (monitorId != null)
        {
            await _monitorService.SetBrightnessPercentAsync(monitorId, percent, generation, verify: true)
                .ConfigureAwait(false);
        }
        else
        {
            var targets = _monitorService.Monitors
                .Where(m => m.IsResponsive)
                .ToDictionary(m => m.Id, _ => percent);
            await _monitorService.ApplyProfileAsync(targets, generation).ConfigureAwait(false);
        }

        SetActiveProfile(profile.Name);
    }

    private async Task ApplyNonGameStateAsync()
    {
        var config = _config();
        var (brightness, label) = ScheduleService.ResolveNonGame(config, DateTime.Now);
        var generation = _monitorService.BeginIntent();

        Log.Info($"applying '{label}' profile: {string.Join(", ", brightness.Select(kv => $"{kv.Key}={kv.Value}%"))}");

        await _monitorService.ApplyProfileAsync(brightness, generation).ConfigureAwait(false);
        SetActiveProfile(label);
    }

    private void SetActiveProfile(string name)
    {
        ActiveProfileName = name;
        ActiveProfileChanged?.Invoke(name);
    }

    /// <summary>Starts a fresh cancellation scope for a game apply and cancels the previous one.</summary>
    private CancellationToken ResetPendingGameApply()
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous;

        lock (_ctsLock)
        {
            previous = _gameApplyCts;
            _gameApplyCts = cts;
        }

        Cancel(previous);
        return cts.Token;
    }

    private void CancelPendingGameApply()
    {
        CancellationTokenSource? previous;
        lock (_ctsLock)
        {
            previous = _gameApplyCts;
            _gameApplyCts = null;
        }

        Cancel(previous);
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        if (cts is null)
            return;

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }
}
