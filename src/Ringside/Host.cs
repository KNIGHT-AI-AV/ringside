using System;
using Ringside.Platform;
using Ringside.Ui;

namespace Ringside;

/// <summary>What the settings and onboarding windows need from the running app.</summary>
public interface IHost
{
    Settings Settings { get; }
    Reading? Last { get; }
    Updates? Updates { get; }
    /// <summary>True for still renders: show everything, change nothing on the computer.</summary>
    bool Demo { get; }
    void Change(Action<Settings> edit);
    void ResetPosition();
    void ShowOnboarding();
    void Quit();
}

/// <summary>A stand-in host for README images and previews.</summary>
public sealed class DemoHost : IHost
{
    public DemoHost(Settings settings, Reading reading) { Settings = settings; Last = reading; }
    public Settings Settings { get; }
    public Reading? Last { get; }
    public Updates? Updates => null;
    public bool Demo => true;
    public void Change(Action<Settings> edit) { edit(Settings); Settings.Normalize(); }
    public void ResetPosition() { }
    public void ShowOnboarding() { }
    public void Quit() { }
}

/// <summary>Which of the apps Ringside reads are on this computer.</summary>
public sealed record Detection(bool ClaudeDesktop, bool ClaudeCode, bool Codex, bool BridgeAvailable)
{
    public static Detection Scan() => new(
        new ClaudeSource().DesktopLogFound,
        ClaudeCodeHook.ClaudeCodeFound,
        new CodexSource().SessionsFound,
        ClaudeCodeHook.BridgeAvailable || ClaudeCodeHook.PluginAvailable);

    public static readonly Detection Everything = new(true, true, true, true);
}
