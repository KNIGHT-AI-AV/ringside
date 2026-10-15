using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ringside;

/// <summary>
/// Renders the README images from sample data: Ringside.exe --render-screenshots &lt;folder&gt;.
/// Sample numbers keep anyone's real usage out of the repository.
/// </summary>
static class Screenshots
{
    public static int Render(string dir)
    {
        var app = new App();
        app.InitializeComponent();
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;

        var claude = new Snapshot("Claude", null, new[]
        {
            new LimitWindow("5-hour", 42, now.AddMinutes(134), ResetKind.Exact, false),
            new LimitWindow("Weekly", 18, now.AddDays(3).AddHours(5), ResetKind.Exact, false),
        }, now.AddMinutes(-1), null, null, ClaudeSource.FeedText);
        var codex = new Snapshot("Codex", "Pro", new[]
        {
            new LimitWindow("5-hour", 31, now.AddMinutes(201), ResetKind.Exact, false),
            new LimitWindow("Weekly", 64, now.AddDays(4).AddHours(2), ResetKind.Exact, false),
        }, now.AddMinutes(-4), null, null, CodexSource.SourceText);
        var hot = claude with
        {
            Windows = new[]
            {
                new LimitWindow("5-hour", 93, now.AddMinutes(38), ResetKind.Exact, false),
                new LimitWindow("Weekly", 57, now.AddDays(3).AddHours(5), ResetKind.Exact, false),
            },
        };
        var tight = codex with
        {
            Windows = new[]
            {
                new LimitWindow("5-hour", 12, now.AddMinutes(260), ResetKind.Exact, false),
                new LimitWindow("Weekly", 81, now.AddDays(2).AddHours(7), ResetKind.Exact, false),
            },
        };

        Widget(new Settings { HintShown = true }, claude, codex, now, Path.Combine(dir, "widget.png"));
        Widget(new Settings { HintShown = true }, hot, tight, now, Path.Combine(dir, "widget-alert.png"));
        Widget(new Settings { HintShown = true, Compact = true }, claude, codex, now, Path.Combine(dir, "compact.png"));
        Save(new OnboardingWindow(new Settings(), new Detection(true, true, true, true)), Path.Combine(dir, "onboarding.png"));
        Save(new SettingsWindow(new Settings(), null, demo: true), Path.Combine(dir, "settings.png"));
        return 0;
    }

    static void Widget(Settings s, Snapshot claude, Snapshot codex, DateTimeOffset now, string path)
    {
        var w = new MainWindow(s, live: false);
        w.Show(claude, codex, now);
        Save(w, path);
    }

    static void Save(Window w, string path)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -20000;
        w.Top = -20000;
        w.ShowActivated = false;
        w.Show();
        w.UpdateLayout();
        Wait(450); // let the switches finish sliding into place

        const double scale = 2;
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(w.ActualWidth * scale), (int)Math.Ceiling(w.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(w);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) png.Save(fs);
        w.Close();
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
