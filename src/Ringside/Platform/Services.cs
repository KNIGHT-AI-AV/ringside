using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Ringside.Platform;

/// <summary>One Ringside per user. A second launch asks the first one to come forward.</summary>
public static class SingleInstance
{
    static Mutex? _mutex;
    // A sandboxed test run gets its own names, so it never collides with the installed copy.
    static string Suffix => Paths.Sandboxed ? "-test" + Stable(Paths.Data) : "";

    // string.GetHashCode changes per process; names must match across processes.
    static uint Stable(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h % 100000;
    }
    static string PipeName => "ringside-" + Sanitize(Environment.UserName) + Suffix;
    static string MutexName => (OperatingSystem.IsWindows() ? @"Local\" : "") + "Ringside.Singleton" + Suffix;

    static string Sanitize(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
        return new string(chars).ToLowerInvariant();
    }

    public static bool Claim()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool first);
            return first;
        }
        catch (Exception e)
        {
            Log.Write("single-instance check failed: " + e.Message);
            return true;
        }
    }

    /// <summary>Sends "activate" or "quit" to the running copy. False when none answers.</summary>
    public static bool Signal(string message)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            c.Connect(600);
            using var w = new StreamWriter(c) { AutoFlush = true };
            w.WriteLine(message);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static void Listen(Action<string> onMessage)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    // Only this user's processes may ask Ringside to come forward or quit.
                    using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    s.WaitForConnection();
                    using var r = new StreamReader(s);
                    var line = r.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line)) onMessage(line.Trim());
                }
                catch (Exception e)
                {
                    Log.Write("signal listener: " + e.Message);
                    Thread.Sleep(2000);
                }
            }
        }) { IsBackground = true, Name = "Ringside signals" };
        t.Start();
    }
}

/// <summary>Launch at login: the Run key on Windows, a LaunchAgent on macOS, XDG autostart on Linux.</summary>
public static class AutoStart
{
    const string Name = "Ringside";
    const string MacLabel = "com.knightaiav.ringside";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string MacPlist => Path.Combine(Paths.Home, "Library", "LaunchAgents", MacLabel + ".plist");
    static string LinuxDesktop => Path.Combine(Paths.XdgConfig, "autostart", "ringside.desktop");

    public static bool Enabled
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
                    return k?.GetValue(Name) is string;
                }
                return File.Exists(OperatingSystem.IsMacOS() ? MacPlist : LinuxDesktop);
            }
            catch (Exception) { return false; }
        }
    }

    public static void Set(bool on)
    {
        if (Paths.Sandboxed) { Log.Write($"sandbox: login item {(on ? "on" : "off")} not written"); return; }
        try
        {
            string? exe = Os.AppExecutable;
            if (OperatingSystem.IsWindows())
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (k is null) return;
                if (on && exe is not null) k.SetValue(Name, $"\"{exe}\"");
                else k.DeleteValue(Name, throwOnMissingValue: false);
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (!on || exe is null) { if (File.Exists(MacPlist)) File.Delete(MacPlist); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(MacPlist)!);
                File.WriteAllText(MacPlist, $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                      <key>Label</key><string>{MacLabel}</string>
                      <key>ProgramArguments</key><array><string>{Escape(exe)}</string></array>
                      <key>AssociatedBundleIdentifiers</key><array><string>{MacLabel}</string></array>
                      <key>RunAtLoad</key><true/>
                      <key>ProcessType</key><string>Interactive</string>
                      <key>LimitLoadToSessionType</key><string>Aqua</string>
                    </dict>
                    </plist>
                    """);
            }
            else
            {
                if (!on || exe is null) { if (File.Exists(LinuxDesktop)) File.Delete(LinuxDesktop); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(LinuxDesktop)!);
                File.WriteAllText(LinuxDesktop, $"""
                    [Desktop Entry]
                    Type=Application
                    Name=Ringside
                    Comment=Claude Code and Codex usage limits at a glance
                    Exec="{exe}"
                    Terminal=false
                    X-GNOME-Autostart-enabled=true
                    """);
            }
        }
        catch (Exception e) { Log.Write("autostart change failed: " + e.Message); }
    }

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Quiet background updates from the project's GitHub releases.</summary>
public sealed class Updates
{
    public const string RepoUrl = "https://github.com/KNIGHT-AI-AV/ringside";
    public const string ReleasesUrl = RepoUrl + "/releases/latest";

    readonly UpdateManager? _mgr;
    UpdateInfo? _pending;
    bool _checking;

    /// <summary>A Velopack update is downloaded and installs on restart.</summary>
    public event Action<string>? Ready;
    /// <summary>A newer version exists but this copy cannot update itself (a Mac build without Velopack).</summary>
    public event Action<string>? Available;

    public Updates()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (mgr.IsInstalled) _mgr = mgr; // a dev build has nothing to update
        }
        catch (Exception e) { Log.Write("updater unavailable: " + e.Message); }
    }

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();
    public string? AvailableVersion { get; private set; }

    public static string CurrentVersion =>
        typeof(Updates).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    /// <summary>Velopack updates, or for an app bundle it did not install, a version check.</summary>
    public bool Supported => _mgr is not null || Os.MacBundle is not null;
    public bool SelfUpdating => _mgr is not null;

    public async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            if (_mgr is not null)
            {
                if (_pending is not null) return;
                var info = await _mgr.CheckForUpdatesAsync();
                if (info is null) return;
                await _mgr.DownloadUpdatesAsync(info);
                _pending = info;
                Ready?.Invoke(info.TargetFullRelease.Version.ToString());
            }
            else if (Os.MacBundle is not null)
            {
                var latest = await LatestTagAsync();
                if (latest is not null && Version.TryParse(latest, out var v) && Version.TryParse(CurrentVersion, out var cur) && v > cur &&
                    AvailableVersion != latest)
                {
                    AvailableVersion = latest;
                    Available?.Invoke(latest);
                }
            }
        }
        catch (Exception e) { Log.Write("update check failed: " + e.Message); }
        finally { _checking = false; }
    }

    static async Task<string?> LatestTagAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Ringside/" + CurrentVersion);
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/KNIGHT-AI-AV/ringside/releases/latest"));
        return doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString()?.TrimStart('v') : null;
    }

    public void RestartNow()
    {
        if (_mgr is not null && _pending is not null) _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        else if (AvailableVersion is not null) Os.OpenUrl(ReleasesUrl);
    }

    /// <summary>Installs a downloaded update once Ringside has closed, without restarting it.</summary>
    public void ApplyOnExit()
    {
        try
        {
            if (_mgr is not null && _pending is not null)
                _mgr.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception e) { Log.Write("update apply failed: " + e.Message); }
    }
}

/// <summary>Undoes everything Ringside changed outside its own folder.</summary>
public static class Cleanup
{
    public static void Run(bool quitRunningCopy)
    {
        AutoStart.Set(false);
        ClaudeCodeHook.Disconnect();
        if (quitRunningCopy) CloseRunningCopy();

        // Keep the user's preferences, but walk through setup again after a reinstall,
        // since the startup entry and the Claude Code link are gone now.
        var settings = Settings.Load();
        settings.OnboardingDone = false;
        settings.Save();
    }

    // A running Ringside holds its own files open, which would leave them behind (Windows).
    static void CloseRunningCopy()
    {
        SingleInstance.Signal("quit");
        string installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        foreach (var p in Process.GetProcessesByName("Ringside"))
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    if (p.MainModule?.FileName is not { } exe ||
                        !exe.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!p.WaitForExit(4000)) p.Kill();
                }
                catch (Exception) { }
            }
        }
    }
}
