using BrightnessControl.Models;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

public class ConfigMigratorTests
{
    private static MonitorInfo Monitor(string id, string name, string device = @"\\.\DISPLAY1") =>
        new() { Id = id, FriendlyName = name, DeviceName = device };

    /// <summary>A v1 config as shipped: per-monitor values keyed by enumeration index.</summary>
    private static AppConfig LegacyConfig()
    {
        var config = new AppConfig { Version = 1 };
        config.Monitors =
        [
            new MonitorMeta { Id = "monitor-0", FriendlyName = "Display 1 · main" },
            new MonitorMeta { Id = "monitor-1", FriendlyName = "Display 2" },
        ];
        config.IdleProfile.MonitorBrightness["monitor-0"] = 100;
        config.IdleProfile.MonitorBrightness["monitor-1"] = 90;
        config.Schedule.DayBrightness["monitor-0"] = 20;
        config.Schedule.NightBrightness["monitor-0"] = 10;
        config.GameProfiles.Add(new GameProfile
        {
            Name = "Forza",
            ProcessName = "forza.exe",
            MonitorBrightness = { ["monitor-0"] = 50 },
        });
        return config;
    }

    [Fact]
    public void Saved_values_follow_the_display_onto_its_stable_id()
    {
        var config = LegacyConfig();
        var monitors = new[]
        {
            Monitor("mon-gsm5b09-uid4353", "Display 1 · main"),
            Monitor("mon-del41a2-uid4355", "Display 2"),
        };

        Assert.True(ConfigMigrator.Reconcile(config, monitors));

        Assert.Equal(100, config.IdleProfile.MonitorBrightness["mon-gsm5b09-uid4353"]);
        Assert.Equal(90, config.IdleProfile.MonitorBrightness["mon-del41a2-uid4355"]);
        Assert.Equal(20, config.Schedule.DayBrightness["mon-gsm5b09-uid4353"]);
        Assert.Equal(10, config.Schedule.NightBrightness["mon-gsm5b09-uid4353"]);
        Assert.Equal(50, config.GameProfiles[0].MonitorBrightness["mon-gsm5b09-uid4353"]);

        Assert.DoesNotContain("monitor-0", config.IdleProfile.MonitorBrightness.Keys);
        Assert.DoesNotContain("monitor-1", config.IdleProfile.MonitorBrightness.Keys);
    }

    [Fact]
    public void A_display_that_is_switched_off_keeps_its_saved_values_until_it_comes_back()
    {
        // The upgrade happens while only one monitor is attached — the other one's settings must
        // survive so they still find it when it is switched on again.
        var config = LegacyConfig();

        ConfigMigrator.Reconcile(config, new[] { Monitor("mon-gsm5b09-uid4353", "Display 1 · main") });
        Assert.Equal(90, config.IdleProfile.MonitorBrightness["monitor-1"]);

        // Later, with both attached, the second one is adopted too.
        ConfigMigrator.Reconcile(config, new[]
        {
            Monitor("mon-gsm5b09-uid4353", "Display 1 · main"),
            Monitor("mon-del41a2-uid4355", "Display 2"),
        });

        Assert.Equal(90, config.IdleProfile.MonitorBrightness["mon-del41a2-uid4355"]);
        Assert.DoesNotContain("monitor-1", config.IdleProfile.MonitorBrightness.Keys);
    }

    [Fact]
    public void Reconciling_twice_changes_nothing_the_second_time()
    {
        var config = LegacyConfig();
        var monitors = new[] { Monitor("mon-gsm5b09-uid4353", "Display 1 · main") };

        Assert.True(ConfigMigrator.Reconcile(config, monitors));
        Assert.False(ConfigMigrator.Reconcile(config, monitors));
        Assert.Equal(ConfigMigrator.CurrentVersion, config.Version);
    }

    [Fact]
    public void A_newly_attached_monitor_gets_an_entry_in_every_map()
    {
        // Without this, a monitor missing from a schedule map is silently skipped when that block
        // is applied — the display simply never follows the schedule.
        var config = new AppConfig();
        var monitors = new[] { Monitor("mon-new-uid1", "Display 3") };

        Assert.True(ConfigMigrator.Reconcile(config, monitors));

        Assert.True(config.IdleProfile.MonitorBrightness.ContainsKey("mon-new-uid1"));
        Assert.True(config.Schedule.DayBrightness.ContainsKey("mon-new-uid1"));
        Assert.True(config.Schedule.NightBrightness.ContainsKey("mon-new-uid1"));
    }

    [Fact]
    public void An_explicit_value_already_saved_under_the_stable_id_wins_over_the_legacy_one()
    {
        var config = LegacyConfig();
        config.IdleProfile.MonitorBrightness["mon-gsm5b09-uid4353"] = 42;

        ConfigMigrator.Reconcile(config, new[] { Monitor("mon-gsm5b09-uid4353", "Display 1 · main") });

        Assert.Equal(42, config.IdleProfile.MonitorBrightness["mon-gsm5b09-uid4353"]);
    }

    [Fact]
    public void Machines_with_no_stable_identity_keep_working_on_the_index_keys()
    {
        var config = LegacyConfig();

        ConfigMigrator.Reconcile(config, new[] { Monitor("monitor-0", "Display 1 · main") });

        Assert.Equal(100, config.IdleProfile.MonitorBrightness["monitor-0"]);
    }
}
