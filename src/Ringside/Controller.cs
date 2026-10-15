using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>
/// Runs Ringside: reads the logs every 15 seconds, feeds the meters, the tray icon and the
/// alerts, and owns every window (meters, onboarding, settings, quick panel).
/// </summary>
public sealed class Controller : IHost
{
    readonly IClassicDesktopStyleApplicationLifetime _life;
    Settings _s = new();
    readonly ClaudeSource _claude = new();
    readonly CodexSource _codex = new();
    readonly UsageHistory _history = new();
    readonly Alerts _alerts = new();
    Updates _updates = null!;
    readonly List<WidgetWindow> _widgets = new();
    TrayIcon? _tray;
    Reading? _last;
    bool _busy, _hidden, _started, _quitting;
    (bool Split, bool Claude, bool Codex) _layout;
    SettingsWindow? _settingsWin;
    OnboardingWindow? _onboarding;
    QuickPanel? _panel;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(90) };
    string _trayKey = "";

    public Controller(IClassicDesktopStyleApplicationLifetime life) => _life = life;

    public Settings Settings => _s;
    public Reading? Last => _last;
    public Updates? Updates => _updates;
    public bool Demo => false;

    public void Start()
    {
        _s = Settings.Load();
        if (Os.IsWindows)
        {
            ClaudeCodeHook.BridgeExe = Path.Combine(AppContext.BaseDirectory, "ringside-statusline.exe");
            ClaudeCodeHook.BridgeIsApp = false;
        }
        else
        {
            // Never hand Claude Code a path macOS made up for an app run straight from Downloads.
            ClaudeCodeHook.BridgeExe = Os.IsTranslocated ? null : Os.AppExecutable;
            ClaudeCodeHook.BridgeIsApp = true;
        }
        ClaudeCodeHook.Repair();

        SingleInstance.Listen(msg => Dispatcher.UIThread.Post(() =>
        {
            if (msg == "quit") Quit();
            else BringForward();
        }));

        _updates = new Updates();
        _updates.Ready += v =>
        {
            RebuildTray();
            Notify("Ringside update ready",
                $"Version {v} installs the next time Ringside starts. To install it now, choose Restart to update in the {Os.TrayName} menu.",
                Brand.Accent);
        };
        _updates.Available += v =>
        {
            RebuildTray();
            Notify($"Ringside {v} is available", "Click to open the download page.", Brand.Accent, () => Os.OpenUrl(Updates.ReleasesUrl));
        };

        _poll.Tick += async (_, _) => await RefreshAsync();
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (_s.CheckForUpdates) await _updates.CheckAsync();
        };

        BuildTray();
        if (_s.OnboardingDone) StartMeters();
        else ShowOnboarding();
    }

    // ---- meters -----------------------------------------------------------------------

    async void StartMeters()
    {
        if (_started) return;
        _started = true;
        BuildWidgets();
        await RefreshAsync();
        _poll.Start();
        _updateTimer.Start();

        // The first read of each log is the only large one; hand that memory back.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Os.TrimWorkingSet();
    }

    void BuildWidgets(PixelPoint? at = null)
    {
        foreach (var w in _widgets.ToList()) { w.Close(); }
        _widgets.Clear();
        _layout = (_s.Split, _s.ShowClaude, _s.ShowCodex);
        bool hint = !_s.HintShown;
        if (_s.Split && _s.ShowClaude && _s.ShowCodex)
        {
            var claude = NewWidget("Claude", new[] { "Claude" }, hint);
            var codex = NewWidget("Codex", new[] { "Codex" }, false);
            claude.Place(_s.ClaudeX is null ? at : null);
            if (_s.CodexX is null)
            {
                // First split: Codex steps out beside Claude, toward the middle of the screen.
                int width = claude.MeasuredCardWidth();
                var screen = claude.Screens.ScreenFromPoint(claude.Position) ?? claude.Screens.Primary;
                bool right = screen is not null && claude.Position.X > screen.WorkingArea.X + screen.WorkingArea.Width / 2;
                int gap = (int)(14 * (screen?.Scaling ?? 1));
                var spot = new PixelPoint(right ? claude.Position.X - width - gap : claude.Position.X + width + gap, claude.Position.Y);
                codex.Place(spot);
            }
            else codex.Place();
        }
        else
        {
            var providers = new[] { "Claude", "Codex" }.Where(_s.Shows).ToArray();
            NewWidget("Both", providers, hint).Place(at);
        }
        if (_hidden) foreach (var w in _widgets) w.Hide();
        if (_last is not null) foreach (var w in _widgets) w.Show(_last);
    }

    WidgetWindow NewWidget(string key, string[] providers, bool hint)
    {
        var w = new WidgetWindow(_s, key, providers, hint);
        w.MenuRequested += (win, at) => ShowPanel(at);
        w.SettingsRequested += OpenSettings;
        w.CycleViewRequested += CycleView;
        w.SplitToggleRequested += () => ToggleSplit();
        w.PinToggleRequested += () => Change(s => s.Topmost = !s.Topmost);
        w.HideRequested += _ => SetHidden(true);
        w.DragEnded += OnDragEnded;
        w.Interacted += () =>
        {
            if (_s.HintShown) return;
            _s.HintShown = true;
            _s.Save();
            foreach (var x in _widgets) x.Apply(false);
        };
        _widgets.Add(w);
        return w;
    }

    void OnDragEnded(WidgetWindow moved)
    {
        // Drop one split meter onto the other and they become one again.
        if (!_s.Split || _widgets.Count != 2) return;
        var other = _widgets.First(w => w != moved);
        if (!moved.Touches(other)) return;
        ToggleSplit(other.Position);
    }

    public async Task RefreshAsync()
    {
        if (_busy || _quitting) return;
        _busy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var (claude, codex) = await Task.Run(() => (_claude.Read(now), _codex.Read(now)));
            _history.Record(claude, now);
            _history.Record(codex, now);
            var runsOut = new Dictionary<string, DateTimeOffset?>();
            foreach (var snap in new[] { claude, codex })
                runsOut[snap.Provider] = snap.Pick(_s.Focus) is { } w ? _history.RunsOutAt(snap.Provider, w, now) : null;
            _last = new Reading(claude, codex, now, runsOut);

            foreach (var w in _widgets) w.Show(_last);
            _settingsWin?.ShowReading(_last);
            UpdateTrayIcon();

            foreach (var snap in new[] { claude, codex })
            {
                if (!_s.Shows(snap.Provider)) continue;
                foreach (var (title, body) in _alerts.Evaluate(snap, _s, _history, now))
                    Notify(title, body, Brand.ToColor(Palette.Solid(snap.Provider, snap.Pick(_s.Focus)?.Percent ?? 0, _s)), BringForward);
            }
        }
        catch (Exception ex) { Log.Write("refresh failed: " + ex); }
        finally { _busy = false; }
    }

    void Notify(string title, string body, Avalonia.Media.Color accent, Action? clicked = null)
    {
        try { new Toast(title, body, accent, clicked).Pop(); }
        catch (Exception e) { Log.Write("notification failed: " + e.Message); }
    }

    // ---- settings ---------------------------------------------------------------------

    /// <summary>Changes settings, saves them and redraws everything that depends on them.</summary>
    public void Change(Action<Settings> edit)
    {
        edit(_s);
        _s.Normalize();
        _s.Save();
        ApplyAll();
    }

    void ApplyAll()
    {
        if (_started)
        {
            var layout = (_s.Split, _s.ShowClaude, _s.ShowCodex);
            if (layout != _layout)
            {
                var at = _widgets.FirstOrDefault()?.Position;
                BuildWidgets(_s.Split ? null : at);
            }
            else foreach (var w in _widgets) w.Apply(!_s.HintShown);
            if (_last is not null)
            {
                // Recompute forecasts if the focused window changed.
                var runsOut = new Dictionary<string, DateTimeOffset?>();
                foreach (var snap in new[] { _last.Claude, _last.Codex })
                    runsOut[snap.Provider] = snap.Pick(_s.Focus) is { } w ? _history.RunsOutAt(snap.Provider, w, _last.Now) : null;
                _last = _last with { RunsOut = runsOut };
                foreach (var w in _widgets) w.Show(_last);
            }
        }
        _settingsWin?.SettingsChanged();
        RebuildTray();
        UpdateTrayIcon();
    }

    void CycleView() => Change(s => s.View = s.View switch
    {
        ViewKind.Card => ViewKind.Pill,
        ViewKind.Pill => ViewKind.Orb,
        _ => ViewKind.Card,
    });

    void ToggleSplit(PixelPoint? combineAt = null)
    {
        if (!(_s.ShowClaude && _s.ShowCodex)) return;
        if (_s.Split)
        {
            var at = combineAt ?? _widgets.FirstOrDefault(w => w.Key == "Claude")?.Position;
            _s.Split = false;
            _s.Save();
            BuildWidgets(at);
        }
        else
        {
            var at = _widgets.FirstOrDefault()?.Position;
            _s.Split = true;
            // Split from wherever the combined meter is now.
            _s.ClaudeX = at?.X;
            _s.ClaudeY = at?.Y;
            _s.CodexX = _s.CodexY = null;
            _s.Save();
            BuildWidgets();
        }
        _layout = (_s.Split, _s.ShowClaude, _s.ShowCodex);
        ApplyAll();
    }

    void SetHidden(bool hidden)
    {
        _hidden = hidden;
        foreach (var w in _widgets)
        {
            if (hidden) w.Hide();
            else w.Show();
        }
        RebuildTray();
    }

    void BringForward()
    {
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        if (!_started) return;
        SetHidden(false);
        _ = RefreshAsync();
    }

    public void OpenSettings()
    {
        if (_settingsWin is not null) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(this);
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Show();
        _settingsWin.Activate();
    }

    public void ShowOnboarding()
    {
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        _settingsWin?.Close();
        _onboarding = new OnboardingWindow(this);
        _onboarding.Closed += (_, _) =>
        {
            _onboarding = null;
            // Closing the welcome cards without finishing still starts the meter, changing nothing else.
            if (!_s.OnboardingDone) Change(s => s.OnboardingDone = true);
            if (_started) ApplyAll();
            else StartMeters();
        };
        _onboarding.Show();
        _onboarding.Activate();
    }

    public void ResetPosition()
    {
        _s.X = _s.Y = _s.ClaudeX = _s.ClaudeY = _s.CodexX = _s.CodexY = null;
        _s.Save();
        if (_started) BuildWidgets();
    }

    void ShowPanel(PixelPoint at)
    {
        _panel?.Close();
        _panel = new QuickPanel(_s, new QuickPanel.Actions(
            View: v => Change(s => s.View = v),
            Fill: f => Change(s => s.Fill = f),
            ToggleSplit: () => ToggleSplit(),
            ToggleTopmost: () => Change(s => s.Topmost = !s.Topmost),
            ToggleClickThrough: () => Change(s => s.ClickThrough = !s.ClickThrough),
            Settings: OpenSettings,
            Refresh: () => _ = RefreshAsync(),
            Hide: () => SetHidden(true),
            Restart: _updates.PendingVersion is not null || _updates.AvailableVersion is not null ? _updates.RestartNow : null,
            RestartLabel: _updates.PendingVersion is { } p ? $"Restart to update ({p})" : _updates.AvailableVersion is { } a ? $"Get version {a}" : null,
            Quit: Quit));
        _panel.Closed += (_, _) => _panel = null;
        _panel.ShowAt(at);
    }

    // ---- tray -------------------------------------------------------------------------

    void BuildTray()
    {
        _tray = new TrayIcon { ToolTipText = "Ringside", Icon = TrayArt.Render(null, _s), IsVisible = true };
        _tray.Clicked += (_, _) =>
        {
            if (!_started) { _onboarding?.Activate(); return; }
            SetHidden(!_hidden);
        };
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        RebuildTray();
    }

    void RebuildTray()
    {
        if (_tray is null) return;
        var menu = new NativeMenu();
        NativeMenuItem Item(string text, Action click, bool? check = null, bool radio = false)
        {
            var i = new NativeMenuItem(text);
            if (check is bool c)
            {
                i.ToggleType = radio ? NativeMenuItemToggleType.Radio : NativeMenuItemToggleType.CheckBox;
                i.IsChecked = c;
            }
            i.Click += (_, _) => click();
            return i;
        }
        NativeMenuItem Sub(string text, params NativeMenuItem[] items)
        {
            var m = new NativeMenu();
            foreach (var x in items) m.Add(x);
            return new NativeMenuItem(text) { Menu = m };
        }

        if (_started) menu.Add(Item(_hidden ? "Show Ringside" : "Hide Ringside", () => SetHidden(!_hidden)));
        else menu.Add(Item("Finish setting up Ringside", ShowOnboarding));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Sub("View",
            Item("Card", () => Change(s => s.View = ViewKind.Card), _s.View == ViewKind.Card, true),
            Item("Pill", () => Change(s => s.View = ViewKind.Pill), _s.View == ViewKind.Pill, true),
            Item("Orb", () => Change(s => s.View = ViewKind.Orb), _s.View == ViewKind.Orb, true)));
        menu.Add(Sub("Show limits as",
            Item("Used (fills up)", () => Change(s => s.Fill = FillMode.Used), _s.Fill == FillMode.Used, true),
            Item("Left (drains like a tank)", () => Change(s => s.Fill = FillMode.Left), _s.Fill == FillMode.Left, true)));
        if (_s.ShowClaude && _s.ShowCodex)
            menu.Add(Item(_s.Split ? "Combine into one" : "Split apart", () => ToggleSplit()));
        menu.Add(Item("Keep on top", () => Change(s => s.Topmost = !s.Topmost), _s.Topmost));
        if (Os.SupportsClickThrough)
            menu.Add(Item("Click-through", () => Change(s => s.ClickThrough = !s.ClickThrough), _s.ClickThrough));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Settings...", OpenSettings));
        menu.Add(Item("Refresh now", () => _ = RefreshAsync()));
        if (_updates?.PendingVersion is { } pending) menu.Add(Item($"Restart to update ({pending})", _updates.RestartNow));
        else if (_updates?.AvailableVersion is { } available) menu.Add(Item($"Get Ringside {available}", _updates.RestartNow));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit Ringside", Quit));
        _tray.Menu = menu;
    }

    void UpdateTrayIcon()
    {
        if (_tray is null) return;
        string tip = TrayArt.Tooltip(_last, _s);
        string key = tip + "|" + _s.ClaudeColor + _s.CodexColor + _s.Colors + _s.LevelColors + _s.Fill;
        if (key == _trayKey) return;
        _trayKey = key;
        _tray.Icon = TrayArt.Render(_last, _s);
        _tray.ToolTipText = tip;
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _poll.Stop();
        _s.Save();
        _updates.ApplyOnExit();
        if (_tray is not null) { _tray.IsVisible = false; _tray.Dispose(); }
        _life.Shutdown();
    }
}
