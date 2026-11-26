using System;
using System.Drawing;
using System.Windows.Forms;

namespace CornSystems { public static class Dpi
{
    private const float BASE_DPI = 96f;
    public static float Current { get; private set; } = BASE_DPI;
    public static float Scale   => Current / BASE_DPI;

    public static int   S(int pixels)   => (int)Math.Round(pixels * Scale);
    public static float S(float pixels) => pixels * Scale;
    public static Size  S(Size sz)      => new Size(S(sz.Width), S(sz.Height));
    public static Point S(Point pt)     => new Point(S(pt.X), S(pt.Y));

    public static void Update(Control ctrl)
    {
        try   { Current = ctrl.DeviceDpi; }
        catch { try { using var g = ctrl.CreateGraphics(); Current = g.DpiX; } catch { } }
    }
    public static void Update(int newDpi) { if (newDpi > 0) Current = newDpi; }
} }
