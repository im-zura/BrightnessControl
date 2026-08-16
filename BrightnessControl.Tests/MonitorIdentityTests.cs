using BrightnessControl.Models;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

public class MonitorIdentityTests
{
    [Fact]
    public void A_device_interface_path_becomes_a_readable_stable_key()
    {
        var id = MonitorIdentity.FromDevicePath(
            @"\\?\DISPLAY#GSM5B09#5&1a2b3c4d&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

        Assert.Equal("mon-gsm5b09-5-1a2b3c4d-0-uid4353", id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_yields_no_identity(string path)
    {
        Assert.Null(MonitorIdentity.FromDevicePath(path));
    }

    [Fact]
    public void Without_a_device_path_the_description_and_display_number_are_used()
    {
        var id = MonitorIdentity.Resolve(devicePath: "", description: "LG ULTRAGEAR", displayNumber: 2, index: 0);

        Assert.Equal("mon-lg-ultragear-2", id);
    }

    [Fact]
    public void With_nothing_to_go_on_the_legacy_index_key_is_used()
    {
        var id = MonitorIdentity.Resolve(devicePath: "", description: "", displayNumber: 0, index: 1);

        Assert.Equal("monitor-1", id);
        Assert.True(MonitorIdentity.IsLegacy(id));
    }

    [Fact]
    public void Stable_ids_are_not_mistaken_for_legacy_ones()
    {
        Assert.False(MonitorIdentity.IsLegacy("mon-gsm5b09-5-1a2b3c4d-0-uid4353"));
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\DISPLAY12", 12)]
    [InlineData(@"\\.\DISPLAY", 0)]
    [InlineData("", 0)]
    public void The_windows_display_number_is_read_off_the_gdi_device_name(string device, int expected)
    {
        Assert.Equal(expected, MonitorController.ParseDisplayNumber(device));
    }

    [Fact]
    public void Brightness_percent_is_derived_from_the_monitors_own_range()
    {
        var info = new MonitorInfo { Id = "m", FriendlyName = "Display 1", Min = 20, Current = 50, Max = 80 };

        Assert.Equal(50, info.CurrentPercent);
    }

    [Fact]
    public void A_monitor_reporting_no_usable_range_reads_as_zero_instead_of_dividing_by_it()
    {
        var info = new MonitorInfo { Id = "m", FriendlyName = "Display 1", Min = 0, Current = 0, Max = 0 };

        Assert.Equal(0, info.CurrentPercent);
    }

    [Fact]
    public void A_game_profile_without_an_explicit_level_falls_back_to_its_legacy_per_monitor_values()
    {
        var profile = new GameProfile { MonitorBrightness = { ["a"] = 40, ["b"] = 60 } };

        Assert.Equal(50, profile.EffectiveGameBrightness);
    }

    [Fact]
    public void An_explicit_game_level_wins_over_the_legacy_values()
    {
        var profile = new GameProfile { GameBrightness = 35, MonitorBrightness = { ["a"] = 90 } };

        Assert.Equal(35, profile.EffectiveGameBrightness);
    }

    [Fact]
    public void A_profile_with_nothing_saved_defaults_to_half_brightness()
    {
        Assert.Equal(50, new GameProfile().EffectiveGameBrightness);
    }
}
