using System;
using System.Globalization;

namespace Ringside;

public enum Level { Ok, Warn, Alert }

/// <summary>
/// Every number and phrase on screen goes through here, so "used" (filling up) and "left"
/// (a tank draining) read naturally everywhere: the meter, tooltips, the tray and alerts.
/// Thresholds are always stored as percent used.
/// </summary>
public static class Display
{
    /// <summary>The card's main line for a reading whose window has (or may have) reset since.</summary>
    public static string LapsedText(LimitWindow w) =>
        w.Kind == ResetKind.Exact ? $"{w.Label} reset since the last reading" : $"{w.Label} may have reset since the last reading";

    /// <summary>The value a gauge draws: used fills toward 100, left drains toward 0.</summary>
    public static double Shown(double used, FillMode fill) =>
        Math.Clamp(fill == FillMode.Used ? used : 100 - used, 0, 100);

    public static string Word(FillMode fill) => fill == FillMode.Used ? "used" : "left";

    public static Level LevelOf(double used, Settings s) =>
        used >= s.AlertAt ? Level.Alert : used >= s.WarnAt ? Level.Warn : Level.Ok;

    /// <summary>"42" for 42 % used, or "58" for the same limit shown as left.</summary>
    public static string Number(double used, FillMode fill) => Fmt.Pct(Shown(used, fill));

    /// <summary>"42% used" or "58% left".</summary>
    public static string Amount(double used, FillMode fill) => $"{Number(used, fill)}% {Word(fill)}";

    /// <summary>A threshold in the words of the current mode: "75% used" or "25% left".</summary>
    public static string Threshold(double usedAt, FillMode fill) => Amount(usedAt, fill);

    /// <summary>One line for a tooltip: "5-hour: 58% left, resets in 2h 14m".</summary>
    public static string Line(LimitWindow w, DateTimeOffset now, Settings s)
    {
        string reset = Fmt.Reset(w, now, s.Resets);
        return $"{w.Label}: {Amount(w.Percent, s.Fill)}{(reset.Length > 0 ? ", " + reset : "")}";
    }

    /// <summary>The forecast line under a meter, or empty when there is nothing to warn about.</summary>
    public static string Forecast(DateTimeOffset? runsOut, DateTimeOffset now, FillMode fill)
    {
        if (runsOut is not { } t) return "";
        string when = t - now < TimeSpan.FromMinutes(1) ? "any minute" : "by " + Fmt.Clock(t);
        return fill == FillMode.Used ? $"At this pace: full {when}" : $"At this pace: empty {when}";
    }
}

public static class Fmt
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

    static string Day(DateTimeOffset t) => t.ToLocalTime().ToString("ddd", CultureInfo.CurrentCulture);

    public static string Reset(LimitWindow w, DateTimeOffset now, ResetStyle style = ResetStyle.Countdown)
    {
        if (w.Lapsed) return w.Kind == ResetKind.Exact ? "reset since last update" : "may have reset since last update";
        if (w.ResetsAt is not { } r) return "";
        var d = r - now;
        if (d <= TimeSpan.Zero) return "resetting now";
        bool far = d.TotalHours >= 24;
        return w.Kind switch
        {
            ResetKind.UpperBound => $"resets by {Clock(r)}",
            ResetKind.Estimate when style == ResetStyle.Clock => $"resets ~{Clock(r)}",
            ResetKind.Estimate => $"resets in ~{Span(d)}",
            _ when far => $"resets {Day(r)} {Clock(r)}",
            _ when style == ResetStyle.Clock => $"resets at {Clock(r)}",
            _ => $"resets in {Span(d)}",
        };
    }

    /// <summary>When an old reading was taken: "as of 3:12 PM", "as of yesterday 9:09 PM" or "as of Tue 9:09 PM".</summary>
    public static string AsOf(DateTimeOffset t, DateTimeOffset now)
    {
        var day = t.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        if (day == today) return "as of " + Clock(t);
        if (day == today.AddDays(-1)) return "as of yesterday " + Clock(t);
        if (today - day < TimeSpan.FromDays(7)) return $"as of {Day(t)} {Clock(t)}";
        return "as of " + t.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }

    public static string Age(DateTimeOffset t, DateTimeOffset now)
    {
        var d = now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 48) return $"{(int)d.TotalHours}h ago";
        return t.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
