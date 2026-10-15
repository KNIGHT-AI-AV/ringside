using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>
/// A floating meter on the desktop. Combined, one window shows both apps; split apart,
/// each app gets its own window, and dropping one onto the other joins them again.
/// </summary>
public sealed class WidgetWindow : Window
{
    const double Gap = 16, SnapZone = 28, JoinZone = 24;

    readonly Settings _s;
    readonly WidgetView _view;
    public readonly string Key; // "Both", "Claude" or "Codex"
    public readonly IReadOnlyList<string> Providers;

    bool _placed, _anchoring, _hovered, _dragging, _moved;
    PixelPoint _dragStartScreen, _dragStartPos;
    Size _lastSize;
    readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromSeconds(3) };
    int _guardTicks;
    bool _fullScreenHidden;

    public event Action<WidgetWindow, PixelPoint>? MenuRequested;
    public event Action? SettingsRequested;
    public event Action? CycleViewRequested;
    public event Action? SplitToggleRequested;
    public event Action? PinToggleRequested;
    public event Action<WidgetWindow>? HideRequested;
    public event Action<WidgetWindow>? DragEnded;
    public event Action? Interacted;

    public WidgetWindow(Settings settings, string key, IReadOnlyList<string> providers, bool showHint)
    {
        _s = settings;
        Key = key;
        Providers = providers;
        Title = "Ringside";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (Os.IsLinux) RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Icon = App.WindowIcon;

        _view = new WidgetView { ShowHint = showHint };
        _view.SettingsRequested += () => SettingsRequested?.Invoke();
        _view.CycleViewRequested += () => CycleViewRequested?.Invoke();
        _view.SplitToggleRequested += () => SplitToggleRequested?.Invoke();
        _view.PinToggleRequested += () => PinToggleRequested?.Invoke();
        _view.HideRequested += () => HideRequested?.Invoke(this);
        _view.Configure(settings, providers);
        Content = _view;
        Opacity = 0;

        _guard.Tick += (_, _) => Guard();
        Opened += (_, _) =>
        {
            Os.PrepareWidget(this, _s);
            _guard.Start();
        };
        Closed += (_, _) => _guard.Stop();
    }

    public void Show(Reading r) => _view.Show(r);

    /// <summary>Re-applies every setting. The docked edges stay where they are.</summary>
    public void Apply(bool hint)
    {
        _view.ShowHint = hint;
        _view.Configure(_s, Providers);
        Os.ApplyTopmost(this, _s.Topmost);
        Os.ApplyClickThrough(this, _s.ClickThrough);
        if (!_hovered) Opacity = _s.Opacity;
    }

    // ---- placement --------------------------------------------------------------------

    (int? X, int? Y) Saved => Key switch
    {
        "Claude" => (_s.ClaudeX, _s.ClaudeY),
        "Codex" => (_s.CodexX, _s.CodexY),
        _ => (_s.X, _s.Y),
    };

    public void SavePosition()
    {
        var p = Position;
        switch (Key)
        {
            case "Claude": _s.ClaudeX = p.X; _s.ClaudeY = p.Y; break;
            case "Codex": _s.CodexX = p.X; _s.CodexY = p.Y; break;
            default: _s.X = p.X; _s.Y = p.Y; break;
        }
        _s.Save();
    }

    /// <summary>Puts the window where it was, or at the first-run spot, then fades it in.</summary>
    public void Place(PixelPoint? requested = null)
    {
        var (x, y) = Saved;
        var screens = Screens.All;
        var size = MeasuredPixels(Screens.Primary?.Scaling ?? 1);
        PixelPoint pos;
        if (requested is { } r) pos = r;
        else if (x is int sx && y is int sy && screens.Any(s => s.WorkingArea.Contains(new PixelPoint(sx + 60, sy + 40))))
            pos = new PixelPoint(sx, sy);
        else pos = FirstRunSpot(size);
        pos = KeepOnScreen(pos, size);
        _anchoring = false;
        Position = pos;
        _placed = true;
        if (!IsVisible) Show();
        FadeTo(_s.Opacity);
        SavePosition();
        // The first layout after showing is not a resize the user caused; start anchoring after it.
        Dispatcher.UIThread.Post(() => { _lastSize = ClientSize; _anchoring = true; }, DispatcherPriority.Background);
    }

    /// <summary>The card's width in screen pixels, measured before the window has laid out.</summary>
    public int MeasuredCardWidth()
    {
        double sc = (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.Scaling ?? 1;
        return MeasuredPixels(sc).Width - 2 * (int)(WidgetView.Gutter * sc);
    }

    /// <summary>Pulls a spot back inside the work area, e.g. when a bigger meter takes a smaller one's place.</summary>
    PixelPoint KeepOnScreen(PixelPoint pos, PixelSize size)
    {
        var screen = Screens.ScreenFromPoint(new PixelPoint(pos.X + size.Width / 2, pos.Y + size.Height / 2))
                     ?? Screens.ScreenFromPoint(pos) ?? Screens.Primary;
        if (screen is null) return pos;
        var wa = screen.WorkingArea;
        double sc = screen.Scaling;
        int gutter = (int)(WidgetView.Gutter * sc), gap = (int)(Gap * sc);
        int minX = wa.X + gap - gutter, maxX = wa.Right - gap - (size.Width - gutter);
        int minY = wa.Y + gap - gutter, maxY = wa.Bottom - gap - (size.Height - gutter);
        return new PixelPoint(Math.Clamp(pos.X, minX, Math.Max(minX, maxX)), Math.Clamp(pos.Y, minY, Math.Max(minY, maxY)));
    }

    PixelSize MeasuredPixels(double scaling)
    {
        _view.Measure(Size.Infinity);
        var d = _view.DesiredSize;
        return new PixelSize((int)Math.Ceiling(d.Width * scaling), (int)Math.Ceiling(d.Height * scaling));
    }

    // First run: Windows and Linux put it bottom-right, above the clock, where it covers no
    // title-bar buttons; macOS puts it top-right, under the menu bar, clear of the Dock.
    PixelPoint FirstRunSpot(PixelSize size)
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return new PixelPoint(100, 100);
        var wa = screen.WorkingArea;
        double sc = screen.Scaling;
        int gutter = (int)(WidgetView.Gutter * sc), gap = (int)(Gap * sc);
        int x = wa.Right - gap - (size.Width - gutter);
        int y = Os.IsMac ? wa.Y + gap - gutter : wa.Bottom - gap - (size.Height - gutter);
        return new PixelPoint(x, y);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ClientSizeProperty || !_placed || !_anchoring || _dragging) return;
        // Grow inward from whichever edges it is docked to, so switching views never pushes it off screen.
        var now = ClientSize;
        var old = _lastSize;
        _lastSize = now;
        if (old.Width <= 0 || Screens.ScreenFromWindow(this) is not { } screen) return;
        double sc = RenderScaling;
        var wa = screen.WorkingArea;
        int dx = (int)Math.Round((now.Width - old.Width) * sc), dy = (int)Math.Round((now.Height - old.Height) * sc);
        int oldW = (int)(old.Width * sc), oldH = (int)(old.Height * sc);
        bool right = Position.X + oldW / 2 > wa.X + wa.Width / 2;
        bool bottom = Position.Y + oldH / 2 > wa.Y + wa.Height / 2;
        if ((right && dx != 0) || (bottom && dy != 0))
        {
            Position = new PixelPoint(Position.X - (right ? dx : 0), Position.Y - (bottom ? dy : 0));
            SavePosition();
        }
    }

    void Snap()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var wa = screen.WorkingArea;
        double sc = RenderScaling;
        int gutter = (int)(WidgetView.Gutter * sc), gap = (int)(Gap * sc), zone = (int)(SnapZone * sc);
        int w = (int)(ClientSize.Width * sc), h = (int)(ClientSize.Height * sc);
        int left = Position.X + gutter, top = Position.Y + gutter, right = Position.X + w - gutter, bottom = Position.Y + h - gutter;
        int x = Position.X, y = Position.Y;
        if (Math.Abs(left - wa.X) < zone) x = wa.X + gap - gutter;
        else if (Math.Abs(wa.Right - right) < zone) x = wa.Right - gap - (w - gutter);
        if (Math.Abs(top - wa.Y) < zone) y = wa.Y + gap - gutter;
        else if (Math.Abs(wa.Bottom - bottom) < zone) y = wa.Bottom - gap - (h - gutter);
        Position = new PixelPoint(x, y);
    }

    /// <summary>The visible card in screen pixels, without the shadow gutter.</summary>
    public PixelRect CardRect
    {
        get
        {
            double sc = RenderScaling;
            int g = (int)(WidgetView.Gutter * sc);
            return new PixelRect(Position.X + g, Position.Y + g,
                Math.Max(1, (int)(ClientSize.Width * sc) - 2 * g), Math.Max(1, (int)(ClientSize.Height * sc) - 2 * g));
        }
    }

    /// <summary>Whether this window was dropped on top of (or right against) the other one.</summary>
    public bool Touches(WidgetWindow other)
    {
        int zone = (int)(JoinZone * RenderScaling);
        var a = CardRect;
        var b = other.CardRect;
        return a.X - zone < b.X + b.Width && b.X < a.X + a.Width + zone &&
               a.Y - zone < b.Y + b.Height && b.Y < a.Y + a.Height + zone;
    }

    // ---- pointer ----------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            Interacted?.Invoke();
            CycleViewRequested?.Invoke();
            e.Handled = true;
            return;
        }
        _dragging = true;
        _moved = false;
        _dragStartScreen = this.PointToScreen(point.Position);
        _dragStartPos = Position;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var now = this.PointToScreen(e.GetPosition(this));
        int dx = now.X - _dragStartScreen.X, dy = now.Y - _dragStartScreen.Y;
        if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
        _moved = true;
        Position = new PixelPoint(_dragStartPos.X + dx, _dragStartPos.Y + dy);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            Interacted?.Invoke();
            MenuRequested?.Invoke(this, this.PointToScreen(e.GetPosition(this)));
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (!_moved) return;
        Snap();
        _lastSize = ClientSize;
        SavePosition();
        Interacted?.Invoke();
        DragEnded?.Invoke(this);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        FadeTo(1);
        _view.ShowControls(true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        FadeTo(_s.Opacity);
        _view.ShowControls(false);
    }

    void FadeTo(double to)
    {
        Transitions ??= new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
        };
        Opacity = to;
    }

    void Guard()
    {
        // Step aside for full-screen video, games and presentations, like notifications do.
        bool fullScreen = _s.HideOverFullScreen && Os.FullScreenAppActive();
        if (fullScreen != _fullScreenHidden)
        {
            _fullScreenHidden = fullScreen;
            _view.IsVisible = !fullScreen;
        }
        if (++_guardTicks % 10 == 0 && _s.Topmost && IsVisible) Os.Reassert(this);
    }
}
