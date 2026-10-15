using System.Windows;
using System.Windows.Input;

namespace Ringside;

public sealed record Detection(bool ClaudeDesktop, bool ClaudeCode, bool Codex, bool BridgeAvailable)
{
    public static Detection Scan() => new(
        new ClaudeSource().DesktopLogFound,
        ClaudeCodeHook.ClaudeCodeFound,
        new CodexSource().SessionsFound,
        ClaudeCodeHook.BridgeAvailable);
}

public partial class OnboardingWindow : Window
{
    readonly Settings _settings;
    readonly bool _demo;

    public OnboardingWindow(Settings settings, Detection? demo = null)
    {
        _settings = settings;
        _demo = demo is not null;
        InitializeComponent();

        var found = demo ?? Detection.Scan();
        DesktopStatus.Text = found.ClaudeDesktop ? "Usage log found" : "Not found";
        CodeStatus.Text = found.ClaudeCode ? "Installed" : "Not found";
        CodexStatus.Text = found.Codex ? "Sessions found" : "Not found";
        NothingFound.Visibility = found.ClaudeDesktop || found.ClaudeCode || found.Codex
            ? Visibility.Collapsed : Visibility.Visible;

        bool hookPossible = found.ClaudeCode && found.BridgeAvailable;
        HookToggle.IsEnabled = hookPossible;
        HookToggle.IsChecked = hookPossible;
        if (!found.ClaudeCode) HookNote.Text = "Needs Claude Code. You can turn this on later in Settings.";
    }

    void OnStart(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (HookToggle.IsChecked == true) ClaudeCodeHook.Connect();
        AutoStart.Set(StartupToggle.IsChecked == true);
        _settings.CheckForUpdates = UpdatesToggle.IsChecked == true;
        _settings.OnboardingDone = true;
        _settings.Save();
        Close();
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (System.InvalidOperationException) { }
    }
}
