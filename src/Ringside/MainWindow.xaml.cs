using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ringside;

public partial class MainWindow : Window
{
    const double Gutter = 18, Gap = 16, SnapZone = 28;

    readonly ClaudeSource _claude = new();
    readonly CodexSource _codex = new();
    readonly Settings _settings;
    readonly bool _live;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromSeconds(3) };
    IntPtr _hwnd;
    bool _busy, _fullScreenHidden, _placed;
    int _guardTicks;
    double _pillA = -1, _pillB = -1;

    public event Action<Snapshot, Snapshot>? Updated;
    public event Action? MenuRequested;
    public event Action? SettingsRequested;

    public MainWindow(Settings settings, bool live = true)
    {
        _settings = settings;
        _live = live;
        InitializeComponent();
        ClaudeView.Configure("Claude", Theme.ClaudeColor, Theme.ClaudeAccent);
        CodexView.Configure("Codex", Theme.CodexColor, Theme.CodexAccent);
        ClaudeView.Animate = CodexView.Animate = live;
        HintText.Visibility = live && !settings.HintShown ? Visibility.Visible : Visibility.Collapsed;
        ApplySettings(anchor: false);
        _poll.Tick += async (_, _) => await RefreshAsync();
        _guard.Tick += (_, _) => Guard();
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // A tool window stays out of Alt+Tab; NOACTIVATE means clicking it never steals focus
        // from whatever you are typing in.
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        ApplyClickThrough();
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_live)
        {
            Root.Opacity = 1;
            Slide.Y = 0;
            return;
        }
        await RefreshAsync();
        UpdateLayout();
        await PlaceInitially();
        _placed = true;
        FadeIn();
        _poll.Start();
        _guard.Start();

        // The first read of each log is the only large one; hand that memory back.
        await Task.Delay(TimeSpan.FromSeconds(5));
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    public async Task RefreshAsync()
    {
        if (_busy || !_live) return;
        _busy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var (claude, codex) = await Task.Run(() => (_claude.Read(now), _codex.Read(now)));
            Show(claude, codex, now);
            Updated?.Invoke(claude, codex);
        }
        catch (Exception ex)
        {
            Log.Write("refresh failed: " + ex);
        }
        finally { _busy = false; }
    }

    /// <summary>Draws two snapshots. Also used to render README screenshots from sample data.</summary>
    public void Show(Snapshot claude, Snapshot codex, DateTimeOffset now)
    {
        ClaudeView.Show(claude, now);
        CodexView.Show(codex, now);
        Pill(PillRingA, PillPctA, claude, Theme.ClaudeAccent, ref _pillA);
        Pill(PillRingB, PillPctB, codex, Theme.CodexAccent, ref _pillB);
        PillPanel.ToolTip = $"Claude {PillPctA.Text} · Codex {PillPctB.Text}";
    }

    void Pill(Ring ring, Run pct, Snapshot s, Brush accent, ref double last)
    {
        var p = s.Primary;
        double v = Math.Clamp(p?.Percent ?? 0, 0, 100);
        pct.Text = p is null ? "-" : Fmt.Pct(p.Percent) + "%";
        pct.Foreground = v >= Theme.AlertAt ? new SolidColorBrush(Theme.RedColor) : (Brush)FindResource("Fg1");
        ring.Stroke = Theme.State(v, accent);
        if (Math.Abs(v - last) < 0.01) return;
        last = v;
        if (!_live) { ring.Value = v; return; }
        ring.BeginAnimation(Ring.ValueProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // ---- settings ---------------------------------------------------------------------

    /// <summary>Applies every visual setting. With anchor set, the docked edges stay put.</summary>
    public void ApplySettings(bool anchor = true)
    {
        Reflow(anchor, () =>
        {
            bool compact = _settings.Compact;
            FullPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            PillPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            var radius = new CornerRadius(compact ? 19 : 16);
            Card.CornerRadius = radius;
            CardShadow.CornerRadius = radius;
            Card.Padding = compact ? new Thickness(11, 8, 14, 8) : new Thickness(16, 14, 16, 14);

            ClaudeView.Visibility = _settings.ShowClaude ? Visibility.Visible : Visibility.Collapsed;
            CodexView.Visibility = _settings.ShowCodex ? Visibility.Visible : Visibility.Collapsed;
            Divider.Visibility = _settings.ShowClaude && _settings.ShowCodex ? Visibility.Visible : Visibility.Collapsed;
            PillA.Visibility = ClaudeView.Visibility;
            PillB.Visibility = CodexView.Visibility;
            PillB.Margin = new Thickness(_settings.ShowClaude ? 14 : 0, 0, 0, 0);

            Root.LayoutTransform = Math.Abs(_settings.Scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(_settings.Scale, _settings.Scale);
        });

        Topmost = _settings.Topmost;
        PinButton.Content = _settings.Topmost ? "" : "";
        PinButton.ToolTip = _settings.Topmost ? "Stays on top (click to release)" : "Keep on top";
        if (Controls.Opacity == 0 && _placed) Fade(Root, _settings.Opacity, 200);
        ApplyClickThrough();
    }

    void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
            _settings.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT);
    }

    void Reflow(bool anchor, Action change)
    {
        if (!anchor || !_placed || _hwnd == IntPtr.Zero)
        {
            change();
            return;
        }
        // Keep whichever edges it is docked to where they are, so it grows inward.
        var wa = WorkArea();
        bool right = Left + ActualWidth / 2 > wa.Left + wa.Width / 2;
        bool bottom = Top + ActualHeight / 2 > wa.Top + wa.Height / 2;
        double rightEdge = Left + ActualWidth, bottomEdge = Top + ActualHeight;
        change();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (right) Left = rightEdge - ActualWidth;
            if (bottom) Top = bottomEdge - ActualHeight;
            SavePosition();
        });
    }

    // ---- placement --------------------------------------------------------------------

    Rect WorkArea()
    {
        var px = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(toDip.Transform(new Point(px.Left, px.Top)), toDip.Transform(new Point(px.Right, px.Bottom)));
    }

    async Task PlaceInitially()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_settings.X is int x && _settings.Y is int y &&
            Array.Exists(screens, s => s.WorkingArea.Contains(x + 60, y + 40)))
        {
            MoveTo(x, y);
            return;
        }
        // First run: bottom-right of the main display, just above the clock, where it covers no
        // title-bar buttons. Hop onto that display first so Windows applies its scaling, then measure.
        var main = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        MoveTo(main.Left + 200, main.Top + 200);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        UpdateLayout();
        var wa = WorkArea();
        Left = wa.Right - Gap - (ActualWidth - Gutter);
        Top = wa.Bottom - Gap - (ActualHeight - Gutter);
        SavePosition();
    }

    public void ResetPosition()
    {
        _settings.X = _settings.Y = null;
        _ = PlaceInitially();
    }

    void MoveTo(int x, int y) =>
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);

    void Snap()
    {
        var wa = WorkArea();
        double cardLeft = Left + Gutter, cardTop = Top + Gutter;
        double cardRight = Left + ActualWidth - Gutter, cardBottom = Top + ActualHeight - Gutter;
        if (Math.Abs(cardLeft - wa.Left) < SnapZone) Left = wa.Left + Gap - Gutter;
        else if (Math.Abs(wa.Right - cardRight) < SnapZone) Left = wa.Right - Gap - (ActualWidth - Gutter);
        if (Math.Abs(cardTop - wa.Top) < SnapZone) Top = wa.Top + Gap - Gutter;
        else if (Math.Abs(wa.Bottom - cardBottom) < SnapZone) Top = wa.Bottom - Gap - (ActualHeight - Gutter);
    }

    void SavePosition()
    {
        if (_hwnd == IntPtr.Zero || !_live || !Native.GetWindowRect(_hwnd, out var r)) return;
        _settings.X = r.Left;
        _settings.Y = r.Top;
        _settings.Save();
    }

    // ---- interaction ------------------------------------------------------------------

    void Interacted()
    {
        if (_settings.HintShown) return;
        _settings.HintShown = true;
        _settings.Save();
        Reflow(true, () => HintText.Visibility = Visibility.Collapsed);
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Interacted();
            ToggleCompact();
            e.Handled = true;
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        Snap();
        SavePosition();
        Interacted();
    }

    void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        Interacted();
        MenuRequested?.Invoke();
        e.Handled = true;
    }

    void OnEnter(object sender, MouseEventArgs e)
    {
        Fade(Root, 1, 160);
        if (_settings.Compact) return;
        Controls.IsHitTestVisible = true;
        Fade(Controls, 1, 160);
    }

    void OnLeave(object sender, MouseEventArgs e)
    {
        Fade(Root, _settings.Opacity, 260);
        Controls.IsHitTestVisible = false;
        Fade(Controls, 0, 200);
    }

    void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    void OnCompactClick(object sender, RoutedEventArgs e) => ToggleCompact();
    void OnPinClick(object sender, RoutedEventArgs e)
    {
        _settings.Topmost = !_settings.Topmost;
        _settings.Save();
        ApplySettings();
    }
    void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    public void ToggleCompact()
    {
        _settings.Compact = !_settings.Compact;
        _settings.Save();
        Controls.IsHitTestVisible = false;
        Controls.BeginAnimation(OpacityProperty, null);
        Controls.Opacity = 0;
        ApplySettings();
    }

    void Guard()
    {
        // Step aside for full-screen video, games and presentations, like Windows notifications do.
        bool fullScreen = _settings.HideOverFullScreen
            && Native.SHQueryUserNotificationState(out int state) == 0
            && state is 2 or 3 or 4;
        if (fullScreen != _fullScreenHidden)
        {
            _fullScreenHidden = fullScreen;
            Root.Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
        }

        // Other always-on-top windows can bury it; reclaim the top every 30 s without taking focus.
        if (++_guardTicks % 10 == 0 && _settings.Topmost && IsVisible && _hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void FadeIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, _settings.Opacity, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
    }

    static void Fade(UIElement el, double to, int ms) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
}
