using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Ringside;

public partial class SettingsWindow : Window
{
    readonly Settings _settings;
    readonly Updates? _updates;
    readonly bool _demo;
    bool _loading;

    public event Action? Changed;
    public event Action? ResetPositionRequested;

    public SettingsWindow(Settings settings, Updates? updates, bool demo = false)
    {
        _settings = settings;
        _updates = updates;
        _demo = demo;
        InitializeComponent();
        Load();
    }

    void Load()
    {
        _loading = true;
        ShowClaude.IsChecked = _settings.ShowClaude;
        ShowCodex.IsChecked = _settings.ShowCodex;
        SizeSmall.IsChecked = _settings.Scale < 0.95;
        SizeLarge.IsChecked = _settings.Scale > 1.05;
        SizeNormal.IsChecked = SizeSmall.IsChecked != true && SizeLarge.IsChecked != true;
        OpacitySlider.Value = Math.Round(_settings.Opacity * 100);
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        NotifyWarn.IsChecked = _settings.NotifyAtWarn;
        NotifyAlert.IsChecked = _settings.NotifyAtAlert;
        NotifyReset.IsChecked = _settings.NotifyOnReset;
        TopmostToggle.IsChecked = _settings.Topmost;
        ClickThrough.IsChecked = _settings.ClickThrough;
        HideFullScreen.IsChecked = _settings.HideOverFullScreen;
        StartWithWindows.IsChecked = _demo || AutoStart.Enabled;
        AutoUpdate.IsChecked = _settings.CheckForUpdates;
        VersionRun.Text = "Ringside " + Updates.CurrentVersion;
        ShowHookState();
        _loading = false;
    }

    void ShowHookState()
    {
        bool found = _demo || ClaudeCodeHook.ClaudeCodeFound;
        bool connected = _demo || ClaudeCodeHook.IsConnected;
        HookButton.Content = connected ? "Disconnect" : "Connect";
        HookButton.IsEnabled = _demo || (found && ClaudeCodeHook.BridgeAvailable);
        HookStatus.Text = connected ? "On. Reset times come straight from Claude Code."
            : found ? "Off. Claude reset times are estimated."
            : "Claude Code was not found on this PC.";
    }

    void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _demo) return;
        // Always keep at least one meter on screen.
        if (ShowClaude.IsChecked != true && ShowCodex.IsChecked != true)
        {
            if (ReferenceEquals(sender, ShowClaude)) ShowCodex.IsChecked = true;
            else ShowClaude.IsChecked = true;
        }
        _settings.ShowClaude = ShowClaude.IsChecked == true;
        _settings.ShowCodex = ShowCodex.IsChecked == true;
        _settings.Scale = SizeSmall.IsChecked == true ? 0.85 : SizeLarge.IsChecked == true ? 1.2 : 1.0;
        _settings.NotifyAtWarn = NotifyWarn.IsChecked == true;
        _settings.NotifyAtAlert = NotifyAlert.IsChecked == true;
        _settings.NotifyOnReset = NotifyReset.IsChecked == true;
        _settings.Topmost = TopmostToggle.IsChecked == true;
        _settings.ClickThrough = ClickThrough.IsChecked == true;
        _settings.HideOverFullScreen = HideFullScreen.IsChecked == true;
        _settings.CheckForUpdates = AutoUpdate.IsChecked == true;
        _settings.Save();
        Changed?.Invoke();
    }

    void OnOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText is null) return;
        OpacityText.Text = $"{e.NewValue:0}%";
        if (_loading || _demo) return;
        _settings.Opacity = Math.Round(e.NewValue) / 100;
        _settings.Save();
        Changed?.Invoke();
    }

    void OnStartup(object sender, RoutedEventArgs e)
    {
        if (_loading || _demo) return;
        AutoStart.Set(StartWithWindows.IsChecked == true);
        StartWithWindows.IsChecked = AutoStart.Enabled;
    }

    void OnHook(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (ClaudeCodeHook.IsConnected) ClaudeCodeHook.Disconnect();
        else ClaudeCodeHook.Connect();
        ShowHookState();
    }

    async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (_updates is null || !_updates.Supported)
        {
            UpdateStatus.Text = "Updates work in the installed version.";
            return;
        }
        CheckNow.IsEnabled = false;
        UpdateStatus.Text = "Checking...";
        await _updates.CheckAsync();
        UpdateStatus.Text = _updates.PendingVersion is { } v
            ? $"Version {v} is ready. It installs when Ringside restarts."
            : "You have the latest version.";
        CheckNow.IsEnabled = true;
    }

    void OnResetPosition(object sender, RoutedEventArgs e)
    {
        if (!_demo) ResetPositionRequested?.Invoke();
    }

    void OnRepo(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Updates.RepoUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open link failed: " + ex.Message); }
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}
