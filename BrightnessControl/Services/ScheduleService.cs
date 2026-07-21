using System.Globalization;
using System.Windows.Threading;
using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Drives day/night auto-brightness. Ticks once a minute and, on a block change, asks the
/// ProfileManager to re-apply the non-game state (which picks the current schedule block).
/// The actual day-vs-night decision lives in <see cref="ResolveNonGame"/> so ProfileManager can
/// reuse it whenever it needs the "no game running" brightness.
/// </summary>
internal sealed class ScheduleService : IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly Func<AppConfig> _config;
    private readonly DispatcherTimer _timer;
    private string _lastBlock = "";

    public ScheduleService(ProfileManager profileManager, Func<AppConfig> config)
    {
        _profileManager = profileManager;
        _config = config;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await OnTickAsync();
    }

    public void Start()
    {
        _lastBlock = CurrentBlock(_config(), DateTime.Now);
        _timer.Start();
    }

    private async Task OnTickAsync()
    {
        var block = CurrentBlock(_config(), DateTime.Now);
        if (block == _lastBlock)
            return;

        _lastBlock = block;
        await _profileManager.ReapplyNonGameAsync();
    }

    /// <summary>The brightness map + label to use when no tracked game is running: the current
    /// schedule block if scheduling is on, otherwise the manual idle profile.</summary>
    public static (Dictionary<string, int> Brightness, string Label) ResolveNonGame(AppConfig config, DateTime now)
    {
        var s = config.Schedule;
        if (!s.Enabled)
            return (config.IdleProfile.MonitorBrightness, "Idle");

        return CurrentBlock(config, now) == "Day"
            ? (s.DayBrightness, "Day")
            : (s.NightBrightness, "Night");
    }

    /// <summary>"Day" or "Night" for the given moment; "Idle" when scheduling is off.</summary>
    private static string CurrentBlock(AppConfig config, DateTime now)
    {
        var s = config.Schedule;
        if (!s.Enabled)
            return "Idle";

        var day = ParseMinutes(s.DayStart, 8 * 60);
        var night = ParseMinutes(s.NightStart, 22 * 60);
        var mins = now.Hour * 60 + now.Minute;

        bool isDay = day <= night
            ? mins >= day && mins < night          // day window within one calendar day
            : mins >= day || mins < night;         // day window wraps past midnight
        return isDay ? "Day" : "Night";
    }

    private static int ParseMinutes(string hhmm, int fallback) =>
        TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out var t)
            ? (int)t.TotalMinutes
            : fallback;

    public void Dispose() => _timer.Stop();
}
