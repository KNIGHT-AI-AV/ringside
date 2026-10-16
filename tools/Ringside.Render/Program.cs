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
        if (what is "all" or "stills") Social();
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

        // Live Claude limits: Claude Code's three homes feeding the meter.
        Save("live-limits", Desk(Art.LiveSurfaces(FillMode.Used, Widget(Look(s => s.View = ViewKind.Orb), r)), pad: 34));

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

    static Control Scaled(Control c, double scale) => new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = c };

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
        string caption = "";
        void Frame(Control c)
        {
            var label = new TextBlock
            {
                Text = caption, FontSize = 15, FontWeight = FontWeight.Medium, Foreground = Brand.Fg2Brush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14),
            };
            var scaled = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.2, 1.2), Child = c, ClipToBounds = false };
            var stage = new Grid { Width = 600, Height = 330, RowDefinitions = new RowDefinitions("*,Auto"), Children = { Center(scaled), label } };
            Grid.SetRow(label, 1);
            Capture(Desk(stage), 1.5, Path.Combine(dir, $"f{n++:000}.png"));
        }
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; return c; }
        static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

        // 1. Card: usage climbs from nothing.
        caption = "Your Claude and Codex limits, always in view";
        for (int i = 0; i <= 12; i++)
        {
            double t = Ease(i / 12.0);
            Frame(Widget(Look(), Reading.Sample(Now, claude: 42 * t, codex: 18 * t, claudeWeek: 31 * t, codexWeek: 12 * t)));
        }
        Hold(Frame, () => Widget(Look(), Reading.Sample(Now)), 8);

        // 2. Flip to "left": the same limits, draining like a tank.
        caption = "Fill up, or drain like a tank";
        for (int i = 0; i <= 10; i++)
        {
            double t = Ease(i / 10.0);
            // The ring eases from the used reading (42) to the left reading (58).
            Frame(Widget(Look(x => x.Fill = FillMode.Left), Reading.Sample(Now, claude: 100 - (58 + (42 - 58) * (1 - t)), codex: 100 - (82 + (18 - 82) * (1 - t)),
                claudeWeek: 100 - (69 + (31 - 69) * (1 - t)), codexWeek: 100 - (88 + (12 - 88) * (1 - t)))));
        }
        Hold(Frame, () => Widget(Look(x => x.Fill = FillMode.Left), Reading.Sample(Now)), 8);

        // 3. Views: pill, then the orb.
        caption = "Three views: card, pill and orb";
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Pill), Reading.Sample(Now)), 8);
        for (int i = 0; i <= 10; i++)
        {
            double t = Ease(i / 10.0);
            Frame(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now, claude: 42 * t, codex: 18 * t)));
        }
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now)), 6);

        // 4. Colors.
        caption = "Your colors";
        foreach (var (c1, c2) in new[] { ("#8B7CF6", "#4EA8F5"), ("#EF5D8C", "#E2B53C"), ("#4EA8F5", "#8CCB45") })
            Hold(Frame, () => Widget(Look(x => { x.View = ViewKind.Orb; x.ClaudeColor = c1; x.CodexColor = c2; }), Reading.Sample(Now)), 5);

        // 5. Split apart, then join.
        caption = "Split them apart, snap them together";
        for (int i = 0; i <= 8; i++)
        {
            double gap = 120 * Ease(i / 8.0);
            Frame(Row(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Claude"), new Border { Width = gap }, Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Codex")));
        }
        Hold(Frame, () => Row(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Claude"), new Border { Width = 120 }, Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now), "Codex")), 6);
        Hold(Frame, () => Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now)), 6);

        // 6. The forecast.
        caption = "It warns you before you run out";
        Hold(Frame, () => Widget(Look(), Reading.Sample(Now, claude: 81, claudeRunsOut: Now.AddMinutes(52))), 10);

        // 7. Live Claude limits from wherever Claude Code runs.
        caption = "Live Claude limits from the terminal, the Claude app and VS Code";
        for (int i = 0; i <= 10; i++)
        {
            double t = Ease(i / 10.0);
            Frame(Art.LiveSurfaces(FillMode.Used, Scaled(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now, claude: 42 * t, codex: 18 * t)), 0.55), 104));
        }
        Hold(Frame, () => Art.LiveSurfaces(FillMode.Used, Scaled(Widget(Look(x => x.View = ViewKind.Orb), Reading.Sample(Now)), 0.55), 104), 10);
        Console.WriteLine($"{n} frames in {dir}");
    }

    static Control WithAlign(Control c) { c.HorizontalAlignment = HorizontalAlignment.Left; return c; }

    /// <summary>GitHub's social preview, 1280x640, drawn from the real controls.</summary>
    static void Social()
    {
        var r = Reading.Sample(Now);
        var left = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(84, 0, 0, 0), Width = 560,
            Children =
            {
                WithAlign(Kit.Logo(96)),
                new TextBlock { Text = "Ringside", FontSize = 84, FontWeight = FontWeight.Bold, Foreground = Brand.Fg1Brush, Margin = new Thickness(-3, 18, 0, 0), LetterSpacing = -1.5 },
                new TextBlock { Text = "Your Claude Code and Codex limits, always in view.", FontSize = 30, LineHeight = 40, Foreground = Brand.Fg2Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 26, 0, 0),
                    Children = { Art.Chip("Windows"), Art.Chip("macOS"), Art.Chip("Linux"), Art.Chip("Free and open source") } },
            },
        };
        var widgets = new Grid
        {
            Width = 600, HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 30, 0),
                    Child = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.3, 1.3), ClipToBounds = false, Child = Widget(Look(), r) } },
                new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 18),
                    Child = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.25, 1.25), ClipToBounds = false, Child = Widget(Look(s => s.View = ViewKind.Orb), r) } },
            },
        };
        var card = new Grid
        {
            Width = 1280, Height = 640, ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#2C2646"), 0), new GradientStop(Color.Parse("#16161D"), 0.55), new GradientStop(Color.Parse("#0E1716"), 1) },
            },
            Children = { left, widgets },
        };
        Save("social-card", card, scale: 1);
    }

    static void Hold(Action<Control> frame, Func<Control> make, int count)
    {
        for (int i = 0; i < count; i++) frame(make());
    }
}
