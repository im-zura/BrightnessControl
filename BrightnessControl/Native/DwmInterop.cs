using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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

    /// <summary>Full acrylic Start-menu treatment: dark title bar, rounded corners, the acrylic system
    /// backdrop, and a transparent client area so the backdrop shows through. The window should paint a
    /// low-alpha tint (e.g. FlyoutTintBrush) as its Background for text contrast.</summary>
    public static void ApplyAcrylic(Window window)
    {
        UseDarkTitleBar(window);
        SetRoundedCorners(window);
        SetBackdrop(window, BackdropType.Acrylic);
        WhenHandleReady(window, _ =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
                target.BackgroundColor = Colors.Transparent;
        });
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
