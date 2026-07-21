using BrightnessControl.Views;
using Application = System.Windows.Application;

namespace BrightnessControl.Services;

/// <summary>Owns the tray icon and toggles the unified flyout panel.</summary>
internal sealed class TrayIconManager : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly MonitorService _monitorService;
    private readonly ProfileManager _profileManager;
    private readonly AppState _state;
    private readonly Action _onSettingsChanged;
    private readonly TrayScrollService _trayScroll;

    private FlyoutWindow? _flyout;

    public TrayIconManager(MonitorService monitorService, ProfileManager profileManager, AppState state,
        Action onSettingsChanged)
    {
        _monitorService = monitorService;
        _profileManager = profileManager;
        _state = state;
        _onSettingsChanged = onSettingsChanged;

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open");
        openItem.Click += (_, _) => ShowFlyout();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ((App)Application.Current).RequestShutdown();
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIconFactory.CreateIcon(32),
            Visible = true,
            Text = AppInfo.Name,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ToggleFlyout();
        };

        _profileManager.ActiveProfileChanged += UpdateTooltip;
        UpdateTooltip(_profileManager.ActiveProfileName);

        // Mouse-wheel-over-tray-icon brightness. Needs the NotifyIcon's window to exist, which it does
        // now that Visible=true was set above.
        _trayScroll = new TrayScrollService(_notifyIcon, monitorService, () => state.Config);
        _trayScroll.Initialize();
    }

    private void UpdateTooltip(string activeProfileName)
    {
        var text = $"{AppInfo.Name} — {activeProfileName}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    /// <summary>Surface the flyout — used when a second launch is redirected to this instance.</summary>
    public void OpenFlyout() => ShowFlyout();

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.Close();
            return;
        }

        ShowFlyout();
    }

    private void ShowFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.Activate();
            return;
        }

        _flyout = new FlyoutWindow(_state, _monitorService, _profileManager, _onSettingsChanged);
        _flyout.Show();
        _flyout.Activate();
    }

    /// <summary>Release the low-level mouse hook early on Windows shutdown/logoff, before the OS
    /// tears the process down and possibly skips OnExit. Idempotent — OnExit's Dispose runs later.</summary>
    public void ReleaseHooks() => _trayScroll.Dispose();

    public void Dispose()
    {
        _trayScroll.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
