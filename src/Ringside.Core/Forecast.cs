using System;
using System.Collections.Generic;
using System.Linq;

namespace Ringside;

/// <summary>
/// Remembers recent readings per limit and projects when a limit runs out at the current
/// pace. Only a steady climb counts: a fit over the last hour, at least two readings at
/// least eight minutes apart. A forecast is offered only when it lands before the reset.
/// </summary>
public sealed class UsageHistory
{
    static readonly TimeSpan Keep = TimeSpan.FromHours(3);
    static readonly TimeSpan Fit = TimeSpan.FromMinutes(60);
    static readonly TimeSpan MinSpan = TimeSpan.FromMinutes(8);

    readonly Dictionary<string, List<(DateTimeOffset T, double Used)>> _points = new();

    static string Key(string provider, string label) => provider + "|" + label;

    public void Record(Snapshot s, DateTimeOffset now)
    {
        var at = s.AsOf ?? now;
        foreach (var w in s.Windows)
        {
            if (w.Lapsed) continue;
            var key = Key(s.Provider, w.Label);
            if (!_points.TryGetValue(key, out var list)) _points[key] = list = new();
            if (list.Count > 0)
            {
                var last = list[^1];
                if (at <= last.T) continue;
                // A drop means the window reset: the old climb says nothing about the new one.
                if (w.Percent < last.Used - 2) list.Clear();
            }
            list.Add((at, w.Percent));
            list.RemoveAll(p => now - p.T > Keep);
        }
    }

    public DateTimeOffset? RunsOutAt(string provider, LimitWindow w, DateTimeOffset now) =>
        _points.TryGetValue(Key(provider, w.Label), out var list) ? Project(list, w, now) : null;

    internal static DateTimeOffset? Project(IReadOnlyList<(DateTimeOffset T, double Used)> all, LimitWindow w, DateTimeOffset now)
    {
        if (w.Lapsed || w.Percent >= 100) return null;
        var pts = all.Where(p => now - p.T <= Fit).ToList();
        if (pts.Count < 2 || pts[^1].T - pts[0].T < MinSpan || pts[^1].Used - pts[0].Used < 1) return null;

        // Least-squares slope in percent per minute.
        var t0 = pts[0].T;
        double n = pts.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var (t, u) in pts)
        {
            double x = (t - t0).TotalMinutes;
            sx += x; sy += u; sxx += x * x; sxy += x * u;
        }
        double den = n * sxx - sx * sx;
        if (Math.Abs(den) < 1e-9) return null;
        double slope = (n * sxy - sx * sy) / den;
        if (slope < 0.01) return null; // under 0.6 % an hour is not a pace worth a warning

        var last = pts[^1];
        var eta = last.T + TimeSpan.FromMinutes((100 - Math.Max(last.Used, w.Percent)) / slope);
        if (eta < now) eta = now;
        if (w.ResetsAt is { } reset && eta >= reset - TimeSpan.FromMinutes(2)) return null;
        if (eta - now > TimeSpan.FromHours(24)) return null;
        return eta;
    }
}

/// <summary>Decides when a limit deserves a notification. The first reading only sets a baseline.</summary>
public sealed class Alerts
{
    readonly Dictionary<string, double> _seen = new();
    readonly Dictionary<string, DateTimeOffset?> _forecastWarned = new();

    public IEnumerable<(string Title, string Body)> Evaluate(Snapshot s, Settings settings, UsageHistory history, DateTimeOffset now)
    {
        var fill = settings.Fill;
        foreach (var w in s.Windows)
        {
            string key = s.Provider + "|" + w.Label;
            bool known = _seen.TryGetValue(key, out double prev);
            _seen[key] = w.Percent;
            if (!known) continue;

            string reset = Fmt.Reset(w, now, settings.Resets);
            string body = reset.Length == 0 ? "" : Fmt.Capitalize(reset) + ".";
            string title = fill == FillMode.Used
                ? $"{s.Provider} {w.Label} limit at {Fmt.Pct(w.Percent)}%"
                : $"{s.Provider} {w.Label}: {Display.Amount(w.Percent, fill)}";

            if (settings.NotifyAtAlert && prev < settings.AlertAt && w.Percent >= settings.AlertAt)
                yield return (title, body);
            else if (settings.NotifyAtWarn && prev < settings.WarnAt && w.Percent >= settings.WarnAt)
                yield return (title, body);

            if (settings.NotifyOnReset && prev >= 50 && w.Percent < 25 && w.Percent + 30 < prev)
            {
                _forecastWarned.Remove(key);
                yield return ($"{s.Provider} {w.Label} limit reset", $"Back to {Display.Amount(w.Percent, fill)}.");
            }

            // One heads-up per window per reset, when the current pace runs out within the hour.
            if (settings.Forecast && settings.NotifyForecast && w.Percent < settings.AlertAt &&
                history.RunsOutAt(s.Provider, w, now) is { } eta && eta - now <= TimeSpan.FromMinutes(60) &&
                !(_forecastWarned.TryGetValue(key, out var warnedFor) && warnedFor == w.ResetsAt))
            {
                _forecastWarned[key] = w.ResetsAt;
                string tail = w.ResetsAt is { } r ? $", before it resets at {Fmt.Clock(r)}" : "";
                yield return ($"{s.Provider} {w.Label} limit may run out soon",
                    $"At this pace it runs out around {Fmt.Clock(eta)}{tail}.");
            }
        }
    }
}
