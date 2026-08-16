namespace BrightnessControl.Services;

/// <summary>
/// The single seam between <see cref="MonitorService"/> and real DDC/CI hardware. The app uses
/// <see cref="Dxva2MonitorTransport"/>; tests substitute a fake so the ordering, retry, and
/// hot-plug logic can be exercised without a monitor attached.
/// </summary>
internal interface IMonitorTransport
{
    List<PhysicalMonitorHandle> Enumerate();
    void DestroyAll(IEnumerable<PhysicalMonitorHandle> handles);

    Task<(bool Success, uint Min, uint Current, uint Max)> GetBrightnessAsync(IntPtr handle);
    Task<bool> SetBrightnessAsync(IntPtr handle, uint raw);

    Task<(bool Success, uint Min, uint Current, uint Max)> GetContrastAsync(IntPtr handle);
    Task<bool> SetContrastAsync(IntPtr handle, uint raw);
}

internal sealed class Dxva2MonitorTransport : IMonitorTransport
{
    public List<PhysicalMonitorHandle> Enumerate() => MonitorController.EnumeratePhysicalMonitors();

    public void DestroyAll(IEnumerable<PhysicalMonitorHandle> handles) => MonitorController.DestroyAll(handles);

    public Task<(bool Success, uint Min, uint Current, uint Max)> GetBrightnessAsync(IntPtr handle) =>
        MonitorController.TryGetBrightnessAsync(handle);

    public Task<bool> SetBrightnessAsync(IntPtr handle, uint raw) =>
        MonitorController.TrySetBrightnessAsync(handle, raw);

    public Task<(bool Success, uint Min, uint Current, uint Max)> GetContrastAsync(IntPtr handle) =>
        MonitorController.TryGetContrastAsync(handle);

    public Task<bool> SetContrastAsync(IntPtr handle, uint raw) =>
        MonitorController.TrySetContrastAsync(handle, raw);
}
