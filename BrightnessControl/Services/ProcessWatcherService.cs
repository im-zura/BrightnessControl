using System.Diagnostics;
using System.IO;

namespace BrightnessControl.Services;

/// <summary>
/// Detects when configured game processes start/stop. Stop detection is name-based (a fresh
/// lookup every tick) rather than handle-based: querying a cached Process for its exit state
/// throws on anti-cheat protected games, and an exception on the timer thread would take the
/// whole app down with it. Process.Exited is still hooked when possible, purely for latency.
/// </summary>
internal sealed class ProcessWatcherService : IDisposable
{
    private readonly object _lock = new();
    private readonly IProcessProbe _probe;
    private readonly Dictionary<string, IWatchedProcess> _runningTracked = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _trackedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _timer;

    public ProcessWatcherService(IProcessProbe? probe = null) => _probe = probe ?? new SystemProcessProbe();

    /// <summary>Raised with the full process name (e.g. "ForzaHorizon6.exe") and the live process when a
    /// tracked process starts. The process lets the caller locate which monitor the game runs on.</summary>
    public event Action<string, Process?>? ProcessStarted;

    /// <summary>Raised with the full process name when a tracked process stops.</summary>
    public event Action<string>? ProcessStopped;

    public void UpdateTrackedProcessNames(IEnumerable<string> processNamesWithExe)
    {
        lock (_lock)
        {
            _trackedProcessNames = new HashSet<string>(processNamesWithExe, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Start(int pollingIntervalMs)
    {
        Stop();
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, pollingIntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Full names currently detected as running, for startup reconciliation.</summary>
    public IReadOnlyCollection<string> CurrentlyRunning
    {
        get { lock (_lock) return _runningTracked.Keys.ToList(); }
    }

    /// <summary>One poll tick. Public so tests can drive it deterministically instead of waiting on a timer.</summary>
    public void Poll()
    {
        // A timer callback that throws is an unhandled exception on a thread-pool thread, which
        // terminates the process. Everything below is best-effort; nothing here may escape.
        try
        {
            PollCore();
        }
        catch (Exception ex)
        {
            Log.Error("process poll tick failed", ex);
        }
    }

    private void PollCore()
    {
        HashSet<string> tracked;
        lock (_lock) tracked = new HashSet<string>(_trackedProcessNames, StringComparer.OrdinalIgnoreCase);

        // Names we hold but no longer watch (profile disabled/deleted): drop them without firing.
        List<string> untracked;
        lock (_lock) untracked = _runningTracked.Keys.Where(k => !tracked.Contains(k)).ToList();
        foreach (var name in untracked)
            TryRemove(name);

        foreach (var fullName in tracked)
        {
            var baseName = Path.GetFileNameWithoutExtension(fullName);
            var matches = _probe.ByBaseName(baseName);

            bool alreadyTracked;
            lock (_lock) alreadyTracked = _runningTracked.ContainsKey(fullName);

            if (matches.Count == 0)
            {
                if (alreadyTracked && TryRemove(fullName))
                {
                    Log.Info($"game stopped: {fullName}");
                    ProcessStopped?.Invoke(fullName);
                }
                continue;
            }

            if (alreadyTracked)
            {
                foreach (var m in matches) m.Dispose();
                continue;
            }

            var process = matches[0];
            for (int i = 1; i < matches.Count; i++)
                matches[i].Dispose();

            lock (_lock) _runningTracked[fullName] = process;

            // Latency optimization only — a failed hook is fine, the next tick catches the exit.
            process.TryHookExit(() => OnExitSignalled(fullName));

            Log.Info($"game started: {fullName} (pid {process.Id})");
            ProcessStarted?.Invoke(fullName, process.Underlying);
        }
    }

    private void OnExitSignalled(string fullName)
    {
        try
        {
            if (!TryRemove(fullName))
                return; // the poll tick already reported it — don't fire twice

            Log.Info($"game stopped: {fullName} (exit event)");
            ProcessStopped?.Invoke(fullName);
        }
        catch (Exception ex)
        {
            Log.Error($"exit handler failed for {fullName}", ex);
        }
    }

    /// <summary>Removes and disposes a tracked entry. Returns true only for the caller that actually
    /// removed it, so the poll tick and the exit event can't both report the same stop.</summary>
    private bool TryRemove(string fullName)
    {
        IWatchedProcess? process;
        lock (_lock)
        {
            if (!_runningTracked.Remove(fullName, out process))
                return false;
        }

        process?.Dispose();
        return true;
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            foreach (var process in _runningTracked.Values)
                process.Dispose();
            _runningTracked.Clear();
        }
    }
}
