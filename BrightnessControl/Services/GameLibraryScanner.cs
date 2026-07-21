using System.IO;
using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>Combines Steam and Epic library scans into one list for the game picker UI.</summary>
internal static class GameLibraryScanner
{
    public static List<DiscoveredGame> ScanAll()
    {
        var games = new List<DiscoveredGame>();

        try { games.AddRange(SteamLibraryScanner.ScanInstalledGames()); }
        catch { /* Steam not installed or unreadable; not fatal */ }

        try { games.AddRange(EpicLibraryScanner.ScanInstalledGames()); }
        catch { /* Epic not installed or unreadable; not fatal */ }

        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Resolves the actual executable path for a discovered game, doing the (potentially
    /// slower) Steam folder scan lazily — only for the one game the user picks, not the whole list.</summary>
    public static string? ResolveExecutable(DiscoveredGame game)
    {
        if (game.KnownExePath != null)
            return File.Exists(game.KnownExePath) ? game.KnownExePath : null;

        return SteamLibraryScanner.ResolveExecutable(game.InstallDir);
    }
}
