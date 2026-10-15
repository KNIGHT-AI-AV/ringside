using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace Ringside;

static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // 2 = a full-screen app, 3 = full-screen Direct3D, 4 = presentation mode.
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
}

public sealed class Settings
{
    public bool OnboardingDone { get; set; }
    public bool HintShown { get; set; }

    // Physical pixels, so the spot survives monitors with different scaling.
    public int? X { get; set; }
    public int? Y { get; set; }

    public bool Compact { get; set; }
    public bool ShowClaude { get; set; } = true;
    public bool ShowCodex { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;

    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool HideOverFullScreen { get; set; } = true;

    public bool NotifyAtWarn { get; set; } = true;
    public bool NotifyAtAlert { get; set; } = true;
    public bool NotifyOnReset { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    static string FilePath => Path.Combine(Paths.Data, "settings.json");

    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            s.Scale = Math.Clamp(s.Scale, 0.8, 1.4);
            s.Opacity = Math.Clamp(s.Opacity, 0.4, 1.0);
            if (!s.ShowClaude && !s.ShowCodex) s.ShowClaude = s.ShowCodex = true;
            return s;
        }
        catch (Exception) { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) { Log.Write("settings save failed: " + e.Message); }
    }
}

static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Ringside";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k is null) return;
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception e) { Log.Write("autostart change failed: " + e.Message); }
    }
}

static class Log
{
    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var path = Path.Combine(Paths.Data, "ringside.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}

/// <summary>Runs when Windows uninstalls Ringside: undo everything it changed outside its folder.</summary>
static class Uninstall
{
    public const string QuitEvent = @"Local\Ringside.Quit";

    public static void Cleanup()
    {
        AutoStart.Set(false);
        ClaudeCodeHook.Disconnect();
        CloseRunningCopy();

        // Keep the user's preferences, but walk through setup again after a reinstall,
        // since the startup entry and the Claude Code link are gone now.
        var settings = Settings.Load();
        settings.OnboardingDone = false;
        settings.Save();
    }

    // A running Ringside holds its own files open, which would leave them behind.
    static void CloseRunningCopy()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(QuitEvent, out var quit))
                using (quit) quit.Set();
        }
        catch (Exception) { }

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
