using System.Diagnostics;
using BrightnessControl.Models;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

/// <summary>In-memory stand-in for real DDC/CI hardware. Records every write so ordering, retry and
/// "did this even reach the monitor" can be asserted without a display attached.</summary>
internal sealed class FakeMonitorTransport : IMonitorTransport
{
    private readonly Dictionary<IntPtr, State> _state = new();
    private readonly List<PhysicalMonitorHandle> _handles = new();

    private sealed class State
    {
        public uint Min = 0, Current, Max = 100;
        public uint ContrastMin = 0, ContrastCurrent, ContrastMax = 100;
        public bool Readable = true;
        /// <summary>Writes that report success but leave the value unchanged — what a monitor does
        /// while its display mode is switching.</summary>
        public int SwallowWrites;
    }

    public List<(IntPtr Handle, uint Raw)> Writes { get; } = new();

    public PhysicalMonitorHandle AddMonitor(
        string devicePath, string deviceName = @"\\.\DISPLAY1", int displayNumber = 1, bool primary = true,
        uint current = 50, bool readable = true)
    {
        var handle = new PhysicalMonitorHandle(
            new IntPtr(_handles.Count + 1), $"Fake {displayNumber}", displayNumber, primary, deviceName, devicePath);

        _handles.Add(handle);
        _state[handle.Handle] = new State { Current = current, Readable = readable };
        return handle;
    }

    /// <summary>Simulates re-enumeration after a display woke up: same monitors, brand new handles.</summary>
    public void ReissueHandles()
    {
        var offset = _handles.Count + 100;
        for (int i = 0; i < _handles.Count; i++)
        {
            var old = _handles[i];
            var fresh = old with { Handle = new IntPtr(offset + i) };
            _state[fresh.Handle] = _state[old.Handle];
            _state.Remove(old.Handle);
            _handles[i] = fresh;
        }
    }

    public void Detach(PhysicalMonitorHandle handle) => _handles.RemoveAll(h => h.DevicePath == handle.DevicePath);

    /// <summary>Takes a display out of enumeration, as detaching it from the desktop really does.</summary>
    public void RemoveByDeviceName(string deviceName)
    {
        var found = _handles.FirstOrDefault(h => h.DeviceName == deviceName);
        if (found is null)
            return;

        _handles.Remove(found);
        _removed[deviceName] = found;
    }

    public void RestoreByDeviceName(string deviceName)
    {
        if (_removed.Remove(deviceName, out var handle))
            _handles.Add(handle);
    }

    private readonly Dictionary<string, PhysicalMonitorHandle> _removed = new();

    public void SetReadable(int index, bool readable) => _state[_handles[index].Handle].Readable = readable;

    public void SwallowNextWrites(int index, int count) => _state[_handles[index].Handle].SwallowWrites = count;

    public List<PhysicalMonitorHandle> Enumerate() => _handles.ToList();

    public void DestroyAll(IEnumerable<PhysicalMonitorHandle> handles) { }

    public Task<(bool Success, uint Min, uint Current, uint Max)> GetBrightnessAsync(IntPtr handle)
    {
        if (!_state.TryGetValue(handle, out var s) || !s.Readable)
            return Task.FromResult((false, 0u, 0u, 0u));

        return Task.FromResult((true, s.Min, s.Current, s.Max));
    }

    public Task<bool> SetBrightnessAsync(IntPtr handle, uint raw)
    {
        Writes.Add((handle, raw));

        if (!_state.TryGetValue(handle, out var s) || !s.Readable)
            return Task.FromResult(false);

        if (s.SwallowWrites > 0)
        {
            s.SwallowWrites--;
            return Task.FromResult(true); // acknowledged, ignored — exactly the failure mode we retry for
        }

        s.Current = raw;
        return Task.FromResult(true);
    }

    public Task<(bool Success, uint Min, uint Current, uint Max)> GetContrastAsync(IntPtr handle)
    {
        if (!_state.TryGetValue(handle, out var s) || !s.Readable)
            return Task.FromResult((false, 0u, 0u, 0u));

        return Task.FromResult((true, s.ContrastMin, s.ContrastCurrent, s.ContrastMax));
    }

    public Task<bool> SetContrastAsync(IntPtr handle, uint raw)
    {
        if (!_state.TryGetValue(handle, out var s))
            return Task.FromResult(false);

        s.ContrastCurrent = raw;
        return Task.FromResult(true);
    }

}

/// <summary>
/// Stands in for taking a display off the Windows desktop. Detaching really does remove it from
/// enumeration, so the fake removes it from the transport too — otherwise tests would never see the
/// situation the UI has to handle: a switched-off display that is no longer in the monitor list.
/// </summary>
internal sealed class FakeDisplayAttacher : IDisplayAttacher
{
    private readonly FakeMonitorTransport _transport;

    public FakeDisplayAttacher(FakeMonitorTransport transport) => _transport = transport;

    public bool FailDetach { get; set; }
    public bool FailAttach { get; set; }
    public bool FailCapture { get; set; }

    public List<string> Detached { get; } = new();
    public List<string> Attached { get; } = new();

    public DetachedDisplay? CaptureMode(string deviceName)
    {
        if (FailCapture)
            return null;

        return new DetachedDisplay { DeviceName = deviceName };
    }

    public bool Detach(string deviceName)
    {
        if (FailDetach)
            return false;

        Detached.Add(deviceName);
        _transport.RemoveByDeviceName(deviceName);
        return true;
    }

    public bool Attach(DetachedDisplay saved)
    {
        if (FailAttach)
            return false;

        Attached.Add(saved.DeviceName);
        _transport.RestoreByDeviceName(saved.DeviceName);
        return true;
    }
}

/// <summary>Scriptable process lookup: tests decide what is "running" and when.</summary>
internal sealed class FakeProcessProbe : IProcessProbe
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Throw { get; set; }
    public List<FakeWatchedProcess> Handed { get; } = new();

    public IReadOnlyList<IWatchedProcess> ByBaseName(string baseName)
    {
        if (Throw)
            throw new InvalidOperationException("probe exploded");

        if (!Running.Contains(baseName))
            return Array.Empty<IWatchedProcess>();

        var p = new FakeWatchedProcess(baseName);
        Handed.Add(p);
        return new IWatchedProcess[] { p };
    }
}

internal sealed class FakeWatchedProcess : IWatchedProcess
{
    private Action? _onExit;

    public FakeWatchedProcess(string name) => Name = name;

    public string Name { get; }
    public int Id => 4242;
    public Process? Underlying => null;
    public bool Disposed { get; private set; }

    /// <summary>Mimics a protected process whose exit hook can't be attached.</summary>
    public bool HookRefused { get; set; }

    public bool TryHookExit(Action onExit)
    {
        if (HookRefused)
            return false;

        _onExit = onExit;
        return true;
    }

    /// <summary>Fires the exit callback the watcher registered, as Windows would.</summary>
    public void SignalExit() => _onExit?.Invoke();

    public void Dispose() => Disposed = true;
}
