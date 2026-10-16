using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>
/// Settings: a sidebar of sections and a live preview on top that redraws the meter the
/// moment anything changes.
/// </summary>
public sealed class SettingsWindow : CardWindow
{
    public enum Page { Look, Colors, Alerts, Behavior, ClaudeCode, About }

    readonly IHost _host;
    readonly bool _animate;
    Settings S => _host.Settings;
    readonly StackPanel _previewRow = new() { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _sampleNote = new() { FontSize = 10.5, Foreground = Brand.Fg3Brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 10) };
    readonly ContentControl _page = new();
    readonly TextBlock _pageTitle = new() { Classes = { "h2" }, FontSize = 18 };
    Page _current;
    Reading? _reading;

    public SettingsWindow(IHost host, Page start = Page.Look, bool animate = true) : base(880, 18)
    {
        _host = host;
        _animate = animate;
        Title = "Ringside Settings";
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var nav = new StackPanel { Spacing = 0 };
        nav.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(8, 4, 0, 22),
            Children =
            {
                Kit.Logo(26),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Ringside", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush },
                        new TextBlock { Text = "Settings", FontSize = 11, Foreground = Brand.Fg3Brush },
                    },
                },
            },
        });
        foreach (var (page, icon, label) in new[]
                 {
                     (Page.Look, "views", "Look"), (Page.Colors, "palette", "Colors"), (Page.Alerts, "bell", "Alerts"),
                     (Page.Behavior, "sliders", "Behavior"), (Page.ClaudeCode, "terminal", "Claude Code"), (Page.About, "info", "About"),
                 })
        {
            var rb = new RadioButton
            {
                Classes = { "nav" }, GroupName = "nav", IsChecked = page == start,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { new PathIcon { Data = Icons.Get(icon), Width = 13, Height = 13 }, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
                },
            };
            var p = page;
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) Go(p); };
            nav.Children.Add(rb);
        }
        var version = new TextBlock { Text = "Version " + Updates.CurrentVersion, FontSize = 11, Foreground = Brand.Fg3Brush, Margin = new Thickness(10, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom };
        var sidebar = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { nav, version },
        };
        Grid.SetRow(version, 1);
        var sideBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#08FFFFFF")),
            BorderBrush = Kit.Res("Hairline"), BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12, 18, 12, 14), Child = sidebar,
            CornerRadius = new CornerRadius(18, 0, 0, 18),
        };

        var close = new Button { Classes = { "icon" }, Content = Brand.Icon("close", 10), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var titleBar = new Grid { Height = 40, Background = Brushes.Transparent, Children = { _pageTitle, close } };
        _pageTitle.VerticalAlignment = VerticalAlignment.Center;
        titleBar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        // The preview shrinks to fit when a view is taller than the stage.
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _previewRow, Margin = new Thickness(6, 4, 6, 18) };
        var stage = Kit.Stage(new Grid { Children = { fit, _sampleNote } }, 236);
        // The scroll bar sits in the 14 px gutter right of the page. The gutter is padding on the
        // content itself: the viewer's own Padding does not narrow its content, which let the
        // text run under the window's edge.
        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(0, 0, 14, 0), Child = _page }, Height = 356, Margin = new Thickness(0, 14, -14, 0) };

        var right = new StackPanel { Margin = new Thickness(22, 10, 22, 18), Children = { titleBar, stage, scroll } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), Children = { sideBorder, right } };
        Grid.SetColumn(right, 1);
        Card.Child = grid;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ShowReading(host.Last);
        Go(start);
    }

    /// <summary>New numbers from the app; real ones when there are any, sample ones otherwise.</summary>
    public void ShowReading(Reading? r)
    {
        bool real = r is not null && (r.Claude.Windows.Count > 0 || r.Codex.Windows.Count > 0);
        _reading = real ? r : Reading.Sample(DateTimeOffset.Now);
        _sampleNote.Text = _host.Demo ? "" : real ? "Live preview" : "Preview with sample numbers";
        BuildPreview();
    }

    /// <summary>Called after any change: the preview redraws, the open page stays as it is.</summary>
    public void SettingsChanged() => BuildPreview();

    void BuildPreview()
    {
        _previewRow.Children.Clear();
        var groups = S.Split && S.ShowClaude && S.ShowCodex ? new[] { new[] { "Claude" }, new[] { "Codex" } } : new[] { new[] { "Claude", "Codex" } };
        foreach (var g in groups)
        {
            var v = new WidgetView(_animate) { Interactive = false };
            v.Configure(S, g);
            if (_reading is not null) v.Show(_reading);
            _previewRow.Children.Add(v);
        }
    }

    void Go(Page page)
    {
        _current = page;
        (_pageTitle.Text, _page.Content) = page switch
        {
            Page.Colors => ("Colors", (Control)ColorsPage()),
            Page.Alerts => ("Alerts", AlertsPage()),
            Page.Behavior => ("Behavior", BehaviorPage()),
            Page.ClaudeCode => ("Claude Code", ClaudeCodePage()),
            Page.About => ("About", AboutPage()),
            _ => ("Look", LookPage()),
        };
    }

    void Set(Action<Settings> edit) => _host.Change(edit);

    // ---- pages ------------------------------------------------------------------------

    Control LookPage()
    {
        var p = new StackPanel();
        p.Children.Add(Kit.Row("View", Kit.Segments(new[] { (ViewKind.Card, "Card"), (ViewKind.Pill, "Pill"), (ViewKind.Orb, "Orb") }, S.View, v => Set(s => s.View = v)),
            "Card shows everything, Pill is one slim line, Orb is the logo: a ring inside a ring."));
        p.Children.Add(Kit.Row("Show limits as", Kit.Segments(new[] { (FillMode.Used, "Used (fills up)"), (FillMode.Left, "Left (tank)") }, S.Fill, f => Set(s => s.Fill = f)),
            "Watch usage climb toward the limit, or watch what's left drain like a fuel gauge."));
        p.Children.Add(Kit.Row("Card gauges", Kit.Segments(new[] { (GaugeStyle.Ring, "Rings"), (GaugeStyle.Bar, "Bars") }, S.Gauge, g => Set(s => s.Gauge = g))));

        p.Children.Add(Kit.Section("Meters"));
        CheckBox? claude = null, codex = null, split = null;
        claude = Kit.Switch("Claude", "Claude Code and the Claude app", S.ShowClaude, on =>
        {
            Set(s => { s.ShowClaude = on; if (!on && !s.ShowCodex) s.ShowCodex = true; });
            Sync();
        });
        codex = Kit.Switch("Codex", "Codex in the terminal, app or editor", S.ShowCodex, on =>
        {
            Set(s => { s.ShowCodex = on; if (!on && !s.ShowClaude) s.ShowClaude = true; });
            Sync();
        });
        split = Kit.Switch("Split apart", "Each app gets its own meter you can put anywhere. Drag one onto the other to join them again.", S.Split, on => Set(s => s.Split = on));
        void Sync()
        {
            claude!.IsChecked = S.ShowClaude;
            codex!.IsChecked = S.ShowCodex;
            split!.IsEnabled = S.ShowClaude && S.ShowCodex;
            split.IsChecked = S.Split;
        }
        split.IsEnabled = S.ShowClaude && S.ShowCodex;
        p.Children.Add(claude);
        p.Children.Add(codex);
        p.Children.Add(split);

        p.Children.Add(Kit.Section("Details"));
        p.Children.Add(Kit.Row("Main gauge shows", Kit.Segments(new[] { (FocusWindow.Tightest, "Closest to limit"), (FocusWindow.FiveHour, "5-hour"), (FocusWindow.Weekly, "Weekly") }, S.Focus, f => Set(s => s.Focus = f))));
        p.Children.Add(Kit.Row("Reset times", Kit.Segments(new[] { (ResetStyle.Countdown, "Countdown"), (ResetStyle.Clock, "Clock time") }, S.Resets, r => Set(s => s.Resets = r))));
        p.Children.Add(Kit.Switch("Smart forecast", "When a limit is climbing fast, show when it will run out at this pace.", S.Forecast, on => Set(s => s.Forecast = on)));

        p.Children.Add(Kit.Section("Size and see-through"));
        p.Children.Add(SliderRow("Size", 70, 160, S.Scale * 100, v => $"{v:0}%", v => Set(s => s.Scale = Math.Round(v) / 100)));
        p.Children.Add(SliderRow("Opacity when idle", 40, 100, S.Opacity * 100, v => $"{v:0}%", v => Set(s => s.Opacity = Math.Round(v) / 100)));
        return p;
    }

    Control ColorsPage()
    {
        var p = new StackPanel();
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8),
            Children = { Kit.Dot(Brand.ToColor(Palette.Parse(S.ClaudeColor, Palette.ClaudeDefault))), Kit.Text("Claude", "label") },
        });
        p.Children.Add(Kit.Swatches(S.ClaudeColor, hex => Set(s => s.ClaudeColor = hex)));
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 8),
            Children = { Kit.Dot(Brand.ToColor(Palette.Parse(S.CodexColor, Palette.CodexDefault))), Kit.Text("Codex", "label") },
        });
        p.Children.Add(Kit.Swatches(S.CodexColor, hex => Set(s => s.CodexColor = hex)));

        p.Children.Add(Kit.Section("Color style"));
        CheckBox? levels = null;
        p.Children.Add(Kit.Row("Gauges", Kit.Segments(new[] { (ColorMode.AppColors, "App colors"), (ColorMode.TrafficLight, "Traffic light") }, S.Colors, m =>
        {
            Set(s => s.Colors = m);
            if (levels is not null) levels.IsEnabled = m == ColorMode.AppColors;
        }), "Traffic light is green while there's room, amber near the limit, red at it."));
        levels = Kit.Switch("Turn amber and red near the limit", "App colors only. Thresholds are under Alerts.", S.LevelColors, on => Set(s => s.LevelColors = on));
        levels.IsEnabled = S.Colors == ColorMode.AppColors;
        p.Children.Add(levels);
        p.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Child = Kit.Link("Back to the original colors", () =>
            {
                Set(s => { s.ClaudeColor = Palette.ClaudeDefault; s.CodexColor = Palette.CodexDefault; s.Colors = ColorMode.AppColors; s.LevelColors = true; });
                Go(Page.Colors);
            }),
        });
        return p;
    }

    Control AlertsPage()
    {
        var p = new StackPanel();
        p.Children.Add(Kit.Text($"Limits turn amber, then red, as they get close. Ringside {(S.Fill == FillMode.Used ? "counts what you've used" : "counts what's left")}, as set under Look.", "hint"));
        p.Children.Add(Kit.Section("Thresholds"));
        p.Children.Add(SliderRow("Amber at", 30, 90, S.WarnAt, v => Display.Threshold(Math.Round(v), S.Fill), v => Set(s => s.WarnAt = Math.Round(v))));
        p.Children.Add(SliderRow("Red at", 35, 99, S.AlertAt, v => Display.Threshold(Math.Round(v), S.Fill), v => Set(s => s.AlertAt = Math.Max(Math.Round(v), s.WarnAt + 5))));

        p.Children.Add(Kit.Section("Notify me"));
        p.Children.Add(Kit.Switch("When a limit turns amber", null, S.NotifyAtWarn, on => Set(s => s.NotifyAtWarn = on)));
        p.Children.Add(Kit.Switch("When a limit turns red", null, S.NotifyAtAlert, on => Set(s => s.NotifyAtAlert = on)));
        p.Children.Add(Kit.Switch("When a limit resets", "After you've used at least half of it.", S.NotifyOnReset, on => Set(s => s.NotifyOnReset = on)));
        p.Children.Add(Kit.Switch("Before I run out", "A heads-up when, at your current pace, a limit runs out within the hour and before it resets.", S.NotifyForecast,
            on => Set(s => { s.NotifyForecast = on; if (on) s.Forecast = true; })));
        return p;
    }

    Control BehaviorPage()
    {
        var p = new StackPanel();
        p.Children.Add(Kit.Switch("Keep on top", "Stays above other windows.", S.Topmost, on => Set(s => s.Topmost = on)));
        if (Os.SupportsClickThrough)
            p.Children.Add(Kit.Switch("Click-through", $"Clicks pass straight through the meter. Turn it off again from the {Os.TrayName} icon.", S.ClickThrough, on => Set(s => s.ClickThrough = on)));
        if (Os.SupportsFullScreenHide)
            p.Children.Add(Kit.Switch("Hide over full-screen apps", "Steps aside for games, videos and presentations.", S.HideOverFullScreen, on => Set(s => s.HideOverFullScreen = on)));
        p.Children.Add(Kit.Switch(Os.LoginItemLabel, null, _host.Demo || AutoStart.Enabled, on => { if (!_host.Demo) AutoStart.Set(on); }));
        p.Children.Add(Kit.Switch("Check for updates", "Asks GitHub for new versions now and then. Nothing about your usage leaves " + Paths.ThisComputer + ".", S.CheckForUpdates, on => Set(s => s.CheckForUpdates = on)));

        p.Children.Add(Kit.Section("Layout"));
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                Kit.Ghost("Reset position", () => _host.ResetPosition()),
                Kit.Ghost("Show the welcome cards", () => { _host.ShowOnboarding(); }),
            },
        });
        return p;
    }

    Control ClaudeCodePage()
    {
        var p = new StackPanel();
        p.Children.Add(Kit.Text("Claude Code knows your exact limits after every reply. Connecting adds Ringside's small Claude Code plugin, which hands them over in the terminal, the Claude app's Code tab and VS Code, plus a status line link for the terminal. It reads no credentials and sends nothing anywhere. Your own status line keeps working.", "body"));
        p.Children.Add(new Border { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = Art.LiveSurfaces(S.Fill, surfaceWidth: 150) });
        var status = new TextBlock { Classes = { "hint" }, Margin = new Thickness(0, 12, 0, 8) };
        var button = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        void Refresh()
        {
            bool found = _host.Demo || ClaudeCodeHook.ClaudeCodeFound;
            bool connected = _host.Demo || ClaudeCodeHook.IsConnected;
            button.Content = connected ? "Disconnect" : "Connect";
            button.IsEnabled = _host.Demo || (found && (ClaudeCodeHook.BridgeAvailable || ClaudeCodeHook.PluginAvailable));
            status.Text = connected ? "Connected. Claude's limits come straight from Claude Code. New sessions pick it up."
                : Os.IsTranslocated ? "Move Ringside to your Applications folder first, then connect."
                : found ? "Not connected. Claude's numbers come from the Claude app's own log, which can be hours old."
                : "Claude Code was not found on " + Paths.ThisComputer + ".";
        }
        button.Click += (_, _) =>
        {
            if (_host.Demo) return;
            if (ClaudeCodeHook.IsConnected) ClaudeCodeHook.Disconnect();
            else ClaudeCodeHook.Connect();
            Refresh();
        };
        Refresh();
        p.Children.Add(status);
        p.Children.Add(button);
        return p;
    }

    Control AboutPage()
    {
        var p = new StackPanel();
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 4, 0, 12),
            Children =
            {
                Kit.Logo(54),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Ringside " + Updates.CurrentVersion, Classes = { "h2" } },
                        Kit.Text("Free and open source (MIT) by Knight AI+AV.", "hint", 2),
                    },
                },
            },
        });
        p.Children.Add(Kit.Text("Ringside reads the usage numbers Claude and Codex already keep on " + Paths.ThisComputer + ". It never reads your login, has no account and sends nothing about your usage anywhere. The only network traffic is to GitHub, to check for and download updates.", "body"));

        var status = new TextBlock { Classes = { "hint" }, Margin = new Thickness(0, 10, 0, 0) };
        var check = new Button { Classes = { "ghost" }, Content = "Check for updates" };
        check.Click += async (_, _) =>
        {
            if (_host.Updates is not { Supported: true } u) { status.Text = "Updates work in the installed version."; return; }
            check.IsEnabled = false;
            status.Text = "Checking...";
            await u.CheckAsync();
            status.Text = u.PendingVersion is { } v ? $"Version {v} is ready. It installs when Ringside restarts."
                : u.AvailableVersion is { } a ? $"Version {a} is available on GitHub."
                : "You have the latest version.";
            check.IsEnabled = true;
        };
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0),
            Children = { check, Kit.Ghost("View on GitHub", () => Os.OpenUrl(Updates.RepoUrl)), Kit.Ghost("★ Star it", () => Os.OpenUrl(Updates.RepoUrl + "/stargazers")) },
        });
        p.Children.Add(status);

        p.Children.Add(Kit.Section("Uninstall"));
        if (Os.IsWindows)
            p.Children.Add(Kit.Text("Uninstall from Windows Settings, Apps. Ringside removes its startup entry and its Claude Code link on the way out.", "hint"));
        else
        {
            var removed = new TextBlock { Classes = { "hint" }, Margin = new Thickness(0, 8, 0, 0) };
            p.Children.Add(Kit.Text(Os.IsMac
                ? "To uninstall, remove Ringside's changes here, then drag Ringside from Applications to the Trash."
                : "To uninstall, remove Ringside's changes here, then delete the Ringside AppImage.", "hint"));
            p.Children.Add(new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = Kit.Ghost("Remove Ringside's changes and quit", () =>
                {
                    if (_host.Demo) return;
                    Cleanup.Run(quitRunningCopy: false);
                    _host.Quit();
                }),
            });
        }
        return p;
    }

    Control SliderRow(string label, double min, double max, double value, Func<double, string> format, Action<double> changed)
    {
        var text = new TextBlock { Text = format(value), FontSize = 12, Foreground = Brand.Fg2Brush, MinWidth = 86, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 260, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            text.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        return Kit.Row(label, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, text } });
    }
}
