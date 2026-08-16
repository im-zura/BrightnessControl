using System.Runtime.CompilerServices;
using BrightnessControl.Services;

namespace BrightnessControl.Tests;

internal static class TestSetup
{
    /// <summary>Runs once, before any test in this assembly. The app log lives next to the real
    /// config, and a test run must not bury the diagnostics of an actual session under hundreds of
    /// synthetic lines.</summary>
    [ModuleInitializer]
    internal static void DisableAppLogging() => Log.Enabled = false;
}
