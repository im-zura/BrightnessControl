using System.IO;
using System.Text;

namespace BrightnessControl.Services;

/// <summary>
/// Minimal rolling text log next to the config, so intermittent field problems (a DDC write the
/// monitor silently dropped, a display that came back with stale handles, a crashed poll tick)
/// leave a trace. Never throws — logging must not be able to break the app it is diagnosing.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024; // roll at ~1 MB, keep one previous file

    private static readonly object Gate = new();

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    private static readonly string LogPath = Path.Combine(LogDir, "log.txt");
    private static readonly string PrevPath = Path.Combine(LogDir, "log.prev.txt");

    /// <summary>Turned off by the test suite so a test run can't bury the diagnostics of a real session.</summary>
    public static bool Enabled { get; set; } = true;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (!Enabled)
            return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                Roll();
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Disk full / file locked / no permission: drop the line rather than fail the caller.
        }
    }

    private static void Roll()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes)
            return;

        if (File.Exists(PrevPath))
            File.Delete(PrevPath);
        File.Move(LogPath, PrevPath);
    }
}
