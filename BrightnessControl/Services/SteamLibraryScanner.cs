using System.IO;
using System.Text.RegularExpressions;
using BrightnessControl.Models;
using Microsoft.Win32;

namespace BrightnessControl.Services;

/// <summary>
/// Finds installed Steam games without needing them running. Reads Steam's own VDF manifest
/// files (a simple key-value text format, not JSON) rather than guessing install locations.
/// </summary>
internal static class SteamLibraryScanner
{
    private static readonly Regex VdfStringPair = new("\"([^\"]+)\"\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly string[] NonGameExeHints =
    {
        "unins", "setup", "redist", "vcredist", "vc_redist", "directx", "dxsetup", "dxwebsetup",
        "crashpad", "crashhandler", "crashreport", "easyanticheat", "battleye", "dotnetfx",
        "prereq", "vulkan", "installer", "helper.exe",
    };

    // Fixed, well-known AppIDs for Valve's shared runtime/redistributable packages, which show up
    // as ordinary appmanifest_*.acf entries indistinguishable from real games by name/installdir alone.
    private static readonly HashSet<string> NonGameAppIds = new() { "228980", "1070560", "1391110" };
    private static readonly string[] NonGameNameHints =
    {
        "steamworks common redistributables", "steam linux runtime", "proton ",
    };

    public static List<DiscoveredGame> ScanInstalledGames()
    {
        var games = new List<DiscoveredGame>();
        var steamPath = FindSteamInstallPath();
        if (steamPath == null)
            return games;

        foreach (var libraryPath in FindLibraryFolders(steamPath))
        {
            var steamAppsDir = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamAppsDir))
                continue;

            foreach (var manifestFile in SafeEnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
            {
                var game = ParseAppManifest(manifestFile, steamAppsDir);
                if (game != null)
                    games.Add(game);
            }
        }

        return games;
    }

    /// <summary>Heuristic exe search, run only once the user actually picks this game (not during listing).</summary>
    public static string? ResolveExecutable(string installDir)
    {
        if (!Directory.Exists(installDir))
            return null;

        FileInfo? best = null;
        foreach (var path in SafeEnumerateFiles(installDir, "*.exe", maxDepth: 3))
        {
            // Check the path relative to the game's own install folder (not just the filename) so
            // giveaway subfolders like "...\Redistributables\Rockstar-Games-Launcher.exe" are excluded
            // even though the filename alone wouldn't obviously look like an installer.
            var relativePath = Path.GetRelativePath(installDir, path);
            if (NonGameExeHints.Any(hint => relativePath.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = new FileInfo(path);
            if (best == null || info.Length > best.Length)
                best = info;
        }

        return best?.FullName;
    }

    private static string? FindSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string userPath && Directory.Exists(userPath))
                return userPath;
        }
        catch { /* registry unavailable; fall through to LM lookup */ }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("InstallPath") is string machinePath && Directory.Exists(machinePath))
                return machinePath;
        }
        catch { /* Steam not installed */ }

        return null;
    }

    private static List<string> FindLibraryFolders(string steamPath)
    {
        // Path.GetFullPath normalizes slash direction so the same physical folder reported with
        // different formatting (registry vs. vdf) isn't scanned twice under two different-looking keys.
        var folders = new List<string> { Path.GetFullPath(steamPath) };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return folders;

        try
        {
            var content = File.ReadAllText(vdfPath);
            foreach (Match match in VdfStringPair.Matches(content))
            {
                if (match.Groups[1].Value.Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    var path = Path.GetFullPath(match.Groups[2].Value.Replace("\\\\", "\\"));
                    if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        folders.Add(path);
                }
            }
        }
        catch { /* malformed/unreadable vdf; fall back to the default library only */ }

        return folders;
    }

    private static DiscoveredGame? ParseAppManifest(string manifestFile, string steamAppsDir)
    {
        try
        {
            var content = File.ReadAllText(manifestFile);
            string? name = null, installDir = null, appId = null;

            foreach (Match match in VdfStringPair.Matches(content))
            {
                var key = match.Groups[1].Value;
                if (appId == null && key.Equals("appid", StringComparison.OrdinalIgnoreCase))
                    appId = match.Groups[2].Value;
                else if (name == null && key.Equals("name", StringComparison.OrdinalIgnoreCase))
                    name = match.Groups[2].Value;
                else if (installDir == null && key.Equals("installdir", StringComparison.OrdinalIgnoreCase))
                    installDir = match.Groups[2].Value;

                if (name != null && installDir != null && appId != null)
                    break;
            }

            if (name == null || installDir == null)
                return null;

            if (appId != null && NonGameAppIds.Contains(appId))
                return null;

            if (NonGameNameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                return null;

            var fullInstallDir = Path.Combine(steamAppsDir, "common", installDir);
            if (!Directory.Exists(fullInstallDir))
                return null;

            return new DiscoveredGame(name, "Steam", fullInstallDir, KnownExePath: null);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, int maxDepth = int.MaxValue)
    {
        var pending = new Queue<(string Dir, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Dequeue();

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern); }
            catch { continue; }

            foreach (var file in files)
                yield return file;

            if (depth >= maxDepth)
                continue;

            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (var subDir in subDirs)
                pending.Enqueue((subDir, depth + 1));
        }
    }
}
