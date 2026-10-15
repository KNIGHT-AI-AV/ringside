using System;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace Ringside;

static class IconFactory
{
    static readonly SD.Color ClaudeColor = SD.Color.FromArgb(0xD9, 0x77, 0x57);
    static readonly SD.Color CodexColor = SD.Color.FromArgb(0x19, 0xC3, 0x9C);
    static readonly SD.Color Alert = SD.Color.FromArgb(0xF2, 0x58, 0x5E);

    /// <summary>Two concentric arcs: Claude outside, Codex inside.</summary>
    public static SD.Icon Tray(double? claude, double? codex)
    {
        using var bmp = new SD.Bitmap(32, 32, SD.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            Arc(g, new SD.RectangleF(3, 3, 26, 26), 4.4f, claude, ClaudeColor);
            Arc(g, new SD.RectangleF(10.5f, 10.5f, 11, 11), 4.4f, codex, CodexColor);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = SD.Icon.FromHandle(h);
            return (SD.Icon)tmp.Clone();
        }
        finally { Native.DestroyIcon(h); }
    }

    static void Arc(SD.Graphics g, SD.RectangleF r, float width, double? pct, SD.Color color)
    {
        if (pct is null) return;
        using (var track = new SD.Pen(SD.Color.FromArgb(80, 205, 205, 215), width)) g.DrawEllipse(track, r);
        float sweep = (float)(Math.Clamp(pct.Value, 0, 100) * 3.6);
        if (sweep <= 0) return;
        using var pen = new SD.Pen(pct >= Theme.AlertAt ? Alert : color, width)
        {
            StartCap = SD.Drawing2D.LineCap.Round,
            EndCap = SD.Drawing2D.LineCap.Round,
        };
        if (sweep >= 359.9f) g.DrawEllipse(pen, r);
        else g.DrawArc(pen, r, -90f, sweep);
    }
}

sealed class DarkMenuColors : WF.ProfessionalColorTable
{
    static readonly SD.Color Bg = SD.Color.FromArgb(30, 30, 35);
    static readonly SD.Color Hi = SD.Color.FromArgb(50, 50, 58);
    static readonly SD.Color Line = SD.Color.FromArgb(60, 60, 68);

    public override SD.Color ToolStripDropDownBackground => Bg;
    public override SD.Color ImageMarginGradientBegin => Bg;
    public override SD.Color ImageMarginGradientMiddle => Bg;
    public override SD.Color ImageMarginGradientEnd => Bg;
    public override SD.Color MenuBorder => Line;
    public override SD.Color MenuItemBorder => Hi;
    public override SD.Color MenuItemSelected => Hi;
    public override SD.Color MenuItemSelectedGradientBegin => Hi;
    public override SD.Color MenuItemSelectedGradientEnd => Hi;
    public override SD.Color MenuItemPressedGradientBegin => Hi;
    public override SD.Color MenuItemPressedGradientEnd => Hi;
    public override SD.Color SeparatorDark => Line;
    public override SD.Color SeparatorLight => Bg;
    public override SD.Color CheckBackground => Hi;
    public override SD.Color CheckSelectedBackground => Hi;
    public override SD.Color CheckPressedBackground => Hi;
}
