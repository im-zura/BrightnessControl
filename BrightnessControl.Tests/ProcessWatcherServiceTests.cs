using BrightnessControl.Services;

namespace BrightnessControl.Tests;

public class ProcessWatcherServiceTests
{
    private static (ProcessWatcherService Watcher, FakeProcessProbe Probe, List<string> Started, List<string> Stopped) Build()
    {
        var probe = new FakeProcessProbe();
        var watcher = new ProcessWatcherService(probe);
        watcher.UpdateTrackedProcessNames(new[] { "RDR2.exe" });

        var started = new List<string>();
        var stopped = new List<string>();
        watcher.ProcessStarted += (name, _) => started.Add(name);
        watcher.ProcessStopped += stopped.Add;

        return (watcher, probe, started, stopped);
    }

    [Fact]
    public void A_game_start_is_reported_once()
    {
        var (watcher, probe, started, _) = Build();

        watcher.Poll();
        Assert.Empty(started);

        probe.Running.Add("RDR2");
        watcher.Poll();
        watcher.Poll();
        watcher.Poll();

        Assert.Equal(new[] { "RDR2.exe" }, started);
    }

    [Fact]
    public void A_game_stop_is_reported_once()
    {
        var (watcher, probe, _, stopped) = Build();

        probe.Running.Add("RDR2");
        watcher.Poll();

        probe.Running.Clear();
        watcher.Poll();
        watcher.Poll();

        Assert.Equal(new[] { "RDR2.exe" }, stopped);
    }

    [Fact]
    public void The_exit_event_and_the_poll_tick_do_not_both_report_the_same_stop()
    {
        var (watcher, probe, _, stopped) = Build();

        probe.Running.Add("RDR2");
        watcher.Poll();

        // Windows raises the exit event, then the next tick also finds nothing running.
        probe.Running.Clear();
        probe.Handed[0].SignalExit();
        watcher.Poll();

        Assert.Single(stopped);
    }

    [Fact]
    public void A_protected_game_whose_exit_hook_is_refused_is_still_detected_by_polling()
    {
        var probe = new FakeProcessProbe();
        var watcher = new ProcessWatcherService(probe);
        watcher.UpdateTrackedProcessNames(new[] { "aces.exe" });

        var stopped = new List<string>();
        watcher.ProcessStopped += stopped.Add;

        probe.Running.Add("aces");
        watcher.Poll();
        probe.Handed[0].HookRefused = true;

        probe.Running.Clear();
        watcher.Poll();

        Assert.Equal(new[] { "aces.exe" }, stopped);
    }

    [Fact]
    public void A_failing_probe_does_not_take_the_poll_tick_down()
    {
        // Regression: an exception here runs on a Timer thread, where it terminates the process —
        // the tray icon disappears and monitors stay at whatever the game last set.
        var (watcher, probe, _, _) = Build();
        probe.Throw = true;

        var ex = Record.Exception(() => watcher.Poll());

        Assert.Null(ex);
    }

    [Fact]
    public void A_running_game_appears_in_CurrentlyRunning_until_it_stops()
    {
        var (watcher, probe, _, _) = Build();

        probe.Running.Add("RDR2");
        watcher.Poll();
        Assert.Equal(new[] { "RDR2.exe" }, watcher.CurrentlyRunning);

        probe.Running.Clear();
        watcher.Poll();
        Assert.Empty(watcher.CurrentlyRunning);
    }

    [Fact]
    public void Dropping_a_profile_stops_tracking_without_reporting_a_stop()
    {
        var (watcher, probe, _, stopped) = Build();

        probe.Running.Add("RDR2");
        watcher.Poll();

        watcher.UpdateTrackedProcessNames(Array.Empty<string>());
        watcher.Poll();

        Assert.Empty(stopped);
        Assert.Empty(watcher.CurrentlyRunning);
    }
}
