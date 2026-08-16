using BrightnessControl.Services;
using Xunit.Abstractions;

namespace BrightnessControl.Tests;

/// <summary>
/// Walks the checklist steps that no fake can cover: real round-trips to the monitors on this
/// machine. Every test restores what it changed before it finishes.
/// </summary>
[Trait("Category", "Hardware")]
public sealed class HardwareSmokeTests
{
    private readonly ITestOutputHelper _out;

    public HardwareSmokeTests(ITestOutputHelper output) => _out = output;

    private async Task<MonitorService> RealMonitorsAsync()
    {
        var service = new MonitorService();
        await service.InitializeAsync();
        return service;
    }

    [HardwareFact]
    public async Task Reports_what_is_actually_attached()
    {
        using var service = await RealMonitorsAsync();

        Assert.NotEmpty(service.Monitors);

        foreach (var m in service.Monitors)
        {
            _out.WriteLine($"{m.FriendlyName}");
            _out.WriteLine($"   id         {m.Id}");
            _out.WriteLine($"   device     {m.DeviceName}   primary={m.IsPrimary}");
            _out.WriteLine($"   brightness {m.CurrentPercent}%  (raw {m.Current} in {m.Min}..{m.Max})");
            _out.WriteLine($"   responsive {m.IsResponsive}   contrast={m.SupportsContrast}   canSwitchOff={m.SupportsPower}");

            Assert.True(m.IsResponsive, $"{m.FriendlyName} did not answer DDC/CI");
            Assert.False(MonitorIdentity.IsLegacy(m.Id), $"{m.FriendlyName} has no stable identity");
        }
    }

    [HardwareFact]
    public async Task A_verified_brightness_write_round_trips()
    {
        using var service = await RealMonitorsAsync();
        var monitor = service.Monitors.First(m => m.IsResponsive);
        var original = monitor.CurrentPercent;

        // Far enough from the current value that the read-back can't pass by accident.
        var target = original <= 50 ? original + 20 : original - 20;

        try
        {
            Assert.True(
                await service.SetBrightnessPercentAsync(monitor.Id, target, service.BeginIntent(), verify: true),
                $"verified write of {target}% did not take on {monitor.FriendlyName}");

            _out.WriteLine($"{monitor.FriendlyName}: {original}% -> {target}% verified");
        }
        finally
        {
            await service.SetBrightnessPercentAsync(monitor.Id, original, service.BeginIntent(), verify: true);
            _out.WriteLine($"{monitor.FriendlyName}: restored to {original}%");
        }
    }

    /// <summary>Diagnostic: whether each screen has a display path we can deactivate.</summary>
    [HardwareFact]
    public void Every_screen_has_a_display_path_that_can_be_switched_off()
    {
        var attacher = new DisplayAttacher();

        foreach (var h in MonitorController.EnumeratePhysicalMonitors())
        {
            var found = attacher.CaptureMode(h.DeviceName);
            _out.WriteLine($"{h.DeviceName}: display path {(found is null ? "NOT FOUND" : "found")}  primary={h.IsPrimary}");
            Assert.NotNull(found);
        }
    }

    /// <summary>
    /// The one that actually matters: a secondary screen goes dark and comes back, entirely from
    /// software. Switching off means taking the display off the Windows desktop — the GPU stops
    /// driving that output, so the panel powers down and, unlike a DDC power-off, Windows can put
    /// it straight back.
    /// </summary>
    [HardwareFact]
    public async Task A_secondary_display_switches_off_and_back_on()
    {
        using var service = await RealMonitorsAsync();

        var target = service.Monitors.FirstOrDefault(m => m.SupportsPower);
        Assert.NotNull(target);

        var id = target!.Id;
        _out.WriteLine($"target: {target.FriendlyName} ({id}) on {target.DeviceName}");

        try
        {
            Assert.True(await service.TurnOffAsync(id), "switching the display off was rejected");
            _out.WriteLine("off        -> sent; the screen should be dark and stay dark");

            var saved = Assert.Single(service.DetachedDisplays);
            _out.WriteLine($"recorded   -> {saved.FriendlyName} on {saved.DeviceName}");
            Assert.DoesNotContain(service.Monitors, m => m.Id == id);

            // Long enough to tell "stayed off" from "blinked and woke straight back up".
            await Task.Delay(6000);

            Assert.True(await service.TurnOnAsync(id), "switching the display back on failed");
            _out.WriteLine("on         -> sent");

            await Task.Delay(2000);
            await service.RefreshAsync();

            var back = service.Monitors.FirstOrDefault(m => m.Id == id);
            Assert.NotNull(back);
            _out.WriteLine($"restored   -> {back!.FriendlyName} at {back.CurrentPercent}%, responsive={back.IsResponsive}");
            Assert.Empty(service.DetachedDisplays);
        }
        finally
        {
            // Whatever went wrong above, never leave a screen dark.
            await service.PowerOnAllAsync();
        }
    }
}
