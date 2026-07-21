using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BrightnessControl.Models;
using BrightnessControl.Native;
using BrightnessControl.Services;
using BrightnessControl.Views.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace BrightnessControl.Views;

/// <summary>
/// The single Start-menu-style acrylic panel: monitor sliders, game-profile tiles, the idle
/// profile, and a branded bottom bar — everything the app does, anchored above the tray icon.
/// </summary>
public partial class FlyoutWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly AppState _state;
    private readonly MonitorService _monitorService;
    private readonly ProfileManager _profileManager;
    private int _openDialogCount;
    private DateTime _shownAt;

    internal FlyoutWindow(AppState state, MonitorService monitorService, ProfileManager profileManager)
    {
        InitializeComponent();
        // Opaque window (renders reliably) + DWM-rounded corners for the Start-menu look.
        DwmInterop.UseDarkTitleBar(this);
        DwmInterop.SetRoundedCorners(this);

        _state = state;
        _monitorService = monitorService;
        _profileManager = profileManager;

        SiteText.Text = AppInfo.Site;
        StartupToggle.IsChecked = _state.Config.StartWithWindows;

        _profileManager.ActiveProfileChanged += OnActiveProfileChanged;
        Closed += (_, _) => _profileManager.ActiveProfileChanged -= OnActiveProfileChanged;
        Loaded += (_, _) => { PositionNearTray(); AnimateIn(); ForceForeground(); };

        BuildMonitors();
        BuildGameTiles();
    }

    // ---- Monitors ----------------------------------------------------------

    private void BuildMonitors()
    {
        MonitorsPanel.Children.Clear();
        var monitors = _monitorService.Monitors.Where(m => m.IsResponsive).ToList();
        NoMonitorsText.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var monitor in monitors)
        {
            var slider = new MonitorBrightnessSlider
            {
                MonitorName = monitor.FriendlyName,
                Value = monitor.CurrentPercent,
            };

            var monitorId = monitor.Id;
            // The live slider doubles as the idle/default preset: what you set here is restored
            // when a tracked game closes (ProfileManager applies Config.IdleProfile.MonitorBrightness).
            slider.BrightnessCommitted += async (_, percent) =>
            {
                _state.Config.IdleProfile.MonitorBrightness[monitorId] = percent;
                ConfigStore.Save(_state.Config);
                await _monitorService.SetBrightnessPercentAsync(monitorId, percent);
            };

            MonitorsPanel.Children.Add(Card(slider, new Thickness(0, 0, 0, 8)));
        }
    }

    // ---- Game tiles --------------------------------------------------------

    private void BuildGameTiles()
    {
        GamesPanel.Children.Clear();

        foreach (var profile in _state.Config.GameProfiles)
            GamesPanel.Children.Add(BuildGameTile(profile));

        GamesPanel.Children.Add(BuildAddTile());
    }

    private Button BuildGameTile(GameProfile profile)
    {
        var grid = new Grid();

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(BuildTileIcon(profile));
        stack.Children.Add(new TextBlock
        {
            Text = profile.Name,
            FontSize = 10,
            Foreground = Brush("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 72,
            Margin = new Thickness(0, 5, 0, 0),
        });
        grid.Children.Add(stack);

        if (profile.MonitorBrightness.Count > 0)
        {
            var avg = (int)Math.Round(profile.MonitorBrightness.Values.Average());
            grid.Children.Add(new Border
            {
                Background = Brush("AccentBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Child = new TextBlock { Text = $"{avg}%", FontSize = 9, Foreground = Brushes.White },
            });
        }

        var button = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Content = grid,
            ToolTip = profile.ProcessName,
        };
        if (string.Equals(profile.Name, _profileManager.ActiveProfileName, StringComparison.Ordinal))
            button.Tag = "active";

        button.Click += (_, _) => EditProfile(profile);
        return button;
    }

    private UIElement BuildTileIcon(GameProfile profile)
    {
        var icon = IconExtractor.GetIcon(profile.ExePath);
        if (icon != null)
        {
            return new Image
            {
                Source = icon,
                Width = 34,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }

        // Fallback: first letter in an accent-tinted circle.
        var letter = string.IsNullOrEmpty(profile.Name) ? "?" : profile.Name[..1].ToUpperInvariant();
        return new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = Brush("AccentSoftBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("AccentTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private Button BuildAddTile()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 24,
            Foreground = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Add game",
            FontSize = 10,
            Foreground = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var button = new Button
        {
            Style = (Style)FindResource("TileButton"),
            Content = stack,
            ToolTip = "Add a game profile",
        };
        button.Click += (_, _) => AddProfile();
        return button;
    }

    // ---- Profile add/edit --------------------------------------------------

    private void AddProfile()
    {
        var editor = new GameProfileEditorWindow(_monitorService, null) { Owner = this };
        if (ShowModal(editor) == true && editor.ResultProfile != null)
        {
            _state.Config.GameProfiles.Add(editor.ResultProfile);
            PersistProfiles();
        }
    }

    private void EditProfile(GameProfile profile)
    {
        var editor = new GameProfileEditorWindow(_monitorService, profile) { Owner = this };
        var result = ShowModal(editor);
        if (result == true && editor.ResultProfile != null)
        {
            var index = _state.Config.GameProfiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
                _state.Config.GameProfiles[index] = editor.ResultProfile;
            PersistProfiles();
        }
        else if (editor.DeleteRequested)
        {
            _state.Config.GameProfiles.RemoveAll(p => p.Id == profile.Id);
            PersistProfiles();
        }
    }

    private void PersistProfiles()
    {
        ConfigStore.Save(_state.Config);
        _profileManager.UpdateConfig(_state.Config);
        BuildGameTiles();
    }

    /// <summary>Shows a dialog without letting the flyout auto-close while it's open.</summary>
    private bool? ShowModal(Window dialog)
    {
        _openDialogCount++;
        try { return dialog.ShowDialog(); }
        finally { _openDialogCount--; Activate(); }
    }

    // ---- Header / active state --------------------------------------------

    private void OnActiveProfileChanged(string profileName)
    {
        // The active game's tile shows the accent ring; just refresh the tiles.
        Dispatcher.Invoke(BuildGameTiles);
    }

    // ---- Bottom bar --------------------------------------------------------

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = StartupToggle.IsChecked == true;
        if (enabled == _state.Config.StartWithWindows)
            return;

        _state.Config.StartWithWindows = enabled;
        StartupManager.SetEnabled(enabled);
        ConfigStore.Save(_state.Config);
    }

    private void SiteLink_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.SiteUrl) { UseShellExecute = true }); }
        catch { /* no browser / blocked: ignore */ }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ---- Window behavior ---------------------------------------------------

    private void PositionNearTray()
    {
        // Anchor to the bottom-right of the primary work area (near the tray). SystemParameters.WorkArea
        // is in WPF device-independent units (DIPs) — the same space as Left/Top/ActualWidth — so this
        // stays correct under DPI scaling. (WinForms Screen.WorkingArea is physical px and would push the
        // window off-screen on any monitor scaled above 100%.)
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 4;
        Top = wa.Bottom - ActualHeight - 4;
    }

    private void AnimateIn()
    {
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(40, 0, duration) { EasingFunction = ease });
    }

    /// <summary>Pull the window to the foreground so it doesn't instantly self-close: opening from a
    /// tray click leaves the shell in the foreground, which would otherwise fire Deactivated at once.</summary>
    private void ForceForeground()
    {
        _shownAt = DateTime.Now;
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetForegroundWindow(hwnd);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Ignore the spurious deactivate that can fire during the show/activate handoff.
        if ((DateTime.Now - _shownAt).TotalMilliseconds < 300)
        {
            ForceForeground();
            return;
        }

        if (_openDialogCount == 0)
            Close();
    }

    // ---- Helpers -----------------------------------------------------------

    private Border Card(UIElement child, Thickness margin) => new()
    {
        Background = Brush("CardBrush"),
        BorderBrush = Brush("CardBorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 8),
        Margin = margin,
        Child = child,
    };

    private Brush Brush(string key) => (Brush)FindResource(key);
}
