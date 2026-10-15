using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Ringside.Ui;

/// <summary>The tray / menu bar icon: the logo's two rings, drawn live from the current readings.</summary>
public static class TrayArt
{
    const int Size = 64;

    public static WindowIcon Render(Reading? r, Settings s)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var track = new SolidColorBrush(Color.Parse("#5ACDCDD7"));
            if (s.ShowClaude) Arc(ctx, 32, 32, 25, 9, track, r, "Claude", s);
            if (s.ShowCodex) Arc(ctx, 32, 32, s.ShowClaude ? 11.5 : 25, 9, track, r, "Codex", s);
        }
        var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    static void Arc(DrawingContext ctx, double cx, double cy, double radius, double width, IBrush track, Reading? r, string provider, Settings s)
    {
        var c = new Point(cx, cy);
        ctx.DrawEllipse(null, new Pen(track, width), c, radius, radius);
        var w = r?.Of(provider).Pick(s.Focus);
        if (w is null) return;
        double v = Display.Shown(w.Percent, s.Fill);
        if (v < 0.5) return;
        var pen = new Pen(Brand.Solid(Palette.Solid(provider, w.Percent, s)), width, lineCap: PenLineCap.Round);
        if (v > 99.5) { ctx.DrawEllipse(null, pen, c, radius, radius); return; }
        double a = v / 100 * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(new Point(cx, cy - radius), false);
            sg.ArcTo(new Point(cx + radius * Math.Sin(a), cy - radius * Math.Cos(a)), new Avalonia.Size(radius, radius), 0, a > Math.PI, SweepDirection.Clockwise);
            sg.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    /// <summary>"Ringside: Claude 58% left · Codex 82% left"</summary>
    public static string Tooltip(Reading? r, Settings s)
    {
        string Part(string p)
        {
            var w = r?.Of(p).Pick(s.Focus);
            return w is null ? $"{p} -" : $"{p} {Display.Amount(w.Percent, s.Fill)}";
        }
        var parts = new System.Collections.Generic.List<string>();
        if (s.ShowClaude) parts.Add(Part("Claude"));
        if (s.ShowCodex) parts.Add(Part("Codex"));
        return "Ringside: " + string.Join(" · ", parts);
    }
}
