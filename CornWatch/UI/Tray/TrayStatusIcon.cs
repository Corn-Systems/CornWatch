using System.Drawing.Drawing2D;
using CornWatch.Models;

namespace CornWatch.UI.Tray;

// The tray icon reflects health at a glance: the plain corn icon when all is well,
// with an amber (warnings) or red (critical) dot in the corner otherwise.
internal static class TrayStatusIcon
{
    public enum Level { Ok, Warning, Critical }

    public static Level LevelOf(SystemSnapshot snap) =>
        snap.Alerts.Any(a => a.Severity == AlertSeverity.Critical) ? Level.Critical
        : snap.Alerts.Any(a => a.Severity == AlertSeverity.Warning) ? Level.Warning
        : Level.Ok;

    // Tooltip is capped at 127 characters by the shell (NotifyIcon.Text throws beyond that).
    public static string Tooltip(SystemSnapshot snap)
    {
        var alerts = snap.Alerts.Count;
        var text = $"{AppInfo.Name} — health {snap.HealthScore}/100" +
                   (alerts == 0 ? " · all good" : $" · {alerts} alert{(alerts == 1 ? "" : "s")}");
        return text.Length > 127 ? text[..127] : text;
    }

    // Draws the status dot over the base icon at its native size. Returns a new Icon the caller owns.
    public static Icon Render(Icon baseIcon, Level level)
    {
        var size = baseIcon.Size;
        using var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawIcon(baseIcon, new Rectangle(Point.Empty, size));
            if (level != Level.Ok)
            {
                // ~45% of the icon, bottom-right, with a dark ring so it reads on light and dark taskbars.
                var d = (int)Math.Round(size.Width * 0.45f);
                var dot = new Rectangle(size.Width - d, size.Height - d, d, d);
                using var ring = new SolidBrush(Theme.Bg);
                using var fill = new SolidBrush(level == Level.Critical ? Theme.Danger : Theme.Warning);
                g.FillEllipse(ring, dot);
                var inset = Math.Max(1, d / 6);
                dot.Inflate(-inset, -inset);
                g.FillEllipse(fill, dot);
            }
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone(); // the clone owns its own copy, so the GDI handle can go
        }
        finally { NativeMethods.DestroyIcon(handle); }
    }
}
