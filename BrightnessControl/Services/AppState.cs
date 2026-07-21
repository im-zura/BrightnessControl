using BrightnessControl.Models;

namespace BrightnessControl.Services;

/// <summary>Holds the single shared, mutable AppConfig instance for the app's lifetime.</summary>
internal sealed class AppState
{
    public AppConfig Config { get; set; }

    public AppState(AppConfig config) => Config = config;
}
