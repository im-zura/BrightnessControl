using BrightnessControl.Models;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

public class MonitorServiceTests
{
    private const string PathA = @"\\?\DISPLAY#GSM5B09#5&1a2b3c4d&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathB = @"\\?\DISPLAY#DEL41A2#5&9f8e7d6c&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static async Task<(MonitorService Service, FakeMonitorTransport Transport)> TwoMonitorsAsync()
    {
        var (service, transport, _) = await TwoMonitorsWithAttacherAsync();
        return (service, transport);
    }

    private static async Task<(MonitorService Service, FakeMonitorTransport Transport, FakeDisplayAttacher Attacher)>
        TwoMonitorsWithAttacherAsync()
    {
        var transport = new FakeMonitorTransport();
        transport.AddMonitor(PathA, @"\\.\DISPLAY1", 1, primary: true);
        transport.AddMonitor(PathB, @"\\.\DISPLAY2", 2, primary: false);

        var attacher = new FakeDisplayAttacher(transport);
        var service = new MonitorService(transport, attacher);
        await service.InitializeAsync();
        return (service, transport, attacher);
    }

    [Fact]
    public async Task Ids_come_from_the_device_path_not_enumeration_order()
    {
        var (service, _) = await TwoMonitorsAsync();

        Assert.Equal("mon-gsm5b09-5-1a2b3c4d-0-uid4353", service.Monitors[0].Id);
        Assert.Equal("mon-del41a2-5-9f8e7d6c-0-uid4355", service.Monitors[1].Id);
    }

    [Fact]
    public async Task Id_survives_a_monitor_dropping_out_and_coming_back()
    {
        var transport = new FakeMonitorTransport();
        var primary = transport.AddMonitor(PathA, @"\\.\DISPLAY1", 1, primary: true);
        transport.AddMonitor(PathB, @"\\.\DISPLAY2", 2, primary: false);

        var service = new MonitorService(transport);
        await service.InitializeAsync();
        var secondaryId = service.Monitors[1].Id;

        // The primary goes away, so the secondary is now the only — and first — monitor enumerated.
        transport.Detach(primary);
        await service.RefreshAsync(probeRetries: 0);

        Assert.Single(service.Monitors);
        Assert.Equal(secondaryId, service.Monitors[0].Id);
    }

    [Fact]
    public async Task Refresh_replaces_stale_handles_after_a_display_wakes()
    {
        var (service, transport) = await TwoMonitorsAsync();
        var before = service.Monitors[1].Id;

        transport.ReissueHandles(); // same displays, new handles — what happens after a power cycle
        await service.RefreshAsync(probeRetries: 0);

        var id = service.Monitors[1].Id;
        Assert.Equal(before, id);

        transport.Writes.Clear();
        Assert.True(await service.SetBrightnessPercentAsync(id, 42));
        Assert.Equal(new IntPtr(103), transport.Writes[0].Handle); // wrote to the reissued handle
    }

    [Fact]
    public async Task A_superseded_apply_never_reaches_the_monitor()
    {
        // The bug this guards: a game profile still resolving its monitor when the game exits used
        // to land on top of the idle brightness that had already been restored.
        var (service, transport) = await TwoMonitorsAsync();
        var id = service.Monitors[0].Id;

        var staleIntent = service.BeginIntent();
        service.BeginIntent(); // something newer happened in the meantime

        transport.Writes.Clear();
        var applied = await service.SetBrightnessPercentAsync(id, 50, staleIntent, verify: true);

        Assert.False(applied);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task A_current_apply_still_goes_through()
    {
        var (service, transport) = await TwoMonitorsAsync();
        var id = service.Monitors[0].Id;

        transport.Writes.Clear();
        var generation = service.BeginIntent();

        Assert.True(await service.SetBrightnessPercentAsync(id, 50, generation, verify: true));
        Assert.Single(transport.Writes);
        Assert.Equal(50u, transport.Writes[0].Raw);
    }

    [Fact]
    public async Task A_write_the_monitor_swallowed_is_retried_until_it_sticks()
    {
        var (service, transport) = await TwoMonitorsAsync();
        var id = service.Monitors[0].Id;

        transport.SwallowNextWrites(0, 2); // acknowledged but ignored, twice
        transport.Writes.Clear();

        Assert.True(await service.SetBrightnessPercentAsync(id, 70, service.BeginIntent(), verify: true));
        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(70, service.Monitors[0].CurrentPercent);
    }

    [Fact]
    public async Task Interactive_writes_skip_the_read_back()
    {
        var (service, transport) = await TwoMonitorsAsync();
        var id = service.Monitors[0].Id;

        transport.SwallowNextWrites(0, 2);
        transport.Writes.Clear();

        // No verification: one write, reported as success. Dragging a slider must stay responsive.
        Assert.True(await service.SetBrightnessPercentAsync(id, 70));
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task A_monitor_that_fails_its_first_read_is_recovered_later()
    {
        var transport = new FakeMonitorTransport();
        transport.AddMonitor(PathA, @"\\.\DISPLAY1", 1, primary: true, readable: false);

        var service = new MonitorService(transport);
        await service.InitializeAsync();
        Assert.False(service.Monitors[0].IsResponsive);

        transport.SetReadable(0, true);
        await service.RetryUnresponsiveAsync();

        Assert.True(service.Monitors[0].IsResponsive);
    }

    [Fact]
    public async Task Profile_values_land_on_the_monitors_named_in_the_profile()
    {
        var (service, _) = await TwoMonitorsAsync();
        var profile = new Dictionary<string, int>
        {
            [service.Monitors[0].Id] = 20,
            [service.Monitors[1].Id] = 80,
        };

        Assert.True(await service.ApplyProfileAsync(profile, service.BeginIntent()));
        Assert.Equal(20, service.Monitors[0].CurrentPercent);
        Assert.Equal(80, service.Monitors[1].CurrentPercent);
    }

    [Fact]
    public async Task Only_the_primary_display_lacks_a_power_button()
    {
        var (service, _) = await TwoMonitorsAsync();

        Assert.False(service.Monitors[0].SupportsPower); // primary
        Assert.True(service.Monitors[1].SupportsPower);
    }

    [Fact]
    public async Task Switching_a_display_off_takes_it_off_the_desktop_and_remembers_how_to_restore_it()
    {
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();
        var id = service.Monitors[1].Id;

        Assert.True(await service.TurnOffAsync(id));

        Assert.Equal(new[] { @"\\.\DISPLAY2" }, attacher.Detached);
        Assert.Single(service.Monitors);                       // gone from the desktop
        var saved = Assert.Single(service.DetachedDisplays);
        Assert.Equal(id, saved.Id);
        Assert.True(saved.IsRestorable);
    }

    [Fact]
    public async Task Switching_it_back_on_restores_the_display_and_clears_the_record()
    {
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();
        var id = service.Monitors[1].Id;

        await service.TurnOffAsync(id);
        Assert.True(await service.TurnOnAsync(id));

        Assert.Equal(new[] { @"\\.\DISPLAY2" }, attacher.Attached);
        Assert.Equal(2, service.Monitors.Count);
        Assert.Empty(service.DetachedDisplays);
    }

    [Fact]
    public async Task A_failed_detach_leaves_no_record_behind()
    {
        // Otherwise the display would show as "off" in the panel while it is plainly still on.
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();
        attacher.FailDetach = true;

        Assert.False(await service.TurnOffAsync(service.Monitors[1].Id));

        Assert.Empty(service.DetachedDisplays);
        Assert.Equal(2, service.Monitors.Count);
    }

    [Fact]
    public async Task A_display_whose_mode_cannot_be_captured_is_not_switched_off()
    {
        // Without a saved mode there would be no way to put it back where it was.
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();
        attacher.FailCapture = true;

        Assert.False(await service.TurnOffAsync(service.Monitors[1].Id));

        Assert.Empty(attacher.Detached);
        Assert.Equal(2, service.Monitors.Count);
    }

    [Fact]
    public async Task The_primary_display_cannot_be_switched_off()
    {
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();

        Assert.False(await service.TurnOffAsync(service.Monitors[0].Id));

        Assert.Empty(attacher.Detached);
    }

    [Fact]
    public async Task The_last_display_still_on_cannot_be_switched_off()
    {
        var transport = new FakeMonitorTransport();
        transport.AddMonitor(PathB, @"\\.\DISPLAY2", 2, primary: false);
        var attacher = new FakeDisplayAttacher(transport);

        var service = new MonitorService(transport, attacher);
        await service.InitializeAsync();

        Assert.False(await service.TurnOffAsync(service.Monitors[0].Id));
        Assert.Empty(attacher.Detached);
    }

    [Fact]
    public async Task Turning_everything_back_on_restores_every_switched_off_display()
    {
        var (service, _, attacher) = await TwoMonitorsWithAttacherAsync();
        await service.TurnOffAsync(service.Monitors[1].Id);

        await service.PowerOnAllAsync();

        Assert.Empty(service.DetachedDisplays);
        Assert.Equal(2, service.Monitors.Count);
        Assert.Single(attacher.Attached);
    }

    [Fact]
    public async Task A_display_switched_off_in_a_previous_session_can_still_be_restored()
    {
        // The app may have been closed — or have crashed — while a screen was off. The saved record
        // is the only thing that can bring it back.
        var transport = new FakeMonitorTransport();
        transport.AddMonitor(PathA, @"\\.\DISPLAY1", 1, primary: true);
        var attacher = new FakeDisplayAttacher(transport);

        var service = new MonitorService(transport, attacher);
        service.SeedDetached(new[]
        {
            new DetachedDisplay
            {
                Id = "mon-del41a2-5-9f8e7d6c-0-uid4355", FriendlyName = "Display 2",
                DeviceName = @"\\.\DISPLAY2",
            },
        });
        await service.InitializeAsync();

        await service.PowerOnAllAsync();

        Assert.Equal(new[] { @"\\.\DISPLAY2" }, attacher.Attached);
        Assert.Empty(service.DetachedDisplays);
    }

    [Fact]
    public async Task Switching_a_display_off_reports_it_so_the_state_can_be_persisted()
    {
        var (service, _, _) = await TwoMonitorsWithAttacherAsync();
        int notifications = 0;
        service.DetachedChanged += () => notifications++;

        await service.TurnOffAsync(service.Monitors[1].Id);
        await service.TurnOnAsync(service.DetachedDisplays.Count > 0 ? service.DetachedDisplays[0].Id : "");

        Assert.True(notifications >= 2);
    }

    [Fact]
    public async Task A_display_that_is_off_is_left_out_of_profile_applies()
    {
        var (service, _, _) = await TwoMonitorsWithAttacherAsync();

        var off = service.Monitors[1].Id;
        await service.TurnOffAsync(off);

        // It is no longer part of the desktop, so a profile naming it simply finds nothing to write.
        Assert.True(await service.ApplyProfileAsync(new Dictionary<string, int> { [off] = 90 }, service.BeginIntent()));
        Assert.DoesNotContain(service.Monitors, m => m.Id == off);
    }

    [Theory]
    [InlineData(0, 0u, 100u, 0u)]
    [InlineData(100, 0u, 100u, 100u)]
    [InlineData(50, 20u, 80u, 50u)]   // non-zero minimum
    [InlineData(50, 0u, 40u, 20u)]    // maximum below 100
    [InlineData(70, 10u, 10u, 10u)]   // degenerate range: clamp to min instead of dividing by zero
    public void Percent_maps_onto_the_monitors_native_range(int percent, uint min, uint max, uint expected)
    {
        Assert.Equal(expected, MonitorService.ToRaw(percent, min, max));
    }
}
