using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BrightnessControl.Views.Controls;

/// <summary>
/// Shared per-monitor brightness slider. Reused in the live tray popup (where committing a
/// value triggers a real DDC/CI write) and in profile editors (where committing a value just
/// updates an in-memory dictionary). Debounces rapid drag input either way.
/// </summary>
public partial class MonitorBrightnessSlider : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _debounceTimer;
    private bool _suppressEvents;

    public static readonly DependencyProperty MonitorNameProperty = DependencyProperty.Register(
        nameof(MonitorName), typeof(string), typeof(MonitorBrightnessSlider),
        new PropertyMetadata("Monitor", OnMonitorNameChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(MonitorBrightnessSlider),
        new PropertyMetadata(50, OnValueChanged));

    public static readonly DependencyProperty ShowGlyphProperty = DependencyProperty.Register(
        nameof(ShowGlyph), typeof(bool), typeof(MonitorBrightnessSlider),
        new PropertyMetadata(true, OnShowGlyphChanged));

    /// <summary>Whether to show the monitor glyph. Off for non-monitor rows like "Contrast".</summary>
    public bool ShowGlyph
    {
        get => (bool)GetValue(ShowGlyphProperty);
        set => SetValue(ShowGlyphProperty, value);
    }

    public string MonitorName
    {
        get => (string)GetValue(MonitorNameProperty);
        set => SetValue(MonitorNameProperty, value);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Raised ~150ms after the user stops dragging, with the settled percent value.</summary>
    public event EventHandler<int>? BrightnessCommitted;

    public MonitorBrightnessSlider()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            BrightnessCommitted?.Invoke(this, (int)Slider.Value);
        };

        NameText.Text = MonitorName;
        PercentText.Text = $"{Value}%";
        Slider.Value = Value;
    }

    private static void OnMonitorNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonitorBrightnessSlider control)
            control.NameText.Text = (string)e.NewValue;
    }

    private static void OnShowGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonitorBrightnessSlider control)
            control.Glyph.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonitorBrightnessSlider control && !control._suppressEvents)
        {
            control._suppressEvents = true;
            control.Slider.Value = (int)e.NewValue;
            control.PercentText.Text = $"{e.NewValue}%";
            control._suppressEvents = false;
        }
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents)
            return;

        var intValue = (int)Math.Round(e.NewValue);
        PercentText.Text = $"{intValue}%";

        _suppressEvents = true;
        Value = intValue;
        _suppressEvents = false;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }
}
