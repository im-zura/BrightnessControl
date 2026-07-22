using System.Drawing;
using System.Drawing.Drawing2D;

namespace BrightnessControl.Services;

/// <summary>
/// Draws the app's "brightness" logo (a white sun on a warm rounded tile) at runtime, so the tray
/// icon and window icons are a real logo instead of a generic default — no external asset needed.
/// </summary>
internal static class AppIconFactory
{
    public static Icon CreateIcon(int size = 32)
    {
        using var bmp = DrawLogo(size);
        // GetHicon copies into a new HICON; Icon.FromHandle wraps it. Cloning lets us free the HICON.
        var hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private static Bitmap DrawLogo(int s)
    {
        var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Warm rounded tile background.
        float m = s * 0.055f;
        float tw = s - 2 * m;
        float rad = s * 0.22f;
        using (var path = RoundedRect(m, m, tw, tw, rad))
        using (var grad = new LinearGradientBrush(
            new RectangleF(m, m, tw, tw),
            Color.FromArgb(255, 255, 193, 94),   // amber
            Color.FromArgb(255, 240, 135, 46),   // orange
            45f))
        {
            g.FillPath(grad, path);
        }

        // Sun rays.
        float cx = s / 2f, cy = s / 2f;
        float r1 = s * 0.255f, r2 = s * 0.40f;
        using (var pen = new Pen(Color.White, s * 0.062f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI * 2 * i / 8.0;
                g.DrawLine(pen,
                    cx + r1 * (float)Math.Cos(a), cy + r1 * (float)Math.Sin(a),
                    cx + r2 * (float)Math.Cos(a), cy + r2 * (float)Math.Sin(a));
            }
        }

        // Sun core.
        float coreR = s * 0.17f;
        using (var white = new SolidBrush(Color.White))
            g.FillEllipse(white, cx - coreR, cy - coreR, coreR * 2, coreR * 2);

        return bmp;
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        float d = r * 2;
        var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
