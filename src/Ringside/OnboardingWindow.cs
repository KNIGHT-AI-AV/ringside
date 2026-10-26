using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>
/// First run: eight picture cards that set Ringside up and show how to use it. Every
/// choice takes effect at once and can be changed later in Settings.
/// </summary>
public sealed class OnboardingWindow : CardWindow
{
    readonly IHost _host;
    readonly Detection _found;
    readonly bool _animate;
    readonly TransitioningContentControl _pageHost = new() { Height = 548 };
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _step = new() { FontSize = 11, Foreground = Brand.Fg3Brush, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _back, _next;
    readonly List<Func<Control>> _pages;
    int _index;
    bool _hook, _login = true, _updates = true;

    Settings S => _host.Settings;

    public int PageCount => _pages.Count;

    public OnboardingWindow(IHost host, Detection? demo = null, int start = 0, bool animate = true) : base(640, 20)
    {
        _host = host;
        _found = demo ?? Detection.Scan();
        _animate = animate;
        Title = "Welcome to Ringside";
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _hook = _found.ClaudeCode && _found.BridgeAvailable && !Os.IsTranslocated;
        _updates = S.CheckForUpdates;
        if (animate) _pageHost.PageTransition = new CrossFade(TimeSpan.FromMilliseconds(220));

        // Only one of the apps here? Start with just that meter.
        if (!S.OnboardingDone && demo is null)
        {
            bool claude = _found.ClaudeDesktop || _found.ClaudeCode, codex = _found.Codex;
            if (claude != codex) host.Change(s => { s.ShowClaude = claude; s.ShowCodex = codex; });
        }

        _pages = new List<Func<Control>> { Welcome, Found, Views, Fill, Colors, Alerts, Moves, Finish };

        var close = new Button { Classes = { "icon" }, Content = Brand.Icon("close", 10), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var header = new Grid
        {
            Height = 34, Background = Brushes.Transparent,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                    Children = { Kit.Logo(22), new TextBlock { Text = "Ringside", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush, VerticalAlignment = VerticalAlignment.Center } },
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Children = { _step, close } },
            },
        };
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        _back = Kit.Ghost("Back", () => GoTo(_index - 1));
        _next = Kit.Primary("Next", () => { if (_index == _pages.Count - 1) Finish_(); else GoTo(_index + 1); });
        _next.MinWidth = 150;
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 14, 0, 0),
            Children = { _back, _dots, _next },
        };
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(_dots, 1);
        Grid.SetColumn(_next, 2);
        _next.HorizontalAlignment = HorizontalAlignment.Right;

        Card.Padding = new Thickness(28, 16, 28, 22);
        Card.Child = new StackPanel { Children = { header, new Border { Height = 12 }, _pageHost, footer } };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Right && _index < _pages.Count - 1) GoTo(_index + 1);
            else if (e.Key == Key.Left && _index > 0) GoTo(_index - 1);
            else if (e.Key == Key.Escape) Close();
        };
        GoTo(start);

        // Windows will not let a just-started app take the foreground, so lift the welcome
        // cards above everything once, then let them behave like a normal window.
        Opened += async (_, _) =>
        {
            Topmost = true;
            Activate();
            await System.Threading.Tasks.Task.Delay(400);
            Topmost = false;
        };
    }

    public void GoTo(int index)
    {
        _index = Math.Clamp(index, 0, _pages.Count - 1);
        _pageHost.Content = _pages[_index]();
        _back.IsVisible = _index > 0;
        _next.Content = _index == _pages.Count - 1 ? "Start Ringside" : _index == 0 ? "Let's set it up" : "Next";
        _step.Text = $"{_index + 1} of {_pages.Count}";
        _dots.Children.Clear();
        for (int i = 0; i < _pages.Count; i++)
            _dots.Children.Add(new Border
            {
                Width = i == _index ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3),
                Background = i == _index ? Kit.Res("BrandGradient") : new SolidColorBrush(Color.Parse("#33FFFFFF")),
            });
    }

    void Finish_()
    {
        if (!_host.Demo)
        {
            if (_hook && !ClaudeCodeHook.IsConnected) ClaudeCodeHook.Connect();
            AutoStart.Set(_login);
        }
        _host.Change(s => { s.CheckForUpdates = _updates; s.OnboardingDone = true; });
        Close();
    }

    // ---- building blocks --------------------------------------------------------------

    static Control Page(Control hero, string title, string body, Control? options = null, double heroHeight = 286)
    {
        var p = new StackPanel { Children = { Kit.Stage(hero, heroHeight), Kit.Text(title, "h1", 18), Kit.Text(body, "body", 6) } };
        if (options is not null) { options.Margin = new Thickness(0, 12, 0, 0); p.Children.Add(options); }
        return p;
    }

    Control Preview(Settings look, IReadOnlyList<string> providers, Reading? reading = null, double scale = 1)
    {
        var v = new WidgetView(_animate) { Interactive = false };
        v.Configure(look, providers);
        v.Show(reading ?? Reading.Sample(DateTimeOffset.Now));
        if (Math.Abs(scale - 1) < 0.01) return v;
        return new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = v, ClipToBounds = false };
    }

    Settings Look(Action<Settings>? edit = null)
    {
        var s = S.Clone();
        s.ShowClaude = s.ShowCodex = true;
        s.Split = false;
        edit?.Invoke(s);
        return s;
    }

    static readonly string[] Both = { "Claude", "Codex" };

    // ---- the cards --------------------------------------------------------------------

    Control Welcome()
    {
        var hero = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = -6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Preview(Look(s => s.View = ViewKind.Orb), Both, scale: 1.2),
                Preview(Look(s => { s.View = ViewKind.Card; s.Gauge = GaugeStyle.Ring; }), Both, scale: 0.92),
            },
        };
        hero.Margin = new Thickness(0, 10, 0, 0);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Art.Chip("100% free"), Art.Chip("Open source"), Art.Chip("No account"), Art.Chip("Your usage stays on " + Paths.ThisComputer) } };
        return Page(hero, "Meet Ringside",
            $"Your Claude and Codex limits, always in view. Ringside reads the usage numbers your apps already keep on {Paths.ThisComputer}. It never reads your login and never sends your usage anywhere.",
            chips, heroHeight: 330);
    }

    Control Found()
    {
        bool any = _found.ClaudeDesktop || _found.ClaudeCode || _found.Codex;
        CheckBox? claude = null, codex = null;
        claude = Kit.Switch("Claude meter", null, S.ShowClaude, on =>
        {
            _host.Change(s => { s.ShowClaude = on; if (!on && !s.ShowCodex) s.ShowCodex = true; });
            codex!.IsChecked = S.ShowCodex;
        });
        codex = Kit.Switch("Codex meter", null, S.ShowCodex, on =>
        {
            _host.Change(s => { s.ShowCodex = on; if (!on && !s.ShowClaude) s.ShowClaude = true; });
            claude.IsChecked = S.ShowClaude;
        });
        return Page(Art.AppTiles(_found),
            any ? "Here's what Ringside found" : "Nothing to read yet",
            (any ? "" : "That's fine: Ringside fills in on its own as soon as one of these runs. ") +
            "Only use one of them? Keep just that meter. You can change it any time.",
            new StackPanel { Children = { claude, codex } });
    }

    Control Views()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (view, name, scale) in new[] { (ViewKind.Card, "Card", 0.5), (ViewKind.Pill, "Pill", 0.6), (ViewKind.Orb, "Orb", 0.78) })
        {
            var tile = new RadioButton
            {
                Classes = { "tile" }, GroupName = "view", IsChecked = S.View == view, Width = 176,
                Content = new StackPanel
                {
                    Children =
                    {
                        new Grid { Height = 170, Children = { Center(Preview(Look(s => s.View = view), Both, scale: scale)) } },
                        new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) },
                    },
                },
            };
            var v = view;
            tile.IsCheckedChanged += (_, _) => { if (tile.IsChecked == true) _host.Change(s => s.View = v); };
            row.Children.Add(tile);
        }
        return Page(row, "Pick your view",
            "Card shows everything. Pill is one slim line. Orb is the Ringside logo come to life: Claude's ring outside, Codex's inside. Double-click the meter to switch any time.");
    }

    Control Fill()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        double scale = S.View switch { ViewKind.Card => 0.66, ViewKind.Pill => 0.8, _ => 0.95 };
        foreach (var (fill, name, sub) in new[] { (FillMode.Used, "Fills up", "42% used"), (FillMode.Left, "Drains like a tank", "58% left") })
        {
            var tile = new RadioButton
            {
                Classes = { "tile" }, GroupName = "fill", IsChecked = S.Fill == fill, Width = 262,
                Content = new StackPanel
                {
                    Children =
                    {
                        new Grid { Height = 182, Children = { Center(Preview(Look(s => s.Fill = fill), Both, scale: scale)) } },
                        new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) },
                        new TextBlock { Text = sub, FontSize = 11.5, Foreground = Brand.Fg2Brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) },
                    },
                },
            };
            var f = fill;
            tile.IsCheckedChanged += (_, _) => { if (tile.IsChecked == true) _host.Change(s => s.Fill = f); };
            row.Children.Add(tile);
        }
        return Page(row, "Fill up, or tank?",
            "Watch usage climb toward the limit, or watch what's left drain like a fuel gauge. Every number, tooltip and alert speaks the same way.");
    }

    Control Colors()
    {
        var stage = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        void Redraw() => stage.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Preview(Look(s => s.View = ViewKind.Orb), Both, scale: 1.05),
                Preview(Look(s => s.View = ViewKind.Pill), Both, scale: 1.0),
            },
        };
        Redraw();
        var options = new StackPanel
        {
            Children =
            {
                Labeled("Claude", Kit.Swatches(S.ClaudeColor, hex => { _host.Change(s => s.ClaudeColor = hex); Redraw(); })),
                Labeled("Codex", Kit.Swatches(S.CodexColor, hex => { _host.Change(s => s.CodexColor = hex); Redraw(); })),
                Kit.Row("Gauges", Kit.Segments(new[] { (ColorMode.AppColors, "App colors"), (ColorMode.TrafficLight, "Traffic light") }, S.Colors,
                    m => { _host.Change(s => s.Colors = m); Redraw(); })),
            },
        };
        return Page(stage, "Make it yours",
            "Pick a color for each app. Gauges still turn amber, then red, as a limit gets close. Prefer green, amber and red? Choose traffic light.",
            options, heroHeight: 218);

        static Control Labeled(string name, Control swatches) => new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("64,*"),
            Children = { new TextBlock { Text = name, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) }, Col1(swatches) },
        };
        static Control Col1(Control c) { Grid.SetColumn(c, 1); return c; }
    }

    Control Alerts()
    {
        var now = DateTimeOffset.Now;
        var reading = Reading.Sample(now, claude: 81, claudeRunsOut: now.AddMinutes(52));
        var accent = Brand.ToColor(Palette.Amber);
        var hero = new Grid
        {
            Children =
            {
                new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 6, 0, 0),
                    Child = Preview(Look(s => { s.View = ViewKind.Card; s.Forecast = true; }), new[] { "Claude" }, reading, 0.92) },
                new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 22, 20),
                    Child = Art.ToastMock(S.Fill, accent) },
            },
        };
        var options = new StackPanel
        {
            Children =
            {
                Kit.Switch("Alert me near the limit", $"Amber at {Display.Threshold(S.WarnAt, S.Fill)}, red at {Display.Threshold(S.AlertAt, S.Fill)}.",
                    S.NotifyAtWarn || S.NotifyAtAlert, on => _host.Change(s => { s.NotifyAtWarn = on; s.NotifyAtAlert = on; })),
                Kit.Switch("Smart forecast", "Warns you when your current pace will run out before the reset.",
                    S.Forecast && S.NotifyForecast, on => _host.Change(s => { s.Forecast = on; s.NotifyForecast = on; })),
                Kit.Switch("Tell me when a limit resets", null, S.NotifyOnReset, on => _host.Change(s => s.NotifyOnReset = on)),
            },
        };
        return Page(hero, "Stay ahead of the limit",
            "Ringside nudges you as a limit gets close, and its forecast spots a fast climb before it bites.",
            options, heroHeight: 250);
    }

    Control Moves() => Page(Art.Moves(Os.TrayName), "Handy moves",
        $"The {Os.TrayName} icon shows or hides Ringside and holds every option. Settings has the rest: sizes, thresholds, colors and more.");

    Control Finish()
    {
        bool hookPossible = _found.ClaudeCode && _found.BridgeAvailable && !Os.IsTranslocated;
        string hookHint = !_found.ClaudeCode ? "Needs Claude Code. You can turn it on later in Settings."
            : Os.IsTranslocated ? "Move Ringside to your Applications folder first, then turn this on in Settings."
            : "Adds Ringside's small Claude Code plugin and status line link. Reads no credentials; your own status line keeps working.";
        var hook = Kit.Switch("Live Claude limits", hookHint, _hook && hookPossible, on => _hook = on);
        hook.IsEnabled = hookPossible || _host.Demo;
        var options = new StackPanel
        {
            Children =
            {
                hook,
                Kit.Switch(Os.LoginItemLabel, null, _login, on => _login = on),
                Kit.Switch("Check for updates", Updates.CheckDescription, _updates, on => _updates = on),
            },
        };
        var meter = Preview(Look(s => s.View = ViewKind.Orb), Both, scale: 0.55);
        return Page(Center(Art.LiveSurfaces(S.Fill, meter, surfaceWidth: 118)), "Last step",
            "Claude Code can hand Ringside its exact limits after every reply, in the terminal, the Claude app and VS Code. Connect it and Claude's numbers stay live.",
            options, heroHeight: 196);
    }

    static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }
}
