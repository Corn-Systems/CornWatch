using System.Drawing.Drawing2D;
using CornSystems;

namespace CornWatch.UI.Tray;

// Dark Corn Systems tray menu: light text, gold highlight text and a gold check mark.
internal sealed class TrayMenuRenderer() : ToolStripProfessionalRenderer(new TrayColorTable())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // The disabled header row ("CornWatch v0.4.0") reads as a gold title, not a greyed-out item.
        e.TextColor = e.Item.Tag as string == "header" ? Theme.Accent
            : !e.Item.Enabled ? Theme.TextSec
            : e.Item.Selected ? Theme.Accent
            : Theme.TextPri;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextDim;
        base.OnRenderArrow(e);
    }

    // A drawn tick instead of the system bitmap, which is black and unreadable on the dark menu.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.AccentGlow)) g.FillRectangle(bg, r);
        using var pen = new Pen(Theme.Accent, Math.Max(1.5f, Dpi.S(2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen,
        [
            new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
            new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
        ]);
    }
}
