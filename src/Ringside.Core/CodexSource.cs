using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Ringside;

/// <summary>
/// Codex usage from Codex's local session logs. Codex records the account's rate-limit
/// snapshot with every reply. No login token is read and nothing goes over the network.
/// </summary>
public sealed class CodexSource
{
    internal const string SourceText = "From Codex's local session log, which records your limits with every Codex reply.";
    const int TailBytes = 4 << 20;

    readonly string _root;

    sealed record Raw(
        DateTimeOffset At,
        string? Plan,
        List<(double Used, int? Minutes, DateTimeOffset? Resets)> Windows,
        decimal? Credits,
        bool Unlimited);

    // Per log: how far it has been read and the newest snapshot found so far. Live session
    // logs only grow, so each poll reads just the bytes appended since the last one.
    sealed class LogState
    {
        public long Offset;
        public DateTime Stamp;
        public Raw? Raw;
    }

    readonly Dictionary<string, LogState> _logs = new(StringComparer.OrdinalIgnoreCase);
    Raw? _last;

    public CodexSource(string? sessionsRoot = null)
    {
        _root = sessionsRoot ?? DefaultRoot();
    }

    static string DefaultRoot()
    {
        string? home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Paths.Home, ".codex");
        return Path.Combine(home, "sessions");
    }

    public bool SessionsFound => Directory.Exists(_root);

    public Snapshot Read(DateTimeOffset now)
    {
        Raw? best = null;
        try
        {
            var recent = RecentFiles().Take(4).ToList();
            foreach (var gone in _logs.Keys.Except(recent, StringComparer.OrdinalIgnoreCase).ToList()) _logs.Remove(gone);
            // Several sessions can run at once, so take the newest snapshot among the
            // most recently written logs.
            foreach (var f in recent)
                if (Update(f) is { } r && (best is null || r.At > best.At)) best = r;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        if (best is not null && (_last is null || best.At >= _last.At)) _last = best;
        return Build(_last, now);
    }

    List<string> RecentFiles()
    {
        if (!Directory.Exists(_root)) return new List<string>();
        var days = new List<string>();
        foreach (var y in Directory.EnumerateDirectories(_root).OrderByDescending(x => x, StringComparer.Ordinal))
        foreach (var m in Directory.EnumerateDirectories(y).OrderByDescending(x => x, StringComparer.Ordinal))
        foreach (var d in Directory.EnumerateDirectories(m).OrderByDescending(x => x, StringComparer.Ordinal))
        {
            days.Add(d);
            if (days.Count >= 10) goto Done;
        }
        Done:
        return days.SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .ToList();
    }

    Raw? Update(string path)
    {
        var fi = new FileInfo(path);
        if (!_logs.TryGetValue(path, out var st)) _logs[path] = st = new LogState();
        if (st.Stamp == fi.LastWriteTimeUtc && st.Offset == fi.Length) return st.Raw;

        using var fs = SharedFile.Open(path);
        long len = fs.Length;
        long start = st.Offset;
        bool cut = false;
        if (start > len) { start = 0; st.Raw = null; } // replaced by a shorter file
        if (len - start > TailBytes) { start = len - TailBytes; cut = true; }

        var (raw, end) = ScanFrom(fs, start, len, cut);
        if (raw is not null) st.Raw = raw;
        st.Offset = end;
        st.Stamp = fi.LastWriteTimeUtc;
        return st.Raw;
    }

    // Reads complete lines in [start, len) and returns the newest snapshot plus the offset
    // just past the last complete line, so a half-written line is read next time.
    static (Raw? Raw, long End) ScanFrom(FileStream fs, long start, long len, bool cut)
    {
        int count = (int)(len - start);
        if (count <= 0) return (null, start);
        var buf = new byte[count];
        fs.Seek(start, SeekOrigin.Begin);
        int read = 0;
        while (read < count)
        {
            int n = fs.Read(buf, read, count - read);
            if (n == 0) break;
            read += n;
        }
        if (read == 0) return (null, start);
        int lastNl = Array.LastIndexOf(buf, (byte)'\n', read - 1);
        if (lastNl < 0) return (null, start);
        int from = 0;
        if (cut) from = Array.IndexOf(buf, (byte)'\n', 0, lastNl + 1) + 1; // the first line is cut
        string text = Encoding.UTF8.GetString(buf, from, lastNl + 1 - from);

        for (int end = text.Length; end > 0;)
        {
            int nl = text.LastIndexOf('\n', end - 1);
            var line = text.AsSpan(nl + 1, end - nl - 1).TrimEnd('\r');
            end = Math.Max(nl, 0);
            if (line.IsEmpty || !line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
            if (line.Contains("\"rate_limits\":null", StringComparison.Ordinal)) continue;
            if (Parse(line.ToString()) is { } r) return (r, start + lastNl + 1);
        }
        return (null, start + lastNl + 1);
    }

    static Raw? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!Find(root, "rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object) return null;

            var at = DateTimeOffset.MinValue;
            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p))
                at = p;

            var windows = new List<(double, int?, DateTimeOffset?)>();
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
                if (!w.TryGetProperty("used_percent", out var up) || up.ValueKind != JsonValueKind.Number) continue;
                int? minutes = w.TryGetProperty("window_minutes", out var wm) && wm.ValueKind == JsonValueKind.Number
                    ? (int)wm.GetDouble() : null;
                DateTimeOffset? resets = null;
                if (w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number)
                    resets = DateTimeOffset.FromUnixTimeSeconds((long)ra.GetDouble());
                else if (w.TryGetProperty("resets_in_seconds", out var rs) && rs.ValueKind == JsonValueKind.Number && at != DateTimeOffset.MinValue)
                    resets = at.AddSeconds(rs.GetDouble());
                windows.Add((up.GetDouble(), minutes, resets));
            }
            if (windows.Count == 0) return null;

            decimal? credits = null;
            bool unlimited = false;
            if (rl.TryGetProperty("credits", out var cr) && cr.ValueKind == JsonValueKind.Object)
            {
                unlimited = cr.TryGetProperty("unlimited", out var un) && un.ValueKind == JsonValueKind.True;
                bool has = cr.TryGetProperty("has_credits", out var hc) && hc.ValueKind == JsonValueKind.True;
                if (has && cr.TryGetProperty("balance", out var bal))
                {
                    if (bal.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(bal.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                        credits = b;
                    else if (bal.ValueKind == JsonValueKind.Number)
                        credits = bal.GetDecimal();
                }
            }

            string? plan = rl.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
            return new Raw(at, plan, windows, credits, unlimited);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    static bool Find(JsonElement e, string name, out JsonElement found)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.NameEquals(name)) { found = p.Value; return true; }
                if (Find(p.Value, name, out found)) return true;
            }
        }
        found = default;
        return false;
    }

    static Snapshot Build(Raw? r, DateTimeOffset now)
    {
        if (r is null) return Snapshot.Empty("Codex", "Waiting for a Codex reply", SourceText);

        var windows = r.Windows
            .OrderBy(w => w.Minutes ?? int.MaxValue)
            .Select(w =>
            {
                bool lapsed = w.Resets is { } x && now >= x;
                return new LimitWindow(Label(w.Minutes), lapsed ? 0 : w.Used, w.Resets,
                    w.Resets is null ? ResetKind.None : ResetKind.Exact, lapsed);
            })
            .ToList();

        string? note = r.Unlimited ? "Unlimited credits"
            : r.Credits is decimal c ? string.Format(CultureInfo.CurrentCulture, "{0:N0} credits left", c)
            : null;
        return new Snapshot("Codex", Title(r.Plan), windows, r.At == DateTimeOffset.MinValue ? null : r.At, null, note, SourceText);
    }

    internal static string Label(int? minutes) => minutes switch
    {
        null => "Limit",
        300 => "5-hour",
        1440 => "Daily",
        10080 => "Weekly",
        int m when m % 1440 == 0 => $"{m / 1440}-day",
        int m when m % 60 == 0 => $"{m / 60}-hour",
        int m => $"{m}-min",
    };

    static string? Title(string? plan) =>
        string.IsNullOrWhiteSpace(plan) ? null : char.ToUpperInvariant(plan[0]) + plan[1..];
}
