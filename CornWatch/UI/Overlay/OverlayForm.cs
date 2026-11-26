using System.Drawing.Text;
using CornWatch.Models;

namespace CornWatch.UI.Overlay;

// CornWatch Overlay: a small always-on-top, click-through readout of CPU / GPU / RAM / battery
// for use while gaming. It never takes focus and mouse clicks pass through to the game.
// Shows over windowed and borderless-fullscreen games; exclusive fullscreen draws over everything.
internal sealed class OverlayForm : Form
{
    public const string HotkeyText = "Ctrl+Alt+O";

    // 96-DPI pixels, scaled by this window's own monitor DPI (it can sit on a different monitor than MainForm).
    private const int ScreenMargin = 14, Pad = 8, AccentWidth = 3, ColumnGap = 10, RowGap = 2;
    private const float FontPx = 13f;

    private static readonly string[] _labels = ["CPU", "GPU", "RAM", "BAT"];

    private SystemSnapshot? _snap;
    private Font _font = null!, _bold = null!;
    private float _scale = 1f;
    private string _corner = "TopRight";
    private byte _alpha = 217;
    private int _labelWidth, _valueWidth, _tempWidth, _rowHeight;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        Text = "CornWatch Overlay";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        _scale = DeviceDpi / 96f;
        BuildFonts();
        DpiChanged += (_, e) =>
        {
            _scale = e.DeviceDpiNew / 96f;
            BuildFonts();
            Relayout();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Layered + transparent = click-through; tool window = no taskbar / Alt-Tab entry.
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                          | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _alpha = (byte)Math.Round(settings.OverlayOpacityPercent / 100.0 * 255);
        _corner = settings.OverlayCorner;
        ApplyAlpha();
        Relayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // WS_EX_LAYERED is forced in CreateParams (needed for click-through), and a layered window
        // stays invisible until its alpha is set — so set it here rather than through Form.Opacity,
        // which skips the call at 100%.
        ApplyAlpha();
    }

    private void ApplyAlpha()
    {
        if (IsHandleCreated) NativeMethods.SetLayeredWindowAttributes(Handle, 0, _alpha, NativeMethods.LWA_ALPHA);
    }

    public void UpdateSnapshot(SystemSnapshot snap)
    {
        var rowsChanged = RowCount(_snap) != RowCount(snap);
        _snap = snap;
        if (rowsChanged) Relayout();
        Invalidate();
        // Some games re-take the top of the z-order when they gain focus; push back above them.
        if (IsHandleCreated)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using (var accent = new SolidBrush(Theme.Accent)) g.FillRectangle(accent, 0, 0, S(AccentWidth), Height);

        var x0 = S(AccentWidth) + S(Pad);
        var y = S(Pad);
        foreach (var (label, value, valueColor, extra, extraColor) in Rows(_snap))
        {
            TextRenderer.DrawText(g, label, _font, new Point(x0, y), Theme.TextSec, TextFormatFlags.NoPadding);
            var xv = x0 + _labelWidth + S(ColumnGap);
            TextRenderer.DrawText(g, value, _bold, new Rectangle(xv, y, _valueWidth, _rowHeight), valueColor,
                TextFormatFlags.NoPadding | TextFormatFlags.Right);
            if (extra is not null)
                TextRenderer.DrawText(g, extra, _bold, new Rectangle(xv + _valueWidth + S(ColumnGap), y, _tempWidth, _rowHeight), extraColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.Right);
            y += _rowHeight + S(RowGap);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _font.Dispose();
            _bold.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated) BeginInvoke(Relayout);
    }

    private int S(int px) => (int)Math.Round(px * _scale);

    private void BuildFonts()
    {
        _font?.Dispose();
        _bold?.Dispose();
        _font = new Font("Courier New", FontPx * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _bold = new Font("Courier New", FontPx * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var flags = TextFormatFlags.NoPadding;
        _labelWidth = _labels.Max(l => TextRenderer.MeasureText(l, _font, Size.Empty, flags).Width);
        // Widest possible values, so the box doesn't twitch as numbers change.
        _valueWidth = TextRenderer.MeasureText("100%+", _bold, Size.Empty, flags).Width;
        _tempWidth = TextRenderer.MeasureText("100°C", _bold, Size.Empty, flags).Width;
        _rowHeight = TextRenderer.MeasureText("Ag", _bold, Size.Empty, flags).Height;
    }

    private static int RowCount(SystemSnapshot? snap) => Rows(snap).Count();

    private void Relayout()
    {
        var rows = Math.Max(1, RowCount(_snap));
        var size = new Size(
            S(AccentWidth) + S(Pad) * 2 + _labelWidth + S(ColumnGap) * 2 + _valueWidth + _tempWidth,
            S(Pad) * 2 + rows * _rowHeight + (rows - 1) * S(RowGap));
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var m = S(ScreenMargin);
        var x = _corner.EndsWith("Left", StringComparison.Ordinal) ? area.Left + m : area.Right - size.Width - m;
        var y = _corner.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - size.Height - m : area.Top + m;
        Bounds = new Rectangle(new Point(x, y), size);
        Invalidate();
    }

    // (label, value, value colour, optional second column, its colour)
    private static IEnumerable<(string, string, Color, string?, Color)> Rows(SystemSnapshot? s)
    {
        if (s is null)
        {
            yield return ("CPU", "…", Theme.TextDim, null, Theme.TextDim);
            yield break;
        }

        var cpuTemp = s.CpuTempAvailable && s.CpuTemperature > 0 ? s.CpuTemperature : (float?)null;
        yield return ("CPU", $"{s.CpuTotalUsage:0}%", Theme.ForPercent(s.CpuTotalUsage),
            cpuTemp is { } ct ? $"{ct:0}°C" : "—", cpuTemp is { } ct2 ? Theme.ForTemp(ct2) : Theme.TextSec);

        if (s.Gpus.FirstOrDefault() is { } gpu)
            yield return ("GPU", $"{gpu.UsagePercent:0}%", Theme.ForPercent(gpu.UsagePercent),
                gpu.TemperatureCelsius is { } gt ? $"{gt:0}°C" : "—", gpu.TemperatureCelsius is { } gt2 ? Theme.ForTemp(gt2) : Theme.TextSec);

        yield return ("RAM", $"{s.RamUsagePercent:0}%", Theme.ForPercent(s.RamUsagePercent), null, Theme.TextSec);

        if (s.Battery is { } b)
            // "+" = plugged in (Courier New has no lightning-bolt glyph).
            yield return ("BAT", $"{b.ChargePercent:0}%{(b.OnAcPower ? "+" : "")}",
                b.ChargePercent <= 10 && !b.OnAcPower ? Theme.Danger : b.ChargePercent <= 25 && !b.OnAcPower ? Theme.Warning : Theme.Success,
                null, Theme.TextSec);
    }
}
