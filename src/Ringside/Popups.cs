using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>Base for Ringside's own small borderless windows: a dark card with a soft shadow.</summary>
public abstract class CardWindow : Window
{
    protected readonly Border Card;

    protected CardWindow(double width, double radius = 14)
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Icon = App.WindowIcon;
        if (Os.IsLinux) RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Card = new Border
        {
            Width = width,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            Background = Kit.Res("CardFill"),
            BorderBrush = Kit.Res("CardEdge"),
            BoxShadow = BoxShadows.Parse("0 8 28 0 #8C000000"),
            Margin = new Thickness(18),
        };
        Content = Card;
    }

    /// <summary>Keeps a window of this size inside the work area of the screen at a point.</summary>
    protected PixelPoint Clamp(PixelPoint wanted, PixelSize size)
    {
        var screen = Screens.ScreenFromPoint(wanted) ?? Screens.Primary;
        if (screen is null) return wanted;
        var wa = screen.WorkingArea;
        int x = Math.Clamp(wanted.X, wa.X, Math.Max(wa.X, wa.Right - size.Width));
        int y = Math.Clamp(wanted.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - size.Height));
        return new PixelPoint(x, y);
    }

    protected PixelSize Measured(double scaling)
    {
        Card.Measure(Size.Infinity);
        var d = Card.DesiredSize;
        return new PixelSize((int)((d.Width + 36) * scaling), (int)((d.Height + 36) * scaling));
    }
}

/// <summary>
/// The right-click panel: switch views, used or left, split or combine, and the usual
/// commands, one click each. It closes as soon as you click anywhere else.
/// </summary>
public sealed class QuickPanel : CardWindow
{
    public sealed record Actions(
        Action<ViewKind> View, Action<FillMode> Fill, Action ToggleSplit, Action ToggleTopmost, Action ToggleClickThrough,
        Action Settings, Action Refresh, Action Hide, Action? Restart, string? RestartLabel, Action Quit);

    bool _closing;

    public QuickPanel(Settings s, Actions a) : base(250)
    {
        ShowInTaskbar = false;
        Topmost = true;
        Card.Padding = new Thickness(12, 12, 12, 8);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(4, 0, 0, 10),
            Children = { Kit.Logo(18), new TextBlock { Text = "Ringside", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush, VerticalAlignment = VerticalAlignment.Center } },
        });

        stack.Children.Add(Caption("View"));
        stack.Children.Add(Wide(Kit.Segments(new[] { (ViewKind.Card, "Card"), (ViewKind.Pill, "Pill"), (ViewKind.Orb, "Orb") }, s.View, v => { a.View(v); }, fill: true)));
        stack.Children.Add(Caption("Show limits as"));
        stack.Children.Add(Wide(Kit.Segments(new[] { (FillMode.Used, "Used ↑"), (FillMode.Left, "Left ↓") }, s.Fill, f => { a.Fill(f); }, fill: true)));
        stack.Children.Add(Separator());

        if (s.ShowClaude && s.ShowCodex)
            stack.Children.Add(Item(s.Split ? "combine" : "split", s.Split ? "Combine into one" : "Split apart", null, a.ToggleSplit));
        stack.Children.Add(Item("pin", "Keep on top", s.Topmost, a.ToggleTopmost));
        if (Os.SupportsClickThrough)
            stack.Children.Add(Item("through", "Click-through", s.ClickThrough, a.ToggleClickThrough, hint: "Turn off from the " + Os.TrayName + " icon"));
        stack.Children.Add(Separator());
        stack.Children.Add(Item("gear", "Settings...", null, a.Settings));
        stack.Children.Add(Item("refresh", "Refresh now", null, a.Refresh));
        if (a.Restart is not null) stack.Children.Add(Item("spark", a.RestartLabel ?? "Restart to update", null, a.Restart));
        stack.Children.Add(Item("hide", "Hide Ringside", null, a.Hide));
        stack.Children.Add(Item("power", "Quit Ringside", null, a.Quit));
        Card.Child = stack;

        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
    }

    void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.UIThread.Post(Close);
    }

    public void ShowAt(PixelPoint screenPoint)
    {
        double sc = (Screens.ScreenFromPoint(screenPoint) ?? Screens.Primary)?.Scaling ?? 1;
        var size = Measured(sc);
        int g = (int)(18 * sc);
        Position = Clamp(new PixelPoint(screenPoint.X - g + 4, screenPoint.Y - g + 4), size);
        Show();
        Activate();
    }

    static Control Wide(Control c) { c.HorizontalAlignment = HorizontalAlignment.Stretch; c.Margin = new Thickness(0, 0, 0, 6); return c; }

    static TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = FontWeight.Medium, Foreground = Brand.Fg3Brush, Margin = new Thickness(4, 2, 0, 5),
    };

    static Border Separator() => new() { Height = 1, Background = Kit.Res("Hairline"), Margin = new Thickness(2, 4) };

    Button Item(string icon, string text, bool? check, Action click, string? hint = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto") };
        grid.Children.Add(new PathIcon { Data = Icons.Get(icon), Width = 12, Height = 12, Foreground = Brand.Fg2Brush, HorizontalAlignment = HorizontalAlignment.Left });
        var label = new TextBlock { Text = text, FontSize = 12.5, Foreground = Brand.Fg1Brush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (check == true)
        {
            var mark = new PathIcon { Data = Icons.Get("check"), Width = 12, Height = 12, Foreground = new SolidColorBrush(Brand.Accent) };
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);
        }
        var b = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, Padding = new Thickness(8, 7), CornerRadius = new CornerRadius(7), Cursor = new Cursor(StandardCursorType.Hand),
        };
        Avalonia.Automation.AutomationProperties.SetName(b, text);
        if (hint is not null) ToolTip.SetTip(b, hint);
        b.Click += (_, _) => { SafeClose(); click(); };
        return b;
    }
}

/// <summary>Ringside's own notification card: the same look on Windows, macOS and Linux.</summary>
public sealed class Toast : CardWindow
{
    static readonly List<Toast> Open = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(7) };

    public Toast(string title, string body, Color accent, Action? clicked) : base(330)
    {
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Card.Padding = new Thickness(14, 12, 10, 13);
        var close = new Button { Classes = { "icon" }, Content = Brand.Icon("close", 9), VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, e) => { e.Handled = true; Close(); };
        var text = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = body, FontSize = 12, LineHeight = 17, Foreground = Brand.Fg2Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), IsVisible = body.Length > 0 },
            },
        };
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent), Margin = new Thickness(0, 2, 12, 2) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(bar);
        grid.Children.Add(text);
        grid.Children.Add(close);
        Card.Child = grid;
        Card.Cursor = new Cursor(StandardCursorType.Hand);
        PointerReleased += (_, _) => { clicked?.Invoke(); Close(); };
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => _timer.Start();
        _timer.Tick += (_, _) => Close();
        Opened += (_, _) => { if (Os.IsWindows || Os.IsMac) Os.PrepareToast(this); };
        Closed += (_, _) => { _timer.Stop(); Open.Remove(this); Restack(); };
    }

    /// <summary>Bottom-right on Windows and Linux (above the clock), top-right on macOS.</summary>
    public void Pop()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        Open.Add(this);
        Opacity = 0;
        Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
        };
        Restack();
        Show();
        Opacity = 1;
        _timer.Start();
    }

    static void Restack()
    {
        int offset = 0;
        foreach (var t in Open.ToList())
        {
            var screen = t.Screens.Primary;
            if (screen is null) continue;
            double sc = screen.Scaling;
            var size = t.Measured(sc);
            var wa = screen.WorkingArea;
            int gap = (int)(4 * sc);
            int x = wa.Right - size.Width + gap;
            int y = Os.IsMac ? wa.Y - gap + offset : wa.Bottom - size.Height + gap - offset;
            t.Position = new PixelPoint(x, y);
            offset += size.Height - (int)(30 * sc);
        }
    }
}
