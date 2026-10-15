using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Ringside.Tests;

public sealed class SourceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ringside-tests-" + Guid.NewGuid().ToString("N"));

    public SourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    [Fact]
    public void Claude_log_estimates_the_reset_from_where_usage_dropped()
    {
        var now = DateTimeOffset.UtcNow;
        var t0 = now.AddMinutes(-60);
        var log = Write("plan-usage-history.json", $$$"""
            {"version":2,"samples":[
              {"t":{{{Ms(t0)}}},"org":"a","u":{"fh":92}},
              {"t":{{{Ms(t0.AddMinutes(15))}}},"org":"a","u":{"fh":2}},
              {"t":{{{Ms(t0.AddMinutes(30))}}},"org":"a","u":{"fh":7}},
              {"t":{{{Ms(t0.AddMinutes(45))}}},"org":"a","u":{"fh":19}}
            ]}
            """);
        var s = new ClaudeSource(log, Path.Combine(_dir, "none.json")).Read(now);

        var w = Assert.Single(s.Windows);
        Assert.Equal("5-hour", w.Label);
        Assert.Equal(19, w.Percent);
        Assert.Equal(ResetKind.Estimate, w.Kind);
        // The window began between the 92 and the 2 sample, so it resets 5 h after the midpoint.
        var expected = t0.AddMinutes(7.5).AddHours(5);
        Assert.InRange((w.ResetsAt!.Value - expected).Duration().TotalSeconds, 0, 1);
    }

    [Fact]
    public void Claude_log_follows_the_signed_in_account()
    {
        var now = DateTimeOffset.UtcNow;
        var log = Write("plan-usage-history.json", $$$"""
            {"samples":[
              {"t":{{{Ms(now.AddMinutes(-30))}}},"org":"old","u":{"fh":80,"sd":50}},
              {"t":{{{Ms(now.AddMinutes(-5))}}},"org":"new","u":{"fh":10}}
            ]}
            """);
        var s = new ClaudeSource(log, Path.Combine(_dir, "none.json")).Read(now);
        Assert.Equal(10, s.Primary!.Percent);
        Assert.DoesNotContain(s.Windows, w => w.Label == "Weekly");
    }

    [Fact]
    public void Claude_window_that_must_have_reset_reads_zero()
    {
        var now = DateTimeOffset.UtcNow;
        var log = Write("plan-usage-history.json", $$$"""
            {"samples":[{"t":{{{Ms(now.AddHours(-6))}}},"org":"a","u":{"fh":55}}]}
            """);
        var w = new ClaudeSource(log, Path.Combine(_dir, "none.json")).Read(now).Windows.Single();
        Assert.True(w.Lapsed);
        Assert.Equal(0, w.Percent);
    }

    [Fact]
    public void Claude_status_line_feed_wins_when_newer_and_gives_exact_resets()
    {
        var now = DateTimeOffset.UtcNow;
        var log = Write("plan-usage-history.json", $$$"""
            {"samples":[{"t":{{{Ms(now.AddMinutes(-20))}}},"org":"a","u":{"fh":30}}]}
            """);
        var resets = now.AddHours(2).ToUnixTimeSeconds();
        var feed = Write("claude-statusline.json", $$$"""
            {"updated":{{{now.AddMinutes(-1).ToUnixTimeSeconds()}}},
             "five_hour":{"used_percentage":34.5,"resets_at":{{{resets}}}},
             "seven_day":{"used_percentage":61,"resets_at":{{{now.AddDays(3).ToUnixTimeSeconds()}}}}}
            """);
        var s = new ClaudeSource(log, feed).Read(now);
        Assert.Equal(ClaudeSource.FeedText, s.Source);
        var five = s.Windows.Single(w => w.Label == "5-hour");
        Assert.Equal(34.5, five.Percent);
        Assert.Equal(ResetKind.Exact, five.Kind);
        Assert.Equal(resets, five.ResetsAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(61, s.Primary!.Percent);
    }

    [Fact]
    public void Codex_reads_the_newest_rate_limits_from_session_logs()
    {
        var now = DateTimeOffset.UtcNow;
        var resets = now.AddDays(2).ToUnixTimeSeconds();
        var root = Path.Combine(_dir, "sessions");
        Write(Path.Combine("sessions", "2026", "10", "14", "rollout-a.jsonl"),
            "{\"timestamp\":\"2026-10-14T10:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":null}}\n" +
            "{\"timestamp\":\"2026-10-14T10:05:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":" +
            "{\"primary\":{\"used_percent\":12.0,\"window_minutes\":300,\"resets_at\":" + now.AddHours(3).ToUnixTimeSeconds() + "}," +
            "\"secondary\":{\"used_percent\":47.0,\"window_minutes\":10080,\"resets_at\":" + resets + "}," +
            "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":\"1234.5\"},\"plan_type\":\"pro\"}}}\n");

        var s = new CodexSource(root).Read(now);
        Assert.Equal("Pro", s.Plan);
        Assert.Equal(new[] { "5-hour", "Weekly" }, s.Windows.Select(w => w.Label));
        Assert.Equal(47, s.Primary!.Percent);
        Assert.Equal(resets, s.Primary.ResetsAt!.Value.ToUnixTimeSeconds());
        Assert.Contains("credits left", s.Note);
    }

    [Fact]
    public void Codex_picks_up_lines_appended_after_the_first_read()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Path.Combine(_dir, "sessions");
        var log = Write(Path.Combine("sessions", "2026", "10", "15", "rollout-b.jsonl"),
            Line(20, now.AddDays(1)));
        var src = new CodexSource(root);
        Assert.Equal(20, src.Read(now).Primary!.Percent);

        File.AppendAllText(log, Line(35, now.AddDays(1)));
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(35, src.Read(now).Primary!.Percent);

        static string Line(double pct, DateTimeOffset reset) =>
            "{\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\",\"payload\":{\"rate_limits\":{\"primary\":" +
            "{\"used_percent\":" + pct + ",\"window_minutes\":10080,\"resets_at\":" + reset.ToUnixTimeSeconds() + "}}}}\n";
    }

    [Fact]
    public void Codex_without_logs_waits_politely()
    {
        var s = new CodexSource(Path.Combine(_dir, "missing")).Read(DateTimeOffset.UtcNow);
        Assert.Null(s.Primary);
        Assert.Equal("Waiting for a Codex reply", s.Note);
    }

    [Theory]
    [InlineData(300, "5-hour")]
    [InlineData(10080, "Weekly")]
    [InlineData(1440, "Daily")]
    [InlineData(120, "2-hour")]
    public void Codex_window_labels(int minutes, string label) => Assert.Equal(label, CodexSource.Label(minutes));

    [Fact]
    public void Alerts_fire_once_when_crossing_a_threshold()
    {
        var alerts = new Alerts();
        var settings = new Settings();
        var history = new UsageHistory();
        var now = DateTimeOffset.UtcNow;
        Snapshot At(double pct) => new("Claude", null,
            new[] { new LimitWindow("5-hour", pct, now.AddHours(1), ResetKind.Exact, false) }, now, null, null, "");

        Assert.Empty(alerts.Evaluate(At(70), settings, history, now));            // baseline
        Assert.Single(alerts.Evaluate(At(80), settings, history, now));           // crossed 75
        Assert.Empty(alerts.Evaluate(At(85), settings, history, now));
        Assert.Single(alerts.Evaluate(At(91), settings, history, now));           // crossed 90
        var reset = alerts.Evaluate(At(3), settings, history, now).Single();      // window reset
        Assert.Contains("reset", reset.Title);
    }
}
