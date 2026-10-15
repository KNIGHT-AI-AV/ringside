using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ringside.Ui;

namespace Ringside.Render;

/// <summary>
/// Draws Ringside's windows with sample data straight to PNG: README images, release notes
/// and the frames of the demo GIF. Usage: Ringside.Render &lt;out dir&gt; [stills|frames|all]
/// </summary>
static class Program
{
    static readonly DateTimeOffset Now = new(2026, 10, 15, 14, 20, 0, TimeSpan.FromHours(-7));
    static string _out = ".";

    static int Main(string[] args)
    {
        _out = Path.GetFullPath(args.Length > 0 ? args[0] : "renders");
        string what = args.Length > 1 ? args[1] : "all";
        Directory.CreateDirectory(_out);
        // Keep the renders away from the user's own settings.
        Environment.SetEnvironmentVariable("RINGSIDE_DATA", Path.Combine(Path.GetTempPath(), "ringside-render"));
        App.RenderOnly = true;

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .SetupWithoutStarting();

        if (what is "all" or "stills") Stills();
        if (what is "all" or "frames") Frames();
        Console.WriteLine("rendered to " + _out);
        return 0;
    }

    // ---- stills -----------------------------------------------------------------------

    static Settings Look(Action<Settings>? edit = null)
    {
        var s = new Settings { OnboardingDone = true, HintShown = true };
        edit?.Invoke(s);
        s.Normalize();
        return s;
    }

    static WidgetView Widget(Settings s, Reading r, params string[] providers)
    {
        var v = new WidgetView(animate: false) { Interactive = false };
        v.Configure(s, providers.Length == 0 ? new[] { "Claude", "Codex" } : providers);
        v.Show(r);
        return v;
    }

    static void Stills()
    {
        var r = Reading.Sample(Now);
        var hot = Reading.Sample(Now, claude: 93, codex: 78, claudeRunsOut: null);
        var climbing = Reading.Sample(Now, claude: 81, claudeRunsOut: Now.AddMinutes(52));

        Save("widget-card", Desk(Widget(Look(), climbing)));
        Save("widget-card-left", Desk(Widget(Look(s => s.Fill = FillMode.Left), r)));
        Save("widget-bars", Desk(Widget(Look(s => s.Gauge = GaugeStyle.Bar), r)));
        Save("widget-bars-left", Desk(Widget(Look(s => { s.Gauge = GaugeStyle.Bar; s.Fill = FillMode.Left; }), r)));
        Save("widget-pill", Desk(Widget(Look(s => s.View = ViewKind.Pill), r)));
        Save("widget-orb", Desk(Widget(Look(s => s.View = ViewKind.Orb), r)));
        Save("widget-alert", Desk(Widget(Look(), hot)));
        Save("widget-traffic", Desk(Widget(Look(s => { s.View = ViewKind.Orb; s.Colors = ColorMode.TrafficLight; }), r)));

        // Every view side by side, and the colors at work.
        Save("views", Desk(Row(
            Widget(Look(), r),
            new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0, Children = { Widget(Look(s => s.View = ViewKind.Orb), r), Widget(Look(s => s.View = ViewKind.Pill), r) } })));
        Save("used-vs-left", Desk(Row(Widget(Look(s => s.View = ViewKind.Orb), r), Widget(Look(s => { s.View = ViewKind.Orb; s.Fill = FillMode.Left; }), r))));
        Save("colors", Desk(Row(
            Widget(Look(s => { s.View = ViewKind.Orb; s.ClaudeColor = "#8B7CF6"; s.CodexColor = "#4EA8F5"; }), r),
            Widget(Look(s => { s.View = ViewKind.Orb; s.ClaudeColor = "#EF5D8C"; s.CodexColor = "#E2B53C"; }), r),
            Widget(Look(s => { s.View = ViewKind.Orb; s.Colors = ColorMode.TrafficLight; }), r))));
        Save("split", Desk(Row(
            Widget(Look(s => s.View = ViewKind.Orb), r, "Claude"),
            new Border { Width = 70 },
            Widget(Look(s => s.View = ViewKind.Orb), r, "Codex"))));

        // Transparent cut-outs for the social card.
        Save("orb-clear", Widget(Look(s => s.View = ViewKind.Orb), r));
        Save("card-clear", Widget(Look(), r));

        // Windows.
        for (int i = 0; i < 8; i++)
        {
            var o = new OnboardingWindow(new DemoHost(Look(s => s.OnboardingDone = false), r), Detection.Everything, i, animate: false);
            Save($"onboarding-{i + 1}", Desk(Detach(o)));
            o.Close();
        }
        foreach (var page in new[] { SettingsWindow.Page.Look, SettingsWindow.Page.Colors, SettingsWindow.Page.Alerts, SettingsWindow.Page.ClaudeCode })
        {
            var w = new SettingsWindow(new DemoHost(Look(), climbing), page, animate: false);
            Save("settings-" + page.ToString().ToLowerInvariant(), Desk(Detach(w)));
            w.Close();
        }
        var panel = new QuickPanel(Look(), new QuickPanel.Actions(_ => { }, _ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, null, null, () => { }));
        Save("quick-panel", Desk(Detach(panel)));
        var toast = new Toast("Claude 5-hour limit may run out soon", "At this pace it runs out around 3:12 PM, before it resets at 4:34 PM.", Brand.ToColor(Palette.Amber), null);
        Save("toast", Desk(Detach(toast)));
    }

    /// <summary>Takes a window's content out so it can be drawn on the backdrop.</summary>
    static Control Detach(Window w)
    {
        var c = (Control)w.Content!;
        w.Content = null;
        return c;
    }

    static Control Row(params Control[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    /// <summary>A dark desktop-like backdrop, so the soft shadows read like they do on screen.</summary>
    static Control Desk(Control content, double pad = 26) => new Border
    {
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#2A2540"), 0), new GradientStop(Color.Parse("#14141B"), 0.55), new GradientStop(Color.Parse("#0F1A1A"), 1) },
        },
        Padding = new Thickness(pad),
        Child = content,
    };

    static void Save(string name, Control content, double scale = 2)
    {
        var path = Path.Combine(_out, name + ".png");
        Capture(content, scale, path);
        Console.WriteLine(path);
    }

    static void Capture(Control content, double scale, string path)
    {
        // Two passes: lay out on a huge canvas to learn the natural size (a headless screen
        // would otherwise cap auto-sized windows at 1080p), then size the window to fit exactly.
        var scaled = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scale, scale), Child = content,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var host = new Window
        {
            Width = 6000, Height = 6000,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            Content = scaled,
        };
        host.Show();
        Pump();
        var size = scaled.Bounds.Size;
        host.Width = Math.Ceiling(size.Width);
        host.Height = Math.Ceiling(size.Height);
        Pump();
        var frame = host.CaptureRenderedFrame();
        frame?.Save(path);
        host.Content = null;
        host.Close();
    }

    static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    // ---- animation frames -------------------------------------------------------------

    /// <summary>
    /// The demo GIF: rings climb, the meter switches views, flips to "left", recolors, splits
    /// apart and joins again. Frames are numbered; tools/make-media.py assembles them.
    /// </summary>
    static void Frames()
    {
        var dir = Path.Combine(_out, "frames");
        Directory.CreateDirectory(dir);
        int n = 0;
        void Frame(Control c) => Capture(Desk(new Border { Width = 560, Height = 300, Child = Center(c) }), 1.5, Path.Combine(dir, $"f{n++:000}.png"));
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; return c; }
        static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

        // 1. Card: usage climbs from nothing.
        for (int i = 0; i <= 12; i++)
        {
            double t = Ease(i / 12.0);
            Frame(Widget(Look(), Reading.Sample(Now, claude: 42 * t, codex: 18 * t, claudeWeek: 31 * t, codexWeek: 12 * t)));
        }
        Hold(Frame, () => Widget(Look(), Reading.Sample(Now)), 8);

        // 2. Flip to "left": the same limits drain like a tank.
        for (int i = 0; i <= 10; i++)
        {
            double t = Ease(i / 10.0);
            // The ring eases from the used reading (42) to the left reading (58).
            var s = Look(x => x.Fill = FillMode.Left);
            Frame(Widget(s, Reading.Sample(Now, claude: 100 - (58 + (42 - 58) * (1 - t)), codex: 100 - (82 + (18 - 82) * (1 - t)),
                claudeWeek: 100 - (69 + (31 - 69) * (1 - t)), codexWeek: 100 - (88 + (12 - 88) * (1 - t)))));
        }
        Hold(Frame, () => Widget(Look(x => x.Fill = FillMode.Left), Reading.Sample(Now)), 8);

        // 3. Views: pill, then the orb.
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Pill), Reading.Sample(Now)), 8);
        for (int i = 0; i <= 10; i++)
        {
            double t = Ease(i / 10.0);
            Frame(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now, claude: 42 * t, codex: 18 * t)));
        }
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now)), 6);

        // 4. Colors.
        foreach (var (c1, c2) in new[] { ("#8B7CF6", "#4EA8F5"), ("#EF5D8C", "#E2B53C"), ("#4EA8F5", "#8CCB45") })
            Hold(Frame, () => Widget(Look(x => { x.View = ViewKind.Orb; x.ClaudeColor = c1; x.CodexColor = c2; }), Reading.Sample(Now)), 5);

        // 5. Split apart, then join.
        for (int i = 0; i <= 8; i++)
        {
            double gap = 140 * Ease(i / 8.0);
            Frame(Row(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Claude"), new Border { Width = gap }, Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Codex")));
        }
        Hold(Frame, () => Row(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Claude"), new Border { Width = 140 }, Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Codex")), 6);
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now)), 6);
        Hold(Frame, () => Widget(Look(), Reading.Sample(Now)), 6);
        Console.WriteLine($"{n} frames in {dir}");
    }

    static void Hold(Action<Control> frame, Func<Control> make, int count)
    {
        for (int i = 0; i < count; i++) frame(make());
    }
}
