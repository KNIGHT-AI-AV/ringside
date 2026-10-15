using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ringside;

/// <summary>
/// Renders the README images from sample data, without opening any window:
///   Ringside.exe --render-screenshots &lt;folder&gt;   still images
///   Ringside.exe --render-demo &lt;folder&gt;          numbered frames for the demo GIF
/// Sample numbers keep anyone's real usage out of the repository.
/// </summary>
static class Screenshots
{
    public static int Render(string dir)
    {
        Start();
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;

        Snapshot claude = Claude(42, 18, now), codex = Codex(31, 64, now);
        Widget(new Settings { HintShown = true }, claude, codex, now, Path.Combine(dir, "widget.png"));
        Widget(new Settings { HintShown = true }, Claude(93, 57, now), Codex(12, 81, now), now, Path.Combine(dir, "widget-alert.png"));
        Widget(new Settings { HintShown = true, Compact = true }, claude, codex, now, Path.Combine(dir, "compact.png"));
        Snap(new OnboardingWindow(new Settings(), new Detection(true, true, true, true)), Path.Combine(dir, "onboarding.png"), 2, settle: true);
        Snap(new SettingsWindow(new Settings(), null, demo: true), Path.Combine(dir, "settings.png"), 2, settle: true);
        return 0;
    }

    /// <summary>The rings fill, climb into the alert colours, then the card folds into the compact pill.</summary>
    public static int RenderDemo(string dir)
    {
        Start();
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;
        var settings = new Settings { HintShown = true };
        var w = new MainWindow(settings, live: false);

        int n = 0;
        void Frame(double c5, double cW, double x5, double xW)
        {
            w.Show(Claude(c5, cW, now), Codex(x5, xW, now), now);
            Snap(w, Path.Combine(dir, $"f{n++:000}.png"), 1.5, settle: false);
        }
        static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

        for (int i = 0; i <= 20; i++) { double t = Ease(i / 20.0); Frame(42 * t, 18 * t, 31 * t, 64 * t); }
        for (int i = 1; i <= 16; i++) { double t = Ease(i / 16.0); Frame(42 + 51 * t, 18 + 39 * t, 31 - 19 * t, 64 + 17 * t); }
        settings.Compact = true;
        w.ApplySettings(anchor: false);
        Frame(93, 57, 12, 81);
        return 0;
    }

    static void Start()
    {
        App.RenderOnly = true; // the App queues its normal startup; renders must skip it
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    static Snapshot Claude(double fiveHour, double weekly, DateTimeOffset now) => new("Claude", null, new[]
    {
        new LimitWindow("5-hour", fiveHour, now.AddMinutes(fiveHour > 90 ? 38 : 134), ResetKind.Exact, false),
        new LimitWindow("Weekly", weekly, now.AddDays(3).AddHours(5), ResetKind.Exact, false),
    }, now.AddMinutes(-1), null, null, ClaudeSource.FeedText);

    static Snapshot Codex(double fiveHour, double weekly, DateTimeOffset now) => new("Codex", "Pro", new[]
    {
        new LimitWindow("5-hour", fiveHour, now.AddMinutes(fiveHour < 20 ? 260 : 201), ResetKind.Exact, false),
        new LimitWindow("Weekly", weekly, now.AddDays(weekly > 75 ? 2 : 4).AddHours(weekly > 75 ? 7 : 2), ResetKind.Exact, false),
    }, now.AddMinutes(-4), null, null, CodexSource.SourceText);

    static void Widget(Settings s, Snapshot claude, Snapshot codex, DateTimeOffset now, string path)
    {
        var w = new MainWindow(s, live: false);
        w.Show(claude, codex, now);
        Snap(w, path, 2, settle: false);
    }

    /// <summary>
    /// Lays out a window's content on its own and renders it, so no window is ever shown.
    /// The content goes back into the window afterwards, so the same window can render again.
    /// </summary>
    static void Snap(Window w, string path, double scale, bool settle)
    {
        var content = (UIElement)w.Content;
        w.Content = null;
        var host = new Border { Child = content, Background = Brushes.Transparent };
        host.SetValue(TextElement.FontFamilyProperty, w.FontFamily);
        TextOptions.SetTextFormattingMode(host, TextOptions.GetTextFormattingMode(w));
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        if (settle) Wait(450); // let the switches finish sliding into place

        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(host.ActualWidth * scale), (int)Math.Ceiling(host.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(host);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) png.Save(fs);

        host.Child = null;
        w.Content = content;
    }

    static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
