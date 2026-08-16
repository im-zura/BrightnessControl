using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Shared "nudge every monitor" behaviour behind the global hotkeys and the tray-icon scroll, plus
/// the rule for what a manual brightness change should be remembered as.
/// </summary>
internal static class BrightnessAdjuster
{
    /// <summary>Moves every responsive monitor by <paramref name="delta"/> percent and remembers the
    /// result as the user's everyday level.</summary>
    public static async Task AdjustAllAsync(MonitorService monitorService, AppConfig config, int delta, bool gameRunning)
    {
        var generation = monitorService.BeginIntent();
        bool persisted = false;

        foreach (var monitor in monitorService.Monitors.Where(m => m.IsResponsive))
        {
            var target = Math.Clamp(monitor.CurrentPercent + delta, 0, 100);

            // Interactive: skip the read-back so repeated key presses / wheel notches stay snappy.
            if (!await monitorService.SetBrightnessPercentAsync(monitor.Id, target, generation, verify: false)
                    .ConfigureAwait(false))
                continue;

            persisted |= Remember(config, monitor.Id, target, gameRunning);
        }

        if (persisted)
            ConfigStore.Save(config);
    }

    /// <summary>Stores a manual level in whichever map is restored when no game is running — the
    /// current schedule block, or the idle profile when the schedule is off.
    ///
    /// While a game is running the change is deliberately not remembered: a mid-game tweak is about
    /// that game, and writing it to the everyday level is what made brightness "stay dark" after
    /// quitting. Returns true when the config was modified.</summary>
    public static bool Remember(AppConfig config, string monitorId, int percent, bool gameRunning, DateTime? now = null)
    {
        if (gameRunning)
            return false;

        var (map, _) = ScheduleService.ResolveNonGame(config, now ?? DateTime.Now);
        if (map.TryGetValue(monitorId, out var existing) && existing == percent)
            return false;

        map[monitorId] = percent;
        return true;
    }
}
