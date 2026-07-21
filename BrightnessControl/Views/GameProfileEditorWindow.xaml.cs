using System.Windows;
using BrightnessControl.Models;
using BrightnessControl.Native;
using BrightnessControl.Services;
using BrightnessControl.Views.Controls;

namespace BrightnessControl.Views;

public partial class GameProfileEditorWindow : Window
{
    private readonly MonitorService _monitorService;
    private readonly List<MonitorBrightnessSlider> _sliders = new();
    private readonly string? _existingId;
    private string? _processName;
    private string? _exePath;

    /// <summary>The edited/created profile, populated when the window closes with Save.</summary>
    public GameProfile? ResultProfile { get; private set; }

    /// <summary>True when the user clicked Delete on an existing profile (DialogResult is false).</summary>
    public bool DeleteRequested { get; private set; }

    internal GameProfileEditorWindow(MonitorService monitorService, GameProfile? existing)
    {
        InitializeComponent();
        // Opaque dark chrome (dark title bar + rounded corners). Mica would let a light desktop
        // wallpaper bleed through and wash the panel white; None keeps our solid dark surface.
        DwmInterop.ApplyModernChrome(this, BackdropType.None);
        Title = existing == null ? "Add game profile" : "Edit game profile";
        _monitorService = monitorService;

        _existingId = existing?.Id;
        NameBox.Text = existing?.Name ?? "";
        _processName = existing?.ProcessName;
        _exePath = existing?.ExePath;
        ProcessNameBox.Text = _processName ?? "(none selected)";
        EnabledBox.IsChecked = existing?.Enabled ?? true;
        DeleteButton.Visibility = existing == null ? Visibility.Collapsed : Visibility.Visible;

        BuildSliders(existing);
    }

    private void BuildSliders(GameProfile? existing)
    {
        foreach (var monitor in _monitorService.Monitors.Where(m => m.IsResponsive))
        {
            var slider = new MonitorBrightnessSlider
            {
                MonitorName = monitor.FriendlyName,
                Value = existing != null && existing.MonitorBrightness.TryGetValue(monitor.Id, out var v) ? v : 50,
                Tag = monitor.Id,
                Margin = new Thickness(0, 0, 0, 6),
            };
            _sliders.Add(slider);
            SlidersPanel.Children.Add(slider);
        }
    }

    private void PickProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new GameLibraryPickerDialog { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedProcessName != null)
        {
            _processName = picker.SelectedProcessName;
            _exePath = picker.SelectedExePath;
            ProcessNameBox.Text = _processName;

            // Auto-fill the profile name from the picked game when the user hasn't typed one.
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = picker.SelectedDisplayName
                    ?? System.IO.Path.GetFileNameWithoutExtension(_processName);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowValidationError("Please enter a profile name.");
            return;
        }

        if (string.IsNullOrEmpty(_processName))
        {
            ShowValidationError("Please pick a game.");
            return;
        }

        var brightness = new Dictionary<string, int>();
        foreach (var slider in _sliders)
            brightness[(string)slider.Tag] = slider.Value;

        ResultProfile = new GameProfile
        {
            Id = _existingId ?? Guid.NewGuid().ToString("N"),
            Name = name,
            ProcessName = _processName,
            ExePath = _exePath,
            Enabled = EnabledBox.IsChecked == true,
            MonitorBrightness = brightness,
        };

        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
