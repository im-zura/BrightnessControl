using System.Globalization;
using System.Windows;
using System.Windows.Input;
using BrightnessControl.Models;
using BrightnessControl.Native;
using BrightnessControl.Services;
using BrightnessControl.Views.Controls;

namespace BrightnessControl.Views;

/// <summary>Settings for the day/night schedule, global hotkeys, and tray-icon scroll. Writes changes
/// back to the shared AppConfig on Save; the caller re-applies them (hotkeys, schedule).</summary>
public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly MonitorService _monitorService;

    private readonly Dictionary<string, MonitorBrightnessSlider> _daySliders = new();
    private readonly Dictionary<string, MonitorBrightnessSlider> _nightSliders = new();

    private HotkeyCombo _up = new();
    private HotkeyCombo _down = new();
    private int _capturing; // 0 none, 1 up, 2 down

    internal SettingsWindow(AppState state, MonitorService monitorService)
    {
        InitializeComponent();
        DwmInterop.ApplyAcrylic(this);
        _state = state;
        _monitorService = monitorService;
        Load();
    }

    private void Load()
    {
        var c = _state.Config;

        StartupToggle.IsChecked = c.StartWithWindows;

        ScheduleToggle.IsChecked = c.Schedule.Enabled;
        DayStartBox.Text = c.Schedule.DayStart;
        NightStartBox.Text = c.Schedule.NightStart;

        foreach (var monitor in _monitorService.Monitors.Where(m => m.IsResponsive))
        {
            _daySliders[monitor.Id] = AddScheduleSlider(DaySliders, monitor, c.Schedule.DayBrightness, 80);
            _nightSliders[monitor.Id] = AddScheduleSlider(NightSliders, monitor, c.Schedule.NightBrightness, 30);
        }

        HotkeyToggle.IsChecked = c.Hotkeys.Enabled;
        _up = new HotkeyCombo { Modifiers = c.Hotkeys.Up.Modifiers, Key = c.Hotkeys.Up.Key };
        _down = new HotkeyCombo { Modifiers = c.Hotkeys.Down.Modifiers, Key = c.Hotkeys.Down.Key };
        UpHotkeyButton.Content = ComboText(_up);
        DownHotkeyButton.Content = ComboText(_down);
        HotkeyStepBox.Text = c.Hotkeys.Step.ToString(CultureInfo.InvariantCulture);

        TrayScrollToggle.IsChecked = c.TrayScrollEnabled;
        TrayStepBox.Text = c.TrayScrollStep.ToString(CultureInfo.InvariantCulture);
    }

    private static MonitorBrightnessSlider AddScheduleSlider(
        System.Windows.Controls.Panel host, MonitorInfo monitor, IReadOnlyDictionary<string, int> stored, int fallback)
    {
        var slider = new MonitorBrightnessSlider
        {
            MonitorName = monitor.FriendlyName,
            Value = stored.TryGetValue(monitor.Id, out var v) ? v : fallback,
        };
        host.Children.Add(slider);
        return slider;
    }

    // ---- Hotkey capture ----

    private void CaptureUp_Click(object sender, RoutedEventArgs e) => BeginCapture(1, UpHotkeyButton);
    private void CaptureDown_Click(object sender, RoutedEventArgs e) => BeginCapture(2, DownHotkeyButton);

    private void BeginCapture(int which, System.Windows.Controls.Button button)
    {
        _capturing = which;
        button.Content = "Press keys…";
        button.Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_capturing == 0)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape) // cancel capture
        {
            RefreshHotkeyLabels();
            _capturing = 0;
            e.Handled = true;
            return;
        }

        if (IsModifierKey(key))
            return; // wait for a non-modifier key

        var combo = new HotkeyCombo
        {
            Modifiers = ModifiersToWin32(Keyboard.Modifiers),
            Key = (uint)KeyInterop.VirtualKeyFromKey(key),
        };

        if (_capturing == 1) { _up = combo; UpHotkeyButton.Content = ComboText(combo); }
        else { _down = combo; DownHotkeyButton.Content = ComboText(combo); }

        _capturing = 0;
        e.Handled = true;
    }

    private void RefreshHotkeyLabels()
    {
        UpHotkeyButton.Content = ComboText(_up);
        DownHotkeyButton.Content = ComboText(_down);
    }

    private static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static uint ModifiersToWin32(ModifierKeys m)
    {
        uint v = 0;
        if (m.HasFlag(ModifierKeys.Alt)) v |= User32Interop.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Control)) v |= User32Interop.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Shift)) v |= User32Interop.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) v |= User32Interop.MOD_WIN;
        return v;
    }

    private static string ComboText(HotkeyCombo c)
    {
        if (c.Key == 0)
            return "—";

        var parts = new List<string>();
        if ((c.Modifiers & User32Interop.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((c.Modifiers & User32Interop.MOD_ALT) != 0) parts.Add("Alt");
        if ((c.Modifiers & User32Interop.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((c.Modifiers & User32Interop.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)c.Key).ToString());
        return string.Join(" + ", parts);
    }

    // ---- Save ----

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseTime(DayStartBox.Text, out var day) || !TryParseTime(NightStartBox.Text, out var night))
        {
            ShowError("Times must be in 24-hour HH:mm format (e.g. 08:00, 22:30).");
            return;
        }

        var c = _state.Config;

        // Start-with-Windows: apply the registry Run-key change immediately when it changes.
        var startupEnabled = StartupToggle.IsChecked == true;
        if (startupEnabled != c.StartWithWindows)
        {
            c.StartWithWindows = startupEnabled;
            StartupManager.SetEnabled(startupEnabled);
        }

        c.Schedule.Enabled = ScheduleToggle.IsChecked == true;
        c.Schedule.DayStart = day;
        c.Schedule.NightStart = night;
        foreach (var (id, slider) in _daySliders) c.Schedule.DayBrightness[id] = slider.Value;
        foreach (var (id, slider) in _nightSliders) c.Schedule.NightBrightness[id] = slider.Value;

        c.Hotkeys.Enabled = HotkeyToggle.IsChecked == true;
        c.Hotkeys.Up = _up;
        c.Hotkeys.Down = _down;
        c.Hotkeys.Step = ParseStep(HotkeyStepBox.Text, c.Hotkeys.Step);

        c.TrayScrollEnabled = TrayScrollToggle.IsChecked == true;
        c.TrayScrollStep = ParseStep(TrayStepBox.Text, c.TrayScrollStep);

        ConfigStore.Save(c);
        DialogResult = true;
    }

    private static bool TryParseTime(string text, out string normalized)
    {
        normalized = "";
        if (!TimeSpan.TryParseExact(text.Trim(), new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var t)
            || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))
            return false;

        normalized = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
        return true;
    }

    private static int ParseStep(string text, int fallback) =>
        int.TryParse(text.Trim(), out var v) ? Math.Clamp(v, 1, 50) : fallback;

    private void ShowError(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
