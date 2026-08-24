using System.Runtime.InteropServices;

namespace NoelMic.Ui;

/// <summary>
/// Builds the tray's mic-on and mic-off icons programmatically — no external
/// asset files needed for two simple 32x32 glyphs.
/// </summary>
public sealed class IconFactory : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly Bitmap _micOnBitmap;
    private readonly Bitmap _micOffBitmap;

    // Built exactly once and reused for the app's whole lifetime — icons only
    // swap on mute toggle, and Icon.FromHandle allocates a native GDI handle
    // that must be explicitly destroyed, so creating one per toggle would leak.
    public Icon MicOnIcon { get; }

    public Icon MicOffIcon { get; }

    public IconFactory()
    {
        _micOnBitmap = DrawMicIcon(Color.ForestGreen, slashed: false);
        _micOffBitmap = DrawMicIcon(Color.Firebrick, slashed: true);

        MicOnIcon = Icon.FromHandle(_micOnBitmap.GetHicon());
        MicOffIcon = Icon.FromHandle(_micOffBitmap.GetHicon());
    }

    private static Bitmap DrawMicIcon(Color backgroundColor, bool slashed)
    {
        var bitmap = new Bitmap(32, 32);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var backgroundBrush = new SolidBrush(backgroundColor);
            g.FillEllipse(backgroundBrush, 1, 1, 30, 30);

            using var micPen = new Pen(Color.White, 2.5f);
            using var micBrush = new SolidBrush(Color.White);

            // Mic capsule.
            g.FillRectangle(micBrush, 13, 7, 6, 12);
            g.DrawArc(micPen, 13, 7, 6, 12, 180, 180);

            // Stand.
            g.DrawArc(micPen, 9, 12, 14, 12, 0, 180);
            g.DrawLine(micPen, 16, 22, 16, 26);
            g.DrawLine(micPen, 12, 26, 20, 26);

            if (slashed)
            {
                using var slashPen = new Pen(Color.White, 3f);
                g.DrawLine(slashPen, 6, 26, 26, 6);
            }
        }

        return bitmap;
    }

    public void Dispose()
    {
        DestroyIcon(MicOnIcon.Handle);
        DestroyIcon(MicOffIcon.Handle);
        _micOnBitmap.Dispose();
        _micOffBitmap.Dispose();
    }
}
