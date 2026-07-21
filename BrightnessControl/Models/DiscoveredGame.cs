namespace BrightnessControl.Models;

/// <summary>
/// A game found by scanning installed launcher libraries (Steam/Epic). KnownExePath is set
/// when the manifest already told us the exact executable (Epic); otherwise it's resolved
/// lazily from InstallDir only when the user actually picks this game (Steam heuristic scan).
/// </summary>
public sealed record DiscoveredGame(string Name, string Source, string InstallDir, string? KnownExePath);
