using System.IO;
using System.Text.Json;
using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>
/// Finds installed Epic Games Store titles by reading the launcher's own .item manifests,
/// which (unlike Steam) already state the exact launch executable — no heuristic guessing needed.
/// </summary>
internal static class EpicLibraryScanner
{
    public static List<DiscoveredGame> ScanInstalledGames()
    {
        var games = new List<DiscoveredGame>();

        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir))
            return games;

        IEnumerable<string> itemFiles;
        try { itemFiles = Directory.EnumerateFiles(manifestsDir, "*.item"); }
        catch { return games; }

        foreach (var file in itemFiles)
        {
            var game = TryParseManifest(file);
            if (game != null)
                games.Add(game);
        }

        return games;
    }

    private static DiscoveredGame? TryParseManifest(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;

            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) &&
                incomplete.ValueKind == JsonValueKind.True)
                return null;

            if (!root.TryGetProperty("DisplayName", out var nameEl) ||
                !root.TryGetProperty("InstallLocation", out var installEl) ||
                !root.TryGetProperty("LaunchExecutable", out var exeEl))
                return null;

            var name = nameEl.GetString();
            var installLocation = installEl.GetString();
            var launchExecutable = exeEl.GetString();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installLocation) ||
                string.IsNullOrWhiteSpace(launchExecutable))
                return null;

            var exePath = Path.Combine(installLocation, launchExecutable);
            if (!File.Exists(exePath))
                return null;

            return new DiscoveredGame(name, "Epic", installLocation, exePath);
        }
        catch
        {
            return null;
        }
    }
}
