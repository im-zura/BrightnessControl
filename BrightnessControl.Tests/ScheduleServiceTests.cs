using BrightnessControl.Models;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

public class ScheduleServiceTests
{
    private static AppConfig Config(bool enabled, string dayStart = "08:00", string nightStart = "22:00")
    {
        var config = new AppConfig();
        config.IdleProfile.MonitorBrightness["m1"] = 100;
        config.Schedule.Enabled = enabled;
        config.Schedule.DayStart = dayStart;
        config.Schedule.NightStart = nightStart;
        config.Schedule.DayBrightness["m1"] = 80;
        config.Schedule.NightBrightness["m1"] = 30;
        return config;
    }

    [Fact]
    public void With_the_schedule_off_the_idle_profile_is_what_gets_restored()
    {
        var (brightness, label) = ScheduleService.ResolveNonGame(Config(enabled: false), new DateTime(2026, 8, 16, 23, 0, 0));

        Assert.Equal("Idle", label);
        Assert.Equal(100, brightness["m1"]);
    }

    [Theory]
    [InlineData(8, 0, "Day")]
    [InlineData(13, 30, "Day")]
    [InlineData(21, 59, "Day")]
    [InlineData(22, 0, "Night")]
    [InlineData(3, 0, "Night")]
    [InlineData(7, 59, "Night")]
    public void Day_and_night_blocks_cover_the_whole_clock(int hour, int minute, string expected)
    {
        var (_, label) = ScheduleService.ResolveNonGame(Config(enabled: true), new DateTime(2026, 8, 16, hour, minute, 0));

        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData(23, 0, "Day")]   // the day window runs 22:00 → 06:00
    [InlineData(2, 0, "Day")]
    [InlineData(6, 0, "Night")]
    [InlineData(12, 0, "Night")]
    public void A_day_window_that_wraps_past_midnight_still_resolves(int hour, int minute, string expected)
    {
        var config = Config(enabled: true, dayStart: "22:00", nightStart: "06:00");

        var (_, label) = ScheduleService.ResolveNonGame(config, new DateTime(2026, 8, 16, hour, minute, 0));

        Assert.Equal(expected, label);
    }

    [Fact]
    public void Unparseable_times_fall_back_to_the_08_to_22_defaults()
    {
        var config = Config(enabled: true, dayStart: "not a time", nightStart: "??");

        Assert.Equal("Day", ScheduleService.ResolveNonGame(config, new DateTime(2026, 8, 16, 10, 0, 0)).Label);
        Assert.Equal("Night", ScheduleService.ResolveNonGame(config, new DateTime(2026, 8, 16, 23, 0, 0)).Label);
    }

    [Fact]
    public void The_returned_map_is_the_live_one_so_manual_changes_are_remembered_in_the_right_block()
    {
        var config = Config(enabled: true);

        BrightnessAdjuster.Remember(config, "m1", 55, gameRunning: false, now: new DateTime(2026, 8, 16, 14, 0, 0));

        Assert.Equal(55, config.Schedule.DayBrightness["m1"]);
        Assert.Equal(30, config.Schedule.NightBrightness["m1"]);      // the other block is untouched
        Assert.Equal(100, config.IdleProfile.MonitorBrightness["m1"]);
    }

    [Fact]
    public void A_change_made_while_a_game_is_running_is_not_remembered()
    {
        // Otherwise the game's own brightness becomes the everyday level and "sticks" after quitting.
        var config = Config(enabled: false);

        var changed = BrightnessAdjuster.Remember(config, "m1", 50, gameRunning: true);

        Assert.False(changed);
        Assert.Equal(100, config.IdleProfile.MonitorBrightness["m1"]);
    }
}
