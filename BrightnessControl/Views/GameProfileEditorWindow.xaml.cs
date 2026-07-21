using System.Windows;
using BrightnessControl.Models;
using BrightnessControl.Native;
using BrightnessControl.Services;
using BrightnessControl.Views.Controls;

namespace BrightnessControl.Views;

public partial class GameProfileEditorWindow : Window
{
    private readonly MonitorService _monitorService;
    private MonitorBrightnessSlider _brightnessSlider = null!;
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
        // Acrylic Start-menu chrome, matching the flyout for a consistent look.
        DwmInterop.ApplyAcrylic(this);
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
        _brightnessSlider = new MonitorBrightnessSlider
        {
            MonitorName = "Brightness",
            ShowGlyph = false,
            Value = existing?.EffectiveGameBrightness ?? 50,
        };
        SlidersPanel.Children.Add(_brightnessSlider);
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

        ResultProfile = new GameProfile
        {
            Id = _existingId ?? Guid.NewGuid().ToString("N"),
            Name = name,
            ProcessName = _processName,
            ExePath = _exePath,
            Enabled = EnabledBox.IsChecked == true,
            GameBrightness = _brightnessSlider.Value,
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
