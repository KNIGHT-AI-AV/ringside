using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace Ringside;

public partial class App : Application
{
    Mutex? _single;
    EventWaitHandle? _activate;
    WF.NotifyIcon? _tray;
    WF.ContextMenuStrip? _menu;
    WF.ToolStripMenuItem? _restartItem;
    SD.Icon? _icon;
    MainWindow? _win;
    SettingsWindow? _settingsWin;
    OnboardingWindow? _onboarding;
    Settings _settings = new();
    Updates? _updates;
    DispatcherTimer? _updateTimer;
    readonly Alerts _alerts = new();
    (string, string) _shown = ("", "");

    /// <summary>Set by the screenshot renderer: no single-instance check, no tray, no windows of its own.</summary>
    internal static bool RenderOnly;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (RenderOnly) return;
        _single = new Mutex(true, @"Local\Ringside.Singleton", out bool first);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Ringside.Activate");
        if (!first)
        {
            // Already running: bring that one forward instead.
            _activate.Set();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { Log.Write("unhandled: " + a.Exception); a.Handled = true; };
        Listen(_activate, BringForward);
        // The uninstaller signals this so the running copy closes before its files are removed.
        Listen(new EventWaitHandle(false, EventResetMode.AutoReset, Uninstall.QuitEvent), ExitApp);

        _settings = Settings.Load();
        _updates = new Updates();
        _updates.Ready += OnUpdateReady;
        BuildTray();

        if (_settings.OnboardingDone) StartMeter();
        else ShowOnboarding();
    }

    void Listen(EventWaitHandle handle, Action action)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                handle.WaitOne();
                Dispatcher.BeginInvoke(action);
            }
        }) { IsBackground = true, Name = "Ringside signal" };
        t.Start();
    }

    void BringForward()
    {
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        if (_win is null) return;
        _win.Show();
        _ = _win.RefreshAsync();
    }

    void ShowOnboarding()
    {
        _onboarding = new OnboardingWindow(_settings);
        _onboarding.Closed += (_, _) =>
        {
            _onboarding = null;
            // Closing the welcome card without choosing still starts the meter, changing nothing else.
            if (!_settings.OnboardingDone)
            {
                _settings.OnboardingDone = true;
                _settings.Save();
            }
            StartMeter();
        };
        _onboarding.Show();
        _onboarding.Activate();
    }

    void StartMeter()
    {
        _win = new MainWindow(_settings);
        _win.Updated += OnUpdated;
        _win.MenuRequested += ShowMenuAtCursor;
        _win.SettingsRequested += OpenSettings;
        _win.Show();

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (_settings.CheckForUpdates && _updates is not null) await _updates.CheckAsync();
        };
        _updateTimer.Start();
    }

    // ---- tray -------------------------------------------------------------------------

    static WF.ToolStripMenuItem Item(string text, Action action) => new(text, null, (_, _) => action());

    void BuildTray()
    {
        _menu = new WF.ContextMenuStrip
        {
            Renderer = new WF.ToolStripProfessionalRenderer(new DarkMenuColors()),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new SD.Font("Segoe UI", 9f),
            ForeColor = SD.Color.FromArgb(234, 234, 238),
            BackColor = SD.Color.FromArgb(30, 30, 35),
        };
        var show = Item("Hide Ringside", ToggleVisible);
        var compact = Item("Compact", () => _win?.ToggleCompact());
        var through = Item("Click-through", () =>
        {
            _settings.ClickThrough = !_settings.ClickThrough;
            _settings.Save();
            _win?.ApplySettings();
        });
        var settings = Item("Settings", OpenSettings);
        var refresh = Item("Refresh now", () => _ = _win?.RefreshAsync());
        _restartItem = Item("Restart to update", () => _updates?.RestartNow());
        _restartItem.Visible = false;
        var exit = Item("Exit", ExitApp);
        _menu.Items.AddRange(new WF.ToolStripItem[]
        {
            show, compact, through, new WF.ToolStripSeparator(),
            settings, refresh, _restartItem, new WF.ToolStripSeparator(),
            exit,
        });
        _menu.Opening += (_, _) =>
        {
            show.Enabled = _win is not null;
            show.Text = _win?.IsVisible == true ? "Hide Ringside" : "Show Ringside";
            compact.Checked = _settings.Compact;
            through.Checked = _settings.ClickThrough;
        };

        _icon = IconFactory.Tray(0, 0);
        _tray = new WF.NotifyIcon { Icon = _icon, Text = "Ringside", ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, a) => { if (a.Button == WF.MouseButtons.Left) ToggleVisible(); };
        _tray.BalloonTipClicked += (_, _) => BringForward();
    }

    void ShowMenuAtCursor()
    {
        if (_menu is null) return;
        _menu.Show(WF.Cursor.Position);
        // The widget never takes focus, so bring the menu forward: a menu only closes on
        // Escape or an outside click while its app is in front.
        Native.SetForegroundWindow(_menu.Handle);
    }

    async void ToggleVisible()
    {
        if (_win is null) { _onboarding?.Activate(); return; }
        if (_win.IsVisible) { _win.Hide(); return; }
        _win.Show();
        await _win.RefreshAsync();
    }

    void OnUpdated(Snapshot claude, Snapshot codex)
    {
        var now = DateTimeOffset.Now;
        foreach (var s in new[] { claude, codex })
        {
            if (s.Provider == "Claude" ? !_settings.ShowClaude : !_settings.ShowCodex) continue;
            foreach (var (title, body) in _alerts.Evaluate(s, _settings, now))
                _tray?.ShowBalloonTip(6000, title, body.Length == 0 ? "Open Ringside for details." : body, WF.ToolTipIcon.None);
        }

        if (_tray is null) return;
        double? a = _settings.ShowClaude ? claude.Primary?.Percent : null;
        double? b = _settings.ShowCodex ? codex.Primary?.Percent : null;
        string ta = a is null ? "-" : Fmt.Pct(a.Value) + "%", tb = b is null ? "-" : Fmt.Pct(b.Value) + "%";
        if (_shown == (ta, tb)) return;
        _shown = (ta, tb);
        var old = _icon;
        _icon = IconFactory.Tray(_settings.ShowClaude ? a ?? 0 : null, _settings.ShowCodex ? b ?? 0 : null);
        _tray.Icon = _icon;
        _tray.Text = $"Ringside: Claude {ta} · Codex {tb}";
        old?.Dispose();
    }

    void OnUpdateReady(string version)
    {
        if (_restartItem is not null)
        {
            _restartItem.Text = $"Restart to update ({version})";
            _restartItem.Visible = true;
        }
        _tray?.ShowBalloonTip(6000, "Ringside update ready",
            $"Version {version} installs the next time Ringside starts. To install it now, choose Restart to update in the tray menu.",
            WF.ToolTipIcon.None);
    }

    public void OpenSettings()
    {
        if (_settingsWin is not null) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(_settings, _updates);
        _settingsWin.Changed += () => _win?.ApplySettings();
        _settingsWin.ResetPositionRequested += () => _win?.ResetPosition();
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Show();
        _settingsWin.Activate();
    }

    void ExitApp()
    {
        _settings.Save();
        _updates?.ApplyOnExit();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _icon?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
