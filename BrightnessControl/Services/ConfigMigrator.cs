using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Moves saved per-monitor values from the old enumeration-index keys ("monitor-0") onto the stable
/// device-path keys, and seeds defaults for monitors the config has never seen.
///
/// Adoption runs on every enumeration rather than once: a display that was switched off during the
/// upgrade only shows up later, and its saved brightness should still find it when it does.
/// </summary>
internal static class ConfigMigrator
{
    public const int CurrentVersion = 3;

    private const int DefaultIdle = 15;
    private const int DefaultDay = 80;
    private const int DefaultNight = 30;

    /// <summary>Reconciles the config with the monitors currently attached. Returns true when
    /// something changed and the config should be saved.</summary>
    public static bool Reconcile(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        bool changed = AdoptLegacyKeys(config, monitors);
        changed |= EnsureDefaults(config, monitors);
        changed |= RecordMonitorMeta(config, monitors);

        if (config.Version < CurrentVersion)
        {
            config.Version = CurrentVersion;
            changed = true;
        }

        return changed;
    }

    /// <summary>For every attached monitor whose stable id is missing from a saved map, inherits the
    /// value stored under the index-based id the previous version used for that same display.</summary>
    private static bool AdoptLegacyKeys(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        var legacyByName = config.Monitors
            .Where(m => MonitorIdentity.IsLegacy(m.Id) && !string.IsNullOrEmpty(m.FriendlyName))
            .GroupBy(m => m.FriendlyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        if (legacyByName.Count == 0)
            return false;

        bool changed = false;

        foreach (var monitor in monitors)
        {
            if (MonitorIdentity.IsLegacy(monitor.Id))
                continue; // no stable id available on this machine — the old keys still apply

            if (!legacyByName.TryGetValue(monitor.FriendlyName, out var legacyId))
                continue;

            bool adopted = false;
            foreach (var map in AllMaps(config))
                adopted |= MoveKey(map, legacyId, monitor.Id);

            if (adopted)
            {
                config.Monitors.RemoveAll(m => m.Id == legacyId);
                Log.Info($"config: adopted '{legacyId}' → '{monitor.Id}' ({monitor.FriendlyName})");
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Gives every attached monitor an entry in the idle profile and both schedule blocks.
    /// A monitor missing from a schedule map is silently skipped when that block is applied.</summary>
    private static bool EnsureDefaults(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        bool changed = false;

        foreach (var monitor in monitors)
        {
            changed |= config.IdleProfile.MonitorBrightness.TryAdd(monitor.Id, DefaultIdle);
            changed |= config.Schedule.DayBrightness.TryAdd(monitor.Id, DefaultDay);
            changed |= config.Schedule.NightBrightness.TryAdd(monitor.Id, DefaultNight);
        }

        return changed;
    }

    /// <summary>Keeps the human-readable monitor list in config in step with reality; it is also what
    /// the next upgrade uses to recognise these monitors.</summary>
    private static bool RecordMonitorMeta(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        // Monitors that are currently detached keep their entry so their saved values stay adoptable.
        var updated = config.Monitors
            .Where(m => monitors.All(x => x.Id != m.Id))
            .Concat(monitors.Select(m => new MonitorMeta { Id = m.Id, FriendlyName = m.FriendlyName }))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        var current = config.Monitors.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();

        bool changed = updated.Count != current.Count
            || updated.Zip(current).Any(p => p.First.Id != p.Second.Id || p.First.FriendlyName != p.Second.FriendlyName);

        if (changed)
            config.Monitors = updated;

        return changed;
    }

    private static IEnumerable<Dictionary<string, int>> AllMaps(AppConfig config)
    {
        yield return config.IdleProfile.MonitorBrightness;
        yield return config.Schedule.DayBrightness;
        yield return config.Schedule.NightBrightness;

        foreach (var profile in config.GameProfiles)
            yield return profile.MonitorBrightness; // legacy per-monitor game targets
    }

    private static bool MoveKey(Dictionary<string, int> map, string fromKey, string toKey)
    {
        if (!map.TryGetValue(fromKey, out var value))
            return false;

        map.Remove(fromKey);
        map.TryAdd(toKey, value); // an explicit value already saved under the new id wins
        return true;
    }
}
