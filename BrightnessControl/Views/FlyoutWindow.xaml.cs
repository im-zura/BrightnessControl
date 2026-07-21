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
    private readonly Action _onSettingsChanged;
    private int _openDialogCount;
    private DateTime _shownAt;

    internal FlyoutWindow(AppState state, MonitorService monitorService, ProfileManager profileManager,
        Action onSettingsChanged)
    {
        InitializeComponent();
        // Real Windows acrylic backdrop (transient-window flavor) for the Start-menu look:
        // dark title bar + rounded corners + the DWM acrylic surface. The window/client area is made
        // transparent in OnSourceInitialized so the backdrop shows through the tinted RootBorder.
        DwmInterop.UseDarkTitleBar(this);
        DwmInterop.SetRoundedCorners(this);
        DwmInterop.SetBackdrop(this, BackdropType.Acrylic);

        _state = state;
        _monitorService = monitorService;
        _profileManager = profileManager;
        _onSettingsChanged = onSettingsChanged;

        SiteText.Text = AppInfo.Site;

        _profileManager.ActiveProfileChanged += OnActiveProfileChanged;
        _monitorService.BrightnessChanged += OnBrightnessChanged;
        Closed += (_, _) =>
        {
            _profileManager.ActiveProfileChanged -= OnActiveProfileChanged;
            _monitorService.BrightnessChanged -= OnBrightnessChanged;
        };
        Loaded += (_, _) => { PositionNearTray(); AnimateIn(); ForceForeground(); };

        BuildMonitors();
        BuildGameTiles();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Punch the client area transparent so the DWM acrylic backdrop is what shows behind the
        // tinted RootBorder (without this, WPF paints an opaque surface over the backdrop).
        if (PresentationSource.FromVisual(this) is HwndSource { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;
    }

    // ---- Monitors ----------------------------------------------------------

    private readonly Dictionary<string, MonitorBrightnessSlider> _monitorSliders = new();
    private MonitorBrightnessSlider? _masterSlider;

    private void BuildMonitors()
    {
        MonitorsPanel.Children.Clear();
        _monitorSliders.Clear();
        _masterSlider = null;
        var monitors = _monitorService.Monitors.Where(m => m.IsResponsive).ToList();
        NoMonitorsText.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Master "All monitors" control (only meaningful with more than one display).
        if (monitors.Count > 1)
            MonitorsPanel.Children.Add(BuildMasterCard(monitors));

        foreach (var monitor in monitors)
            MonitorsPanel.Children.Add(BuildMonitorCard(monitor));
    }

    private Border BuildMasterCard(List<Models.MonitorInfo> monitors)
    {
        var master = new MonitorBrightnessSlider
        {
            MonitorName = "All monitors",
            Value = (int)Math.Round(monitors.Average(m => m.CurrentPercent)),
        };
        _masterSlider = master;
        master.BrightnessCommitted += async (_, percent) =>
        {
            foreach (var monitor in _monitorService.Monitors.Where(m => m.IsResponsive))
            {
                _state.Config.IdleProfile.MonitorBrightness[monitor.Id] = percent;
                await _monitorService.SetBrightnessPercentAsync(monitor.Id, percent);
                if (_monitorSliders.TryGetValue(monitor.Id, out var s))
                    s.Value = percent;
            }
            ConfigStore.Save(_state.Config);
        };
        return Card(master, new Thickness(0, 0, 0, 8));
    }

    private Border BuildMonitorCard(Models.MonitorInfo monitor)
    {
        var monitorId = monitor.Id;
        var stack = new StackPanel();

        var slider = new MonitorBrightnessSlider
        {
            MonitorName = monitor.FriendlyName,
            Value = monitor.CurrentPercent,
        };
        _monitorSliders[monitorId] = slider;
        // The live slider doubles as the idle/default preset: what you set here is restored
        // when a tracked game closes (ProfileManager applies Config.IdleProfile.MonitorBrightness).
        slider.BrightnessCommitted += async (_, percent) =>
        {
            _state.Config.IdleProfile.MonitorBrightness[monitorId] = percent;
            ConfigStore.Save(_state.Config);
            await _monitorService.SetBrightnessPercentAsync(monitorId, percent);
        };
        stack.Children.Add(slider);

        if (monitor.SupportsContrast || monitor.SupportsTemperature)
            stack.Children.Add(BuildAdvancedPanel(monitor));

        return Card(stack, new Thickness(0, 0, 0, 8));
    }

    private UIElement BuildAdvancedPanel(Models.MonitorInfo monitor)
    {
        var advanced = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };

        var toggle = new TextBlock
        {
            Text = "Advanced ▾",
            FontSize = 11,
            Foreground = Brush("AccentTextBrush"),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(2, 2, 0, 0),
        };
        toggle.MouseLeftButtonUp += (_, _) =>
        {
            var show = advanced.Visibility != Visibility.Visible;
            advanced.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            toggle.Text = show ? "Advanced ▴" : "Advanced ▾";
        };

        if (monitor.SupportsContrast)
        {
            var contrast = new MonitorBrightnessSlider
            {
                MonitorName = "Contrast",
                ShowGlyph = false,
                Value = monitor.ContrastPercent,
            };
            var id = monitor.Id;
            contrast.BrightnessCommitted += async (_, percent) =>
                await _monitorService.SetContrastPercentAsync(id, percent);
            advanced.Children.Add(contrast);
        }

        if (monitor.SupportsTemperature)
            advanced.Children.Add(BuildTemperatureRow(monitor));

        var container = new StackPanel();
        container.Children.Add(toggle);
        container.Children.Add(advanced);
        return container;
    }

    private static readonly MC_COLOR_TEMPERATURE[] TempPresets =
    {
        MC_COLOR_TEMPERATURE.T4000K, MC_COLOR_TEMPERATURE.T5000K, MC_COLOR_TEMPERATURE.T6500K,
        MC_COLOR_TEMPERATURE.T7500K, MC_COLOR_TEMPERATURE.T8200K, MC_COLOR_TEMPERATURE.T9300K,
        MC_COLOR_TEMPERATURE.T10000K, MC_COLOR_TEMPERATURE.T11500K,
    };

    private UIElement BuildTemperatureRow(Models.MonitorInfo monitor)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Temperature", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextPrimaryBrush"),
        };
        Grid.SetColumn(label, 0);

        var value = new TextBlock
        {
            Text = TempLabel(monitor.Temperature), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right, MinWidth = 52, Margin = new Thickness(0, 0, 6, 0),
            Foreground = Brush("TextSecondaryBrush"),
        };
        Grid.SetColumn(value, 2);

        var warmer = TempButton("−");
        Grid.SetColumn(warmer, 3);
        var cooler = TempButton("+");
        Grid.SetColumn(cooler, 4);

        var id = monitor.Id;
        async void Step(int dir)
        {
            var idx = Array.IndexOf(TempPresets, monitor.Temperature);
            if (idx < 0) idx = 2; // default near 6500K
            idx = Math.Clamp(idx + dir, 0, TempPresets.Length - 1);
            var target = TempPresets[idx];
            if (await _monitorService.SetColorTemperatureAsync(id, target))
                value.Text = TempLabel(monitor.Temperature);
        }
        warmer.Click += (_, _) => Step(-1); // lower Kelvin = warmer
        cooler.Click += (_, _) => Step(+1);

        grid.Children.Add(label);
        grid.Children.Add(value);
        grid.Children.Add(warmer);
        grid.Children.Add(cooler);
        return grid;
    }

    private Button TempButton(string glyph) => new()
    {
        Content = glyph,
        Style = (Style)FindResource("IconButton"),
        Width = 26,
        Height = 24,
        FontSize = 15,
        Foreground = Brush("TextPrimaryBrush"),
    };

    private static string TempLabel(MC_COLOR_TEMPERATURE t) => t switch
    {
        MC_COLOR_TEMPERATURE.T4000K => "4000K",
        MC_COLOR_TEMPERATURE.T5000K => "5000K",
        MC_COLOR_TEMPERATURE.T6500K => "6500K",
        MC_COLOR_TEMPERATURE.T7500K => "7500K",
        MC_COLOR_TEMPERATURE.T8200K => "8200K",
        MC_COLOR_TEMPERATURE.T9300K => "9300K",
        MC_COLOR_TEMPERATURE.T10000K => "10000K",
        MC_COLOR_TEMPERATURE.T11500K => "11500K",
        _ => "—",
    };

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

        grid.Children.Add(new Border
        {
            Background = Brush("AccentBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Child = new TextBlock { Text = $"{profile.EffectiveGameBrightness}%", FontSize = 9, Foreground = Brushes.White },
        });

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

    /// <summary>Mirror an external brightness change (hotkey, tray scroll) onto the open sliders.
    /// Setting Value runs under the slider's suppress guard, so it won't re-commit a DDC write.</summary>
    private void OnBrightnessChanged(string monitorId, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            if (_monitorSliders.TryGetValue(monitorId, out var slider))
                slider.Value = percent;

            if (_masterSlider != null)
            {
                var responsive = _monitorService.Monitors.Where(m => m.IsResponsive).ToList();
                if (responsive.Count > 0)
                    _masterSlider.Value = (int)Math.Round(responsive.Average(m => m.CurrentPercent));
            }
        });
    }

    // ---- Bottom bar --------------------------------------------------------

    private void SiteLink_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.SiteUrl) { UseShellExecute = true }); }
        catch { /* no browser / blocked: ignore */ }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).RequestShutdown();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_state, _monitorService) { Owner = this };
        if (ShowModal(settings) == true)
        {
            _onSettingsChanged();
            BuildMonitors();
        }
    }

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
