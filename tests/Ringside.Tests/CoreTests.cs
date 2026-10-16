using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Ringside.Tests;

public sealed class CoreTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);

    static LimitWindow Window(double used, double resetHours = 2) =>
        new("5-hour", used, Now.AddHours(resetHours), ResetKind.Exact, false);

    [Theory]
    [InlineData(6, FillMode.Used, 6, "6% used")]
    [InlineData(6, FillMode.Left, 94, "94% left")]
    [InlineData(100, FillMode.Left, 0, "0% left")]
    [InlineData(120, FillMode.Used, 100, "100% used")]
    public void Used_fills_up_and_left_drains(double used, FillMode fill, double shown, string amount)
    {
        Assert.Equal(shown, Display.Shown(used, fill));
        Assert.Equal(amount, Display.Amount(used, fill));
    }

    [Fact]
    public void Thresholds_read_in_the_words_of_the_mode()
    {
        Assert.Equal("75% used", Display.Threshold(75, FillMode.Used));
        Assert.Equal("25% left", Display.Threshold(75, FillMode.Left));
    }

    [Fact]
    public void Levels_follow_the_custom_thresholds()
    {
        var s = new Settings { WarnAt = 60, AlertAt = 80 };
        Assert.Equal(Level.Ok, Display.LevelOf(59, s));
        Assert.Equal(Level.Warn, Display.LevelOf(60, s));
        Assert.Equal(Level.Alert, Display.LevelOf(80, s));
    }

    [Fact]
    public void Reset_text_as_countdown_or_clock()
    {
        var w = Window(10, 2.5);
        Assert.Equal("resets in 2h 30m", Fmt.Reset(w, Now, ResetStyle.Countdown));
        Assert.StartsWith("resets at ", Fmt.Reset(w, Now, ResetStyle.Clock));
    }

    [Fact]
    public void Old_readings_say_which_day()
    {
        var now = new DateTimeOffset(2026, 10, 15, 17, 30, 0, TimeSpan.FromHours(-7));
        Assert.StartsWith("as of yesterday ", Fmt.AsOf(now.AddHours(-20), now));
        Assert.DoesNotContain("yesterday", Fmt.AsOf(now.AddHours(-2), now));
    }

    [Fact]
    public void Settings_from_1x_turn_compact_into_the_pill_view()
    {
        var s = Settings.Parse("""{"OnboardingDone":true,"Compact":true,"Scale":5,"ShowClaude":false,"ShowCodex":false}""");
        Assert.Equal(ViewKind.Pill, s.View);
        Assert.Null(s.Compact);
        Assert.Equal(1.6, s.Scale);
        Assert.True(s.ShowClaude && s.ShowCodex);
        Assert.DoesNotContain("Compact", s.ToJson());
    }

    [Fact]
    public void Settings_keep_enums_as_words_and_sane_thresholds()
    {
        var s = Settings.Parse("""{"Fill":"Left","View":"Orb","WarnAt":95,"AlertAt":50,"ClaudeColor":"nope"}""");
        Assert.Equal(FillMode.Left, s.Fill);
        Assert.Equal(ViewKind.Orb, s.View);
        Assert.True(s.AlertAt > s.WarnAt);
        Assert.Equal(Palette.ClaudeDefault, s.ClaudeColor);
        Assert.Contains("\"Fill\": \"Left\"", s.ToJson());
    }

    [Fact]
    public void Settings_know_when_they_came_from_1x()
    {
        Assert.True(Settings.Parse("""{"OnboardingDone":true}""").UpgradedFromV1);
        Assert.False(Settings.Parse("""{"OnboardingDone":true,"Version":2}""").UpgradedFromV1);
        Assert.False(Settings.Parse("""{"OnboardingDone":false}""").UpgradedFromV1);
    }

    [Fact]
    public void Split_needs_both_meters()
    {
        var s = Settings.Parse("""{"Split":true,"ShowCodex":false}""");
        Assert.False(s.Split);
    }

    [Fact]
    public void Palette_parses_and_shades()
    {
        Assert.True(Palette.TryParse("#8b7cf6", out var c));
        Assert.Equal("#8B7CF6", c.Hex);
        Assert.True(Palette.TryParse("fff", out var w));
        Assert.Equal("#FFFFFF", w.Hex);
        Assert.False(Palette.TryParse("#12345", out _));
        var (from, to) = Palette.Gradient(c);
        Assert.True(from.Luminance > c.Luminance && to.Luminance < c.Luminance);
        // The 1.x tints stay exact for the default colors.
        Assert.Equal("#F4A688", Palette.Gradient(Palette.Parse(Palette.ClaudeDefault, "")).From.Hex);
    }

    [Fact]
    public void Gauge_color_turns_amber_and_red_unless_turned_off()
    {
        var s = new Settings { CodexColor = "#4EA8F5" };
        Assert.Equal("#4EA8F5", Palette.Solid("Codex", 10, s).Hex);
        Assert.Equal(Palette.Amber, Palette.Solid("Codex", 80, s));
        Assert.Equal(Palette.Red, Palette.Solid("Codex", 95, s));
        s.LevelColors = false;
        Assert.Equal("#4EA8F5", Palette.Solid("Codex", 95, s).Hex);
        s.Colors = ColorMode.TrafficLight;
        Assert.Equal(Palette.Green, Palette.Solid("Codex", 10, s));
    }

    [Fact]
    public void Forecast_projects_a_steady_climb_before_the_reset()
    {
        // 30 % to 60 % in 40 minutes: 0.75 % a minute, so 100 % about 53 minutes later.
        var pts = new List<(DateTimeOffset, double)>
        {
            (Now.AddMinutes(-40), 30), (Now.AddMinutes(-20), 45), (Now, 60),
        };
        var eta = UsageHistory.Project(pts, Window(60, 3), Now);
        Assert.NotNull(eta);
        Assert.InRange((eta!.Value - Now).TotalMinutes, 50, 56);
        Assert.Contains("full by", Display.Forecast(eta, Now, FillMode.Used));
        Assert.Contains("empty by", Display.Forecast(eta, Now, FillMode.Left));
    }

    [Fact]
    public void Forecast_stays_quiet_when_the_reset_comes_first_or_usage_is_flat()
    {
        var climb = new List<(DateTimeOffset, double)> { (Now.AddMinutes(-40), 30), (Now, 60) };
        Assert.Null(UsageHistory.Project(climb, Window(60, 0.5), Now));        // resets in 30 min
        var flat = new List<(DateTimeOffset, double)> { (Now.AddMinutes(-40), 30), (Now, 30.2) };
        Assert.Null(UsageHistory.Project(flat, Window(30.2), Now));
        var tooShort = new List<(DateTimeOffset, double)> { (Now.AddMinutes(-3), 30), (Now, 40) };
        Assert.Null(UsageHistory.Project(tooShort, Window(40), Now));
    }

    [Fact]
    public void History_forgets_the_old_climb_after_a_reset()
    {
        var h = new UsageHistory();
        Snapshot At(DateTimeOffset t, double used) =>
            new("Codex", null, new[] { new LimitWindow("5-hour", used, Now.AddHours(4), ResetKind.Exact, false) }, t, null, null, "");
        h.Record(At(Now.AddMinutes(-50), 40), Now);
        h.Record(At(Now.AddMinutes(-30), 70), Now);
        h.Record(At(Now.AddMinutes(-10), 2), Now);   // reset
        h.Record(At(Now, 3), Now);
        Assert.Null(h.RunsOutAt("Codex", new LimitWindow("5-hour", 3, Now.AddHours(4), ResetKind.Exact, false), Now));
    }

    [Fact]
    public void Alerts_speak_in_left_mode()
    {
        var alerts = new Alerts();
        var s = new Settings { Fill = FillMode.Left };
        var h = new UsageHistory();
        Snapshot At(double pct) => new("Claude", null, new[] { Window(pct) }, Now, null, null, "");
        Assert.Empty(alerts.Evaluate(At(70), s, h, Now));
        var a = alerts.Evaluate(At(92), s, h, Now).Single();
        Assert.Equal("Claude 5-hour: 8% left", a.Title);
    }

    [Fact]
    public void Bridge_and_app_agree_on_the_data_folder() =>
        Assert.Equal(Paths.Data, StatusLineBridge.DataDir);

    [Fact]
    public void Bridge_summary_says_used_or_left()
    {
        var limits = JsonNode.Parse("""{"five_hour":{"used_percentage":42},"seven_day":{"used_percentage":10}}""")!.AsObject();
        Assert.Equal("5h 42% | 7d 10%", StatusLineBridge.Summary(limits, left: false));
        Assert.Equal("5h 58% left | 7d 90% left", StatusLineBridge.Summary(limits, left: true));
    }

    [Theory]
    [InlineData("\"C:/Users/a/AppData/Local/Ringside/current/ringside-statusline.exe\"", true)]
    [InlineData("\"/Applications/Ringside.app/Contents/MacOS/Ringside\" --statusline", true)]
    [InlineData("bash ~/.claude/statusline.sh", false)]
    public void Hook_recognizes_its_own_status_line(string command, bool ours) =>
        Assert.Equal(ours, ClaudeCodeHook.IsOurs(new JsonObject { ["type"] = "command", ["command"] = command }));

    [Fact]
    public void Hook_command_quotes_the_path_and_adds_the_flag_for_the_app()
    {
        Assert.Equal("'C:/x y/ringside-statusline.exe'", ClaudeCodeHook.Command(@"C:\x y\ringside-statusline.exe", false));
        Assert.Equal("'/Applications/Ringside.app/Contents/MacOS/Ringside' --statusline",
            ClaudeCodeHook.Command("/Applications/Ringside.app/Contents/MacOS/Ringside", true));
        // Shell syntax in a folder name stays literal.
        Assert.Equal(@"'/home/a$(x)/it'\''s/Ringside.AppImage' --statusline",
            ClaudeCodeHook.Command("/home/a$(x)/it's/Ringside.AppImage", true));
    }
}
