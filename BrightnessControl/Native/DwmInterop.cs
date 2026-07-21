using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BrightnessControl.Native;

internal enum BackdropType
{
    None = 1,
    Mica = 2,          // DWMSBT_MAINWINDOW
    Acrylic = 3,       // DWMSBT_TRANSIENTWINDOW
    MicaAlt = 4,       // DWMSBT_TABBEDWINDOW
}

/// <summary>
/// Windows 11 window-composition helpers: dark title bar, rounded corners, and the Mica/Acrylic
/// system backdrops that give the app its Start-menu look. All calls are best-effort no-ops on
/// OS builds that don't support a given attribute.
/// </summary>
internal static class DwmInterop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void UseDarkTitleBar(Window window) =>
        WhenHandleReady(window, hwnd => TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1));

    public static void SetRoundedCorners(Window window) =>
        WhenHandleReady(window, hwnd => TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND));

    public static void SetBackdrop(Window window, BackdropType type) =>
        WhenHandleReady(window, hwnd => TrySet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, (int)type));

    /// <summary>Dark title bar + rounded corners + the given backdrop, in one call.</summary>
    public static void ApplyModernChrome(Window window, BackdropType backdrop)
    {
        UseDarkTitleBar(window);
        SetRoundedCorners(window);
        SetBackdrop(window, backdrop);
    }

    private static void TrySet(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            int v = value;
            DwmSetWindowAttribute(hwnd, attribute, ref v, sizeof(int));
        }
        catch (DllNotFoundException) { /* pre-Win10 dwmapi: no-op */ }
        catch (EntryPointNotFoundException) { /* older dwmapi without this attribute: no-op */ }
    }

    private static void WhenHandleReady(Window window, Action<IntPtr> action)
    {
        void Run()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                action(hwnd);
        }

        if (window.IsLoaded && PresentationSource.FromVisual(window) != null)
            Run();
        else
            window.SourceInitialized += (_, _) => Run();
    }
}
