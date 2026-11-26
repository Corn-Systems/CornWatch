namespace CornWatch;

// Corn Systems palette for the WinForms shell. The dashboard itself is themed by the
// CSS variables at the top of Dashboard.html — keep the two in sync.
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(8, 8, 18);              // #080812
    public static readonly Color Surface = Color.FromArgb(19, 18, 42);       // #13122a
    public static readonly Color Surface2 = Color.FromArgb(26, 24, 53);      // #1a1835
    public static readonly Color Border = Color.FromArgb(42, 40, 80);        // #2a2850
    public static readonly Color Border2 = Color.FromArgb(61, 58, 112);      // #3d3a70

    public static readonly Color Accent = Color.FromArgb(245, 200, 66);      // #f5c842
    public static readonly Color AccentGlow = Color.FromArgb(40, 30, 8);
    public static readonly Color Sky = Color.FromArgb(124, 58, 237);         // #7c3aed

    public static readonly Color Success = Color.FromArgb(34, 197, 94);      // #22c55e
    public static readonly Color Warning = Color.FromArgb(251, 191, 36);     // #fbbf24
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);       // #ef4444

    public static readonly Color TextPri = Color.FromArgb(240, 238, 252);    // #f0eefc
    public static readonly Color TextDim = Color.FromArgb(160, 157, 192);    // #a09dc0
    public static readonly Color TextSec = Color.FromArgb(106, 103, 155);    // #6a679b

    // Same thresholds as pctColor() in Dashboard.html.
    public static Color ForPercent(float pct) => pct > 85 ? Danger : pct > 65 ? Warning : Success;

    // Same thresholds as tempColor() in Dashboard.html.
    public static Color ForTemp(float celsius) => celsius > 90 ? Danger : celsius > 75 ? Warning : Success;
}
