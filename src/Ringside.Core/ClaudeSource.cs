using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ringside;

/// <summary>
/// Claude usage from two local files, never from your login:
/// the Claude desktop app's own usage log (sampled every 15 minutes while the app is open),
/// and, when connected, the limits Claude Code hands to its status line after each reply.
/// The newer of the two wins.
/// </summary>
public sealed class ClaudeSource
{
    static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    internal const string DesktopText =
        "From the Claude app's own usage log, which it updates when it refreshes its usage card, sometimes hours apart. " +
        "The reset time is estimated from those samples.";
    internal const string StaleText = "Waiting for the Claude app to refresh its usage";
    internal const string FeedText =
        "From Claude Code's status line, which reports exact limits and reset times after each reply.";

    readonly string? _logOverride;
    readonly string _feedPath;

    sealed record Sample(DateTimeOffset T, string Org, double? FiveHour, double? Weekly);
    sealed record Reading(IReadOnlyList<LimitWindow> Windows, DateTimeOffset AsOf);

    List<Sample> _samples = new();
    DateTime _logStamp;
    string? _logPath;
    DateTimeOffset _logLookup = DateTimeOffset.MinValue;

    JsonDocument? _feed;
    DateTime _feedStamp;

    public ClaudeSource(string? desktopLog = null, string? feedPath = null)
    {
        _logOverride = desktopLog;
        _feedPath = feedPath ?? Paths.StatusLineFeed;
    }

    public bool DesktopLogFound => ResolveLog(DateTimeOffset.Now) is not null;

    public Snapshot Read(DateTimeOffset now)
    {
        RefreshLog(now);
        RefreshFeed();
        var desktop = FromLog(now);
        var feed = FromFeed(now);

        if (desktop is null && feed is null)
            return Snapshot.Empty("Claude", "Waiting for the Claude app or Claude Code", DesktopText);

        if (feed is not null && (desktop is null || feed.AsOf >= desktop.AsOf))
            return new Snapshot("Claude", null, feed.Windows, feed.AsOf, TimeSpan.FromMinutes(60), null, FeedText);

        var windows = desktop!.Windows.ToList();
        if (feed is not null)
        {
            // An exact reset time from Claude Code beats our estimate when both describe the same window.
            var fiveHour = feed.Windows.FirstOrDefault(w => w.Label == "5-hour");
            int i = windows.FindIndex(w => w.Label == "5-hour");
            if (i >= 0 && fiveHour?.ResetsAt is { } exact && !fiveHour.Lapsed &&
                windows[i].ResetsAt is { } guess && windows[i].Kind != ResetKind.Exact &&
                Math.Abs((exact - guess).TotalMinutes) <= 35)
                windows[i] = windows[i] with { ResetsAt = exact, Kind = ResetKind.Exact };

            // The desktop log does not record every plan's weekly limit; Claude Code does.
            if (!windows.Any(w => w.Label == "Weekly") &&
                feed.Windows.FirstOrDefault(w => w.Label == "Weekly") is { Lapsed: false } weekly)
                windows.Add(weekly);
        }
        // The Claude app only writes its log while its own usage card refreshes, which can be a day
        // apart. Say so, rather than showing an old number as if it were current.
        string? note = now - desktop.AsOf > TimeSpan.FromMinutes(35) ? StaleText : null;
        return new Snapshot("Claude", null, windows, desktop.AsOf, TimeSpan.FromMinutes(35), note, DesktopText);
    }

    // ---- the Claude desktop app's usage log ------------------------------------------

    static IEnumerable<string> LogCandidates()
    {
        const string LogName = "plan-usage-history.json";
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(Paths.Home, "Library", "Application Support", "Claude", LogName);
            yield break;
        }
        if (!OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Paths.XdgConfig, "Claude", LogName);
            yield break;
        }

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(roaming, "Claude", LogName);

        // Store (MSIX) installs keep their roaming data inside the package folder.
        foreach (var d in PackageDirs())
            yield return Path.Combine(d, "LocalCache", "Roaming", "Claude", LogName);
    }

    static List<string> PackageDirs()
    {
        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        try
        {
            return Directory.Exists(packages)
                ? Directory.EnumerateDirectories(packages, "*Claude*").ToList()
                : new List<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    string? ResolveLog(DateTimeOffset now)
    {
        if (_logOverride is not null) return File.Exists(_logOverride) ? _logOverride : null;
        if (now - _logLookup < TimeSpan.FromMinutes(5)) return _logPath;
        _logLookup = now;
        _logPath = LogCandidates().Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return _logPath;
    }

    void RefreshLog(DateTimeOffset now)
    {
        try
        {
            var path = ResolveLog(now);
            if (path is null) return;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == _logStamp) return;
            using var fs = SharedFile.Open(path);
            _samples = ParseLog(fs);
            _logStamp = stamp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Caught mid-write; the next poll reads it.
        }
    }

    static List<Sample> ParseLog(Stream s)
    {
        using var doc = JsonDocument.Parse(s);
        var list = new List<Sample>();
        if (!doc.RootElement.TryGetProperty("samples", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (!e.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) continue;
            long ms = t.TryGetInt64(out var l) ? l : (long)t.GetDouble();
            string org = e.TryGetProperty("org", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : "";
            double? fh = null, sd = null;
            if (e.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                if (u.TryGetProperty("fh", out var f) && f.ValueKind == JsonValueKind.Number) fh = f.GetDouble();
                if (u.TryGetProperty("sd", out var d) && d.ValueKind == JsonValueKind.Number) sd = d.GetDouble();
            }
            list.Add(new Sample(DateTimeOffset.FromUnixTimeMilliseconds(ms), org, fh, sd));
        }
        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list;
    }

    Reading? FromLog(DateTimeOffset now)
    {
        if (_samples.Count == 0) return null;
        // The newest sample belongs to the account the app is signed in to now.
        string org = _samples[^1].Org;
        var mine = _samples.Where(x => x.Org == org).ToList();
        var latest = mine[^1];
        var windows = new List<LimitWindow>();
        var fh = mine.Where(x => x.FiveHour.HasValue).ToList();
        if (fh.Count > 0) windows.Add(FiveHour(fh, now));
        if (latest.Weekly is double w) windows.Add(new LimitWindow("Weekly", w, null, ResetKind.None, false));
        return windows.Count == 0 ? null : new Reading(windows, latest.T);
    }

    // Usage only rises inside one 5-hour window, so the current window began just after
    // the last sample that was zero or higher than the one that followed it.
    static LimitWindow FiveHour(List<Sample> s, DateTimeOffset now)
    {
        int n = s.Count - 1;
        double v = s[n].FiveHour!.Value;
        if (v <= 0) return new LimitWindow("5-hour", 0, null, ResetKind.None, false);

        int k = n;
        while (k > 0)
        {
            double prev = s[k - 1].FiveHour!.Value;
            if (prev <= 0 || prev > s[k].FiveHour!.Value || s[n].T - s[k - 1].T >= FiveHours) break;
            k--;
        }

        var upper = s[k].T + FiveHours;
        if (now >= upper) return new LimitWindow("5-hour", 0, upper, ResetKind.Exact, true);
        if (k > 0)
        {
            var lower = s[k - 1].T + FiveHours;
            if (upper - lower <= TimeSpan.FromMinutes(31))
                return new LimitWindow("5-hour", v, lower + (upper - lower) / 2, ResetKind.Estimate, false);
        }
        return new LimitWindow("5-hour", v, upper, ResetKind.UpperBound, false);
    }

    // ---- Claude Code's status line (written by the Ringside status line bridge) ----------------

    void RefreshFeed()
    {
        try
        {
            if (!File.Exists(_feedPath)) { _feed?.Dispose(); _feed = null; return; }
            var stamp = File.GetLastWriteTimeUtc(_feedPath);
            if (stamp == _feedStamp && _feed is not null) return;
            using var fs = SharedFile.Open(_feedPath);
            var doc = JsonDocument.Parse(fs);
            _feed?.Dispose();
            _feed = doc;
            _feedStamp = stamp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    Reading? FromFeed(DateTimeOffset now)
    {
        if (_feed is null) return null;
        var root = _feed.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("updated", out var up) || up.ValueKind != JsonValueKind.Number) return null;
        var asOf = DateTimeOffset.FromUnixTimeSeconds((long)up.GetDouble());

        var windows = new List<LimitWindow>();
        foreach (var (key, label) in new[] { ("five_hour", "5-hour"), ("seven_day", "Weekly") })
        {
            if (!root.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("used_percentage", out var pct) || pct.ValueKind != JsonValueKind.Number) continue;
            DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds((long)r.GetDouble())
                : null;
            bool lapsed = resets is { } x && now >= x;
            windows.Add(new LimitWindow(label, lapsed ? 0 : pct.GetDouble(), resets,
                resets is null ? ResetKind.None : ResetKind.Exact, lapsed));
        }
        return windows.Count == 0 ? null : new Reading(windows, asOf);
    }
}
