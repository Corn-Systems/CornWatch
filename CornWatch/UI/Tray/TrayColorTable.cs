namespace CornWatch.UI.Tray;

// Corn Systems colours for the tray menu (ToolStripProfessionalRenderer reads these).
internal sealed class TrayColorTable : ProfessionalColorTable
{
    public TrayColorTable() => UseSystemColors = false;

    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color MenuBorder => Theme.Border2;
    public override Color MenuItemBorder => Theme.Border2;
    public override Color MenuItemSelected => Theme.Surface2;
    public override Color MenuItemSelectedGradientBegin => Theme.Surface2;
    public override Color MenuItemSelectedGradientEnd => Theme.Surface2;
    public override Color MenuItemPressedGradientBegin => Theme.Surface2;
    public override Color MenuItemPressedGradientEnd => Theme.Surface2;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Surface;
    public override Color CheckBackground => Theme.AccentGlow;
    public override Color CheckSelectedBackground => Theme.AccentGlow;
    public override Color CheckPressedBackground => Theme.AccentGlow;
    public override Color ButtonSelectedBorder => Theme.Accent;
}
