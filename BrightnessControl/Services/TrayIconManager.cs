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

    private FlyoutWindow? _flyout;

    public TrayIconManager(MonitorService monitorService, ProfileManager profileManager, AppState state)
    {
        _monitorService = monitorService;
        _profileManager = profileManager;
        _state = state;

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open");
        openItem.Click += (_, _) => ShowFlyout();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();
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
    }

    private void UpdateTooltip(string activeProfileName)
    {
        var text = $"{AppInfo.Name} — {activeProfileName}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

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

        _flyout = new FlyoutWindow(_state, _monitorService, _profileManager);
        _flyout.Show();
        _flyout.Activate();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
