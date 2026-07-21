using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BrightnessControl.Services;

/// <summary>
/// Detects when configured game processes start/stop. Uses polling (no elevation required)
/// hybridized with Process.Exited for near-instant stop detection when available.
/// </summary>
internal sealed class ProcessWatcherService : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Process> _runningTracked = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _trackedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _timer;

    /// <summary>Raised with the full process name (e.g. "ForzaHorizon6.exe") and the live process when a
    /// tracked process starts. The process lets the caller locate which monitor the game runs on.</summary>
    public event Action<string, Process>? ProcessStarted;

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

    private void Poll()
    {
        ReconcileMissingProcesses();

        HashSet<string> tracked;
        lock (_lock) tracked = new HashSet<string>(_trackedProcessNames, StringComparer.OrdinalIgnoreCase);

        foreach (var fullName in tracked)
        {
            bool alreadyTracked;
            lock (_lock) alreadyTracked = _runningTracked.ContainsKey(fullName);
            if (alreadyTracked)
                continue;

            var baseName = Path.GetFileNameWithoutExtension(fullName);
            Process[] matches;
            try { matches = Process.GetProcessesByName(baseName); }
            catch { continue; }

            if (matches.Length == 0)
                continue;

            var process = matches[0];
            for (int i = 1; i < matches.Length; i++)
                matches[i].Dispose();

            lock (_lock) _runningTracked[fullName] = process;

            TryHookExit(fullName, process);
            ProcessStarted?.Invoke(fullName, process);
        }
    }

    private void TryHookExit(string fullName, Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => HandleExited(fullName, process);
        }
        catch (Win32Exception)
        {
            // Access denied (e.g. anti-cheat protected process). Fall back to poll-based
            // stop detection: the next poll tick simply won't find it running anymore.
        }
        catch (InvalidOperationException)
        {
            // Process already exited between GetProcessesByName and hooking Exited.
            HandleExited(fullName, process);
        }
    }

    private void HandleExited(string fullName, Process process)
    {
        lock (_lock) _runningTracked.Remove(fullName);
        process.Dispose();
        ProcessStopped?.Invoke(fullName);
    }

    /// <summary>Safety net for processes whose Exited hook didn't attach (access denied):
    /// checked at the start of every poll tick so a stale entry never lingers longer than one interval.</summary>
    private void ReconcileMissingProcesses()
    {
        List<string> stale;
        lock (_lock)
        {
            stale = _runningTracked
                .Where(kv => kv.Value.HasExited)
                .Select(kv => kv.Key)
                .ToList();
        }

        foreach (var fullName in stale)
        {
            lock (_lock)
            {
                if (_runningTracked.TryGetValue(fullName, out var process))
                {
                    _runningTracked.Remove(fullName);
                    process.Dispose();
                }
            }
            ProcessStopped?.Invoke(fullName);
        }
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
