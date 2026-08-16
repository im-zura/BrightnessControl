namespace BrightnessControl.Tests;

/// <summary>
/// A test that talks to the monitors actually attached to this machine. Skipped unless
/// <c>BC_HARDWARE_TESTS=1</c> is set, because it changes what the user is looking at and there is
/// nothing to talk to on a build agent.
///
/// <code>$env:BC_HARDWARE_TESTS=1; dotnet test --filter Category=Hardware -l "console;verbosity=detailed"</code>
/// </summary>
public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BC_HARDWARE_TESTS") != "1")
            Skip = "Set BC_HARDWARE_TESTS=1 to run against the attached monitors.";
    }
}
