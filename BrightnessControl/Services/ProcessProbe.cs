using System.ComponentModel;
using System.Diagnostics;

namespace BrightnessControl.Services;

/// <summary>A running process as <see cref="ProcessWatcherService"/> needs it. Faked in tests so the
/// start/stop/crash paths can be exercised without launching a real game.</summary>
internal interface IWatchedProcess : IDisposable
{
    int Id { get; }

    /// <summary>Subscribes to process exit for near-instant stop detection. Returns false when the
    /// hook can't be attached (access denied on an anti-cheat protected process) — the caller then
    /// falls back to poll-based detection. Never throws.</summary>
    bool TryHookExit(Action onExit);

    /// <summary>The underlying process when there is one — used to locate the game's monitor.</summary>
    Process? Underlying { get; }
}

/// <summary>Looks up running processes by name. The single seam between the watcher and the OS.</summary>
internal interface IProcessProbe
{
    /// <summary>Live processes matching a base name (no ".exe"). Never throws; empty on failure.</summary>
    IReadOnlyList<IWatchedProcess> ByBaseName(string baseName);
}

internal sealed class SystemProcessProbe : IProcessProbe
{
    public IReadOnlyList<IWatchedProcess> ByBaseName(string baseName)
    {
        try
        {
            return Process.GetProcessesByName(baseName)
                .Select(p => (IWatchedProcess)new SystemWatchedProcess(p))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"process probe failed for '{baseName}': {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<IWatchedProcess>();
        }
    }
}

internal sealed class SystemWatchedProcess : IWatchedProcess
{
    private readonly Process _process;

    public SystemWatchedProcess(Process process) => _process = process;

    public int Id
    {
        get { try { return _process.Id; } catch { return 0; } }
    }

    public Process? Underlying => _process;

    public bool TryHookExit(Action onExit)
    {
        try
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => onExit();
            return true;
        }
        catch (Win32Exception)
        {
            return false; // access denied (anti-cheat protected process) — poll detection covers it
        }
        catch (InvalidOperationException)
        {
            return false; // already exited between lookup and hook — the next poll notices
        }
    }

    public void Dispose()
    {
        try { _process.Dispose(); } catch { /* already torn down */ }
    }
}
