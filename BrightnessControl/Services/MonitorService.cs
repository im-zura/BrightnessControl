using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// High-level monitor API: enumerates physical monitors once, caches their handles for the
/// app's lifetime, and exposes 0-100% brightness scaled from each monitor's native min/max range.
/// </summary>
internal sealed class MonitorService : IDisposable
{
    private readonly List<(PhysicalMonitorHandle Handle, MonitorInfo Info)> _monitors = new();

    public IReadOnlyList<MonitorInfo> Monitors => _monitors.Select(m => m.Info).ToList();

    /// <summary>Raised (monitorId, percent) after a successful brightness write, from any source
    /// (hotkey, tray scroll, slider). Lets an open flyout mirror external changes live.</summary>
    public event Action<string, int>? BrightnessChanged;

    public async Task InitializeAsync()
    {
        DisposeHandles();
        _monitors.Clear();

        var handles = MonitorController.EnumeratePhysicalMonitors();

        for (int i = 0; i < handles.Count; i++)
        {
            var handle = handles[i];
            var (success, min, current, max) = await MonitorController.TryGetBrightnessAsync(handle.Handle);

            var info = new MonitorInfo
            {
                Id = $"monitor-{i}",
                FriendlyName = BuildDisplayName(handle, i),
                DeviceName = handle.DeviceName,
                Min = success ? min : 0,
                Current = success ? current : 0,
                Max = success ? max : 100,
                IsResponsive = success,
            };

            if (success)
            {
                var (cok, cmin, ccur, cmax) = await MonitorController.TryGetContrastAsync(handle.Handle);
                if (cok && cmax > cmin)
                {
                    info.SupportsContrast = true;
                    info.ContrastMin = cmin;
                    info.ContrastCurrent = ccur;
                    info.ContrastMax = cmax;
                }
            }

            _monitors.Add((handle, info));
        }
    }

    /// <summary>Labels a monitor by its Windows display number ("Display 1"/"Display 2" — the same
    /// numbers Settings → Display shows), with a "main" marker for the primary. Falls back to a 1-based
    /// index when the display number can't be resolved.</summary>
    private static string BuildDisplayName(PhysicalMonitorHandle handle, int index)
    {
        var n = handle.DisplayNumber > 0 ? handle.DisplayNumber : index + 1;
        return handle.IsPrimary ? $"Display {n} · main" : $"Display {n}";
    }

    public async Task<bool> SetBrightnessPercentAsync(string monitorId, int percent)
    {
        var entry = _monitors.FirstOrDefault(m => m.Info.Id == monitorId);
        if (entry.Info is null || !entry.Info.IsResponsive)
            return false;

        percent = Math.Clamp(percent, 0, 100);
        var info = entry.Info;
        uint raw = (uint)Math.Round(info.Min + percent / 100.0 * (info.Max - info.Min));

        bool ok = await MonitorController.TrySetBrightnessAsync(entry.Handle.Handle, raw);
        if (ok)
        {
            info.Current = raw;
            BrightnessChanged?.Invoke(monitorId, percent);
        }

        return ok;
    }

    public async Task ApplyProfileAsync(Dictionary<string, int> monitorBrightness)
    {
        foreach (var (monitorId, percent) in monitorBrightness)
            await SetBrightnessPercentAsync(monitorId, percent);
    }

    public async Task<bool> SetContrastPercentAsync(string monitorId, int percent)
    {
        var entry = _monitors.FirstOrDefault(m => m.Info.Id == monitorId);
        if (entry.Info is null || !entry.Info.SupportsContrast)
            return false;

        percent = Math.Clamp(percent, 0, 100);
        var info = entry.Info;
        uint raw = (uint)Math.Round(info.ContrastMin + percent / 100.0 * (info.ContrastMax - info.ContrastMin));

        bool ok = await MonitorController.TrySetContrastAsync(entry.Handle.Handle, raw);
        if (ok)
            info.ContrastCurrent = raw;

        return ok;
    }

    private void DisposeHandles()
    {
        if (_monitors.Count > 0)
            MonitorController.DestroyAll(_monitors.Select(m => m.Handle));
    }

    public void Dispose() => DisposeHandles();
}
