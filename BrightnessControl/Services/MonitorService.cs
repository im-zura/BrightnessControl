using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// High-level monitor API: enumerates physical monitors, caches their handles, and exposes
/// 0-100% brightness scaled from each monitor's native min/max range.
///
/// Three things make it survive real desktops:
/// <list type="bullet">
/// <item>handles are re-enumerated on demand (<see cref="RefreshAsync"/>) because a display that
/// sleeps or is unplugged invalidates them — every later write would silently no-op;</item>
/// <item>writes are serialized and stamped with an intent generation, so a slow apply that is
/// already obsolete (e.g. a game profile still resolving when the game exits) can never land on
/// top of a newer one;</item>
/// <item>profile writes are read back and retried, because a monitor drops DDC commands while the
/// display mode is switching — exactly what happens when a fullscreen game exits.</item>
/// </list>
/// </summary>
internal sealed class MonitorService : IDisposable
{
    private static readonly int[] WriteRetryDelaysMs = { 250, 500, 1000 };
    private static readonly int[] ProbeRetryDelaysMs = { 400, 800, 1600 };

    /// <summary>Read-back tolerance in percent: some panels quantise brightness to coarse steps.</summary>
    private const int VerifyTolerance = 3;

    private readonly IMonitorTransport _transport;
    private readonly IDisplayAttacher _attacher;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<Entry> _monitors = new();
    private readonly List<DetachedDisplay> _detached = new();

    private long _generation;
    private bool _disposed;

    private sealed record Entry(PhysicalMonitorHandle Handle, MonitorInfo Info);

    public MonitorService(IMonitorTransport? transport = null, IDisplayAttacher? attacher = null)
    {
        _transport = transport ?? new Dxva2MonitorTransport();
        _attacher = attacher ?? new DisplayAttacher();
    }

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get { lock (_lock) return _monitors.Select(m => m.Info).ToList(); }
    }

    /// <summary>Raised (monitorId, percent) after a successful brightness write, from any source
    /// (hotkey, tray scroll, slider). Lets an open flyout mirror external changes live.</summary>
    public event Action<string, int>? BrightnessChanged;

    /// <summary>Raised after the monitor set is re-enumerated and something actually differs —
    /// a display woke up, was plugged in, or went away. The UI rebuilds on this.</summary>
    public event Action? MonitorsChanged;

    // ---- Enumeration -----------------------------------------------------------

    public Task InitializeAsync() => RefreshAsync(probeRetries: 1);

    /// <summary>Re-enumerates physical monitors and reconciles them with what we already knew.
    /// Existing <see cref="MonitorInfo"/> objects are kept (matched by stable id) so open sliders
    /// and saved state survive a display coming back. Raises <see cref="MonitorsChanged"/> when the
    /// visible set changed.</summary>
    public async Task<bool> RefreshAsync(int probeRetries = 3)
    {
        var handles = _transport.Enumerate();

        var probed = new List<Entry>(handles.Count);
        for (int i = 0; i < handles.Count; i++)
            probed.Add(await BuildEntryAsync(handles[i], i, probeRetries).ConfigureAwait(false));

        List<PhysicalMonitorHandle> stale;
        bool changed;

        lock (_lock)
        {
            stale = _monitors.Select(m => m.Handle).ToList();

            var before = _monitors.Select(m => $"{m.Info.Id}:{m.Info.IsResponsive}").ToList();
            var after = probed.Select(m => $"{m.Info.Id}:{m.Info.IsResponsive}").ToList();
            changed = !before.SequenceEqual(after);

            _monitors.Clear();
            _monitors.AddRange(probed);
        }

        // Released after the new handles are in place: destroying them is what makes the old ones
        // invalid, and a concurrent write must never be able to grab one mid-teardown.
        _transport.DestroyAll(stale);

        if (changed)
        {
            Log.Info("monitors: " + string.Join(", ", probed.Select(p =>
                $"{p.Info.Id} ({p.Info.FriendlyName}) responsive={p.Info.IsResponsive}")));
            MonitorsChanged?.Invoke();
        }

        return changed;
    }

    private async Task<Entry> BuildEntryAsync(PhysicalMonitorHandle handle, int index, int probeRetries)
    {
        // A display that just woke needs a moment before it answers DDC; without the retry it would
        // be written off as unresponsive and disappear from the app until the next restart.
        (bool success, uint min, uint current, uint max) = (false, 0u, 0u, 0u);
        for (int attempt = 0; attempt <= probeRetries; attempt++)
        {
            (success, min, current, max) =
                await GatedAsync(() => _transport.GetBrightnessAsync(handle.Handle)).ConfigureAwait(false);
            if (success)
                break;

            if (attempt < probeRetries)
                await Task.Delay(ProbeRetryDelaysMs[Math.Min(attempt, ProbeRetryDelaysMs.Length - 1)]).ConfigureAwait(false);
        }

        var id = MonitorIdentity.Resolve(handle.DevicePath, handle.Description, handle.DisplayNumber, index);

        var info = new MonitorInfo
        {
            Id = id,
            FriendlyName = BuildDisplayName(handle, index),
            DeviceName = handle.DeviceName,
            IsPrimary = handle.IsPrimary,
            Min = success ? min : 0,
            Current = success ? current : 0,
            Max = success ? max : 100,
            IsResponsive = success,
        };

        if (success)
        {
            var (cok, cmin, ccur, cmax) =
                await GatedAsync(() => _transport.GetContrastAsync(handle.Handle)).ConfigureAwait(false);
            if (cok && cmax > cmin)
            {
                info.SupportsContrast = true;
                info.ContrastMin = cmin;
                info.ContrastCurrent = ccur;
                info.ContrastMax = cmax;
            }
        }

        return new Entry(handle, info);
    }

    /// <summary>Re-probes monitors currently marked unresponsive. One bad read at startup used to
    /// hide a working display for the rest of the session.</summary>
    public async Task RetryUnresponsiveAsync()
    {
        List<Entry> dead;
        lock (_lock) dead = _monitors.Where(m => !m.Info.IsResponsive).ToList();

        if (dead.Count == 0)
            return;

        bool recovered = false;
        foreach (var entry in dead)
        {
            var (ok, min, current, max) =
                await GatedAsync(() => _transport.GetBrightnessAsync(entry.Handle.Handle)).ConfigureAwait(false);
            if (!ok)
                continue;

            entry.Info.Min = min;
            entry.Info.Current = current;
            entry.Info.Max = max;
            entry.Info.IsResponsive = true;
            recovered = true;
            Log.Info($"{entry.Info.Id}: recovered, now responsive");
        }

        if (recovered)
            MonitorsChanged?.Invoke();
    }

    /// <summary>Labels a monitor by its Windows display number ("Display 1"/"Display 2" — the same
    /// numbers Settings → Display shows), with a "main" marker for the primary. Falls back to a 1-based
    /// index when the display number can't be resolved.</summary>
    private static string BuildDisplayName(PhysicalMonitorHandle handle, int index)
    {
        var n = handle.DisplayNumber > 0 ? handle.DisplayNumber : index + 1;
        return handle.IsPrimary ? $"Display {n} · main" : $"Display {n}";
    }

    // ---- Intent generation -----------------------------------------------------

    /// <summary>Opens a new brightness intent and invalidates every older one. Anything still
    /// in flight from a previous intent aborts instead of writing a stale value.</summary>
    public long BeginIntent() => Interlocked.Increment(ref _generation);

    public bool IsCurrentIntent(long generation) => Interlocked.Read(ref _generation) == generation;

    // ---- Brightness ------------------------------------------------------------

    /// <summary>Interactive change (slider, hotkey, tray scroll): opens its own intent and skips the
    /// read-back so dragging stays responsive.</summary>
    public Task<bool> SetBrightnessPercentAsync(string monitorId, int percent) =>
        SetBrightnessPercentAsync(monitorId, percent, BeginIntent(), verify: false);

    public async Task<bool> SetBrightnessPercentAsync(string monitorId, int percent, long generation, bool verify)
    {
        Entry? entry;
        lock (_lock) entry = _monitors.FirstOrDefault(m => m.Info.Id == monitorId);

        if (entry is null || !entry.Info.IsResponsive)
            return false;

        percent = Math.Clamp(percent, 0, 100);
        var info = entry.Info;
        uint raw = ToRaw(percent, info.Min, info.Max);

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (!IsCurrentIntent(generation))
                {
                    Log.Info($"{monitorId}: apply {percent}% dropped — superseded");
                    return false;
                }

                bool ok = await _transport.SetBrightnessAsync(entry.Handle.Handle, raw).ConfigureAwait(false);

                if (ok && !verify)
                {
                    Commit(info, monitorId, raw, percent);
                    return true;
                }

                if (ok && await VerifyAsync(entry, percent).ConfigureAwait(false))
                {
                    Commit(info, monitorId, raw, percent);
                    return true;
                }

                if (attempt >= WriteRetryDelaysMs.Length)
                {
                    Log.Warn($"{monitorId}: brightness {percent}% did not take after {attempt} retries");
                    info.IsResponsive = false; // the periodic re-probe brings it back when it answers again
                    return false;
                }

                await Task.Delay(WriteRetryDelaysMs[attempt]).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Runs one native DDC call with exclusive access. A monitor has a single, slow
    /// command channel: overlapping calls block each other inside the driver, and a call that our
    /// timeout gave up on is still occupying that channel with a thread stuck behind it. Funnelling
    /// every read, write and capability query through here keeps that from piling up.</summary>
    private async Task<T> GatedAsync<T>(Func<Task<T>> nativeCall)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try { return await nativeCall().ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }

    private void Commit(MonitorInfo info, string monitorId, uint raw, int percent)
    {
        info.Current = raw;
        BrightnessChanged?.Invoke(monitorId, percent);
    }

    /// <summary>Reads the value back: a monitor mid display-mode switch acknowledges the write and
    /// then ignores it, which is how brightness got stuck at the game's level after quitting.</summary>
    private async Task<bool> VerifyAsync(Entry entry, int targetPercent)
    {
        var (ok, min, current, max) = await _transport.GetBrightnessAsync(entry.Handle.Handle).ConfigureAwait(false);
        if (!ok || max <= min)
            return false;

        entry.Info.Min = min;
        entry.Info.Max = max;
        var actual = (int)Math.Round((current - min) * 100.0 / (max - min));
        return Math.Abs(actual - targetPercent) <= VerifyTolerance;
    }

    internal static uint ToRaw(int percent, uint min, uint max) =>
        max > min ? (uint)Math.Round(min + Math.Clamp(percent, 0, 100) / 100.0 * (max - min)) : min;

    /// <summary>Applies a saved profile (idle, schedule block, or game) under one intent, verifying
    /// each write. Returns false if a newer intent superseded it partway through.</summary>
    public async Task<bool> ApplyProfileAsync(IReadOnlyDictionary<string, int> monitorBrightness, long generation)
    {
        foreach (var (monitorId, percent) in monitorBrightness)
        {
            if (!IsCurrentIntent(generation))
                return false;

            await SetBrightnessPercentAsync(monitorId, percent, generation, verify: true).ConfigureAwait(false);
        }

        return IsCurrentIntent(generation);
    }

    // ---- Contrast --------------------------------------------------------------

    public async Task<bool> SetContrastPercentAsync(string monitorId, int percent)
    {
        Entry? entry;
        lock (_lock) entry = _monitors.FirstOrDefault(m => m.Info.Id == monitorId);

        if (entry is null || !entry.Info.SupportsContrast)
            return false;

        percent = Math.Clamp(percent, 0, 100);
        var info = entry.Info;
        uint raw = ToRaw(percent, info.ContrastMin, info.ContrastMax);

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool ok = await _transport.SetContrastAsync(entry.Handle.Handle, raw).ConfigureAwait(false);
            if (ok)
                info.ContrastCurrent = raw;
            return ok;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // ---- Power -----------------------------------------------------------------

    /// <summary>Displays the app currently has switched off. They are gone from
    /// <see cref="Monitors"/> — Windows no longer has them on the desktop — so the UI needs this
    /// list to offer a way back.</summary>
    public IReadOnlyList<DetachedDisplay> DetachedDisplays
    {
        get { lock (_lock) return _detached.ToList(); }
    }

    /// <summary>Restores the list persisted from a previous run, so a display that was off when the
    /// app closed can still be switched back on.</summary>
    public void SeedDetached(IEnumerable<DetachedDisplay> saved)
    {
        lock (_lock)
        {
            _detached.Clear();
            _detached.AddRange(saved.Where(d => d.IsRestorable));
        }
    }

    /// <summary>Raised when a display is switched off or back on, so the caller can persist the list.</summary>
    public event Action? DetachedChanged;

    public Task<bool> SetPowerAsync(string monitorId, bool on) =>
        on ? TurnOnAsync(monitorId) : TurnOffAsync(monitorId);

    /// <summary>
    /// Switches a display off by taking it off the Windows desktop: the GPU stops driving that
    /// output, the monitor loses signal and powers its panel down.
    ///
    /// This is deliberately not done over DDC. The DPMS power modes are accepted and then ignored
    /// while Windows keeps the output live — the screen blinks and comes straight back — and the
    /// "hard off" mode takes the monitor's DDC circuit down with it, leaving its physical button as
    /// the only way back. Detaching is the only route that both sticks and is reversible.
    /// </summary>
    public async Task<bool> TurnOffAsync(string monitorId)
    {
        MonitorInfo? info;
        int poweredOn;
        lock (_lock)
        {
            info = _monitors.FirstOrDefault(m => m.Info.Id == monitorId)?.Info;
            poweredOn = _monitors.Count;
        }

        if (info is null)
            return false;

        if (info.IsPrimary)
        {
            Log.Warn($"{monitorId}: refusing to switch off the primary display");
            return false;
        }

        if (poweredOn <= 1)
        {
            Log.Warn($"{monitorId}: refusing to switch off the only display that is on");
            return false;
        }

        var saved = await Task.Run(() => _attacher.CaptureMode(info.DeviceName)).ConfigureAwait(false);
        if (saved is null)
        {
            Log.Warn($"{monitorId}: could not capture the current mode, refusing to switch off");
            return false;
        }

        saved.Id = info.Id;
        saved.FriendlyName = info.FriendlyName;

        // Recorded before the change: if the detach half-succeeds or the app dies mid-way, the
        // display must still be findable and restorable.
        lock (_lock)
        {
            _detached.RemoveAll(d => d.Id == saved.Id);
            _detached.Add(saved);
        }
        DetachedChanged?.Invoke();

        var ok = await Task.Run(() => _attacher.Detach(info.DeviceName)).ConfigureAwait(false);
        if (!ok)
        {
            lock (_lock) _detached.RemoveAll(d => d.Id == saved.Id);
            DetachedChanged?.Invoke();
            return false;
        }

        Log.Info($"{monitorId} ({info.FriendlyName}): switched off");
        await RefreshAsync(probeRetries: 0).ConfigureAwait(false);
        MonitorsChanged?.Invoke();
        return true;
    }

    /// <summary>Puts a switched-off display back exactly where it was.</summary>
    public async Task<bool> TurnOnAsync(string monitorId)
    {
        DetachedDisplay? saved;
        lock (_lock) saved = _detached.FirstOrDefault(d => d.Id == monitorId);

        if (saved is null)
            return false;

        var ok = await Task.Run(() => _attacher.Attach(saved)).ConfigureAwait(false);
        if (!ok)
            return false;

        lock (_lock) _detached.RemoveAll(d => d.Id == monitorId);
        DetachedChanged?.Invoke();

        Log.Info($"{monitorId} ({saved.FriendlyName}): switched back on");

        // The display needs a moment to come up before it will answer DDC.
        await Task.Delay(1200).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        MonitorsChanged?.Invoke();
        return true;
    }

    /// <summary>Brings back every display the app switched off. Called from the tray menu and on
    /// exit, so a screen is never left dark with nothing running to turn it back on.</summary>
    public async Task PowerOnAllAsync()
    {
        List<DetachedDisplay> off;
        lock (_lock) off = _detached.ToList();

        foreach (var display in off)
            await TurnOnAsync(display.Id).ConfigureAwait(false);
    }

    /// <summary>Synchronous best-effort wake for shutdown paths, where awaiting is not an option.</summary>
    public void PowerOnAllBlocking(int timeoutMs = 4000)
    {
        try { PowerOnAllAsync().Wait(timeoutMs); }
        catch (Exception ex) { Log.Warn($"power-on-all during shutdown failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<PhysicalMonitorHandle> handles;
        lock (_lock)
        {
            handles = _monitors.Select(m => m.Handle).ToList();
            _monitors.Clear();
        }

        _transport.DestroyAll(handles);
        _writeGate.Dispose();
    }
}
