using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ringside;

public static class Theme
{
    public const double WarnAt = 75, AlertAt = 90;

    public static readonly Color ClaudeColor = Rgb(0xD97757);
    public static readonly Color CodexColor = Rgb(0x19C39C);
    public static readonly Color AmberColor = Rgb(0xF3B048);
    public static readonly Color RedColor = Rgb(0xF2585E);

    public static readonly Brush ClaudeAccent = Gradient(0xF4A688, 0xCF6440);
    public static readonly Brush CodexAccent = Gradient(0x62E5BF, 0x0F9E7B);
    static readonly Brush Amber = Gradient(0xFFD584, 0xEFA136);
    static readonly Brush Red = Gradient(0xFF8F86, 0xEC404D);

    // Pre-attentive colour: a ring keeps its brand colour until it needs attention.
    public static Brush State(double pct, Brush accent) => pct >= AlertAt ? Red : pct >= WarnAt ? Amber : accent;

    static Brush Gradient(int from, int to)
    {
        var g = new LinearGradientBrush(Rgb(from), Rgb(to), new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

static class Fmt
{
    public static string Pct(double p) =>
        Math.Round(Math.Clamp(p, 0, 999)).ToString("0", CultureInfo.InvariantCulture);

    public static string Span(TimeSpan d)
    {
        if (d.TotalMinutes < 1) return "<1m";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h {d.Minutes}m";
        return $"{(int)d.TotalDays}d {d.Hours}h";
    }

    public static string Clock(DateTimeOffset t) => t.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string Reset(LimitWindow w, DateTimeOffset now)
    {
        if (w.Lapsed) return "reset since last update";
        if (w.ResetsAt is not { } r) return "";
        var d = r - now;
        if (d <= TimeSpan.Zero) return "resetting now";
        return w.Kind switch
        {
            ResetKind.UpperBound => $"resets by {Clock(r)}",
            ResetKind.Estimate => $"resets in ~{Span(d)}",
            _ when d.TotalHours < 24 => $"resets in {Span(d)}",
            _ => $"resets {r.ToLocalTime().ToString("ddd", CultureInfo.CurrentCulture)} {Clock(r)}",
        };
    }

    public static string Age(DateTimeOffset t, DateTimeOffset now)
    {
        var d = now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 48) return $"{(int)d.TotalHours}h ago";
        return t.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }
}
