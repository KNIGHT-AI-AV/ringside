using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Ringside.Platform;

/// <summary>
/// What differs between Windows, macOS and Linux for a floating meter: staying out of
/// Alt+Tab and the Dock, never stealing focus, click-through, staying on top, and sitting
/// on every desktop. Everything else in Ringside is the same code everywhere.
/// </summary>
public static class Os
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static string LoginItemLabel => IsWindows ? "Start with Windows" : "Open at login";
    public static string TrayName => IsMac ? "menu bar" : IsWindows ? "tray" : "panel";
    public static bool SupportsClickThrough => IsWindows || IsMac;
    public static bool SupportsFullScreenHide => IsWindows || IsMac;

    /// <summary>On macOS, Topmost is handled through the window level instead (see ApplyTopmost).</summary>
    public static bool UseTopmostProperty => !IsMac;

    static IntPtr Handle(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    /// <summary>Called once the widget window exists on screen.</summary>
    public static void PrepareWidget(Window w, Settings s)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        try
        {
            if (IsWindows)
            {
                // A tool window stays out of Alt+Tab; NOACTIVATE means clicking it never steals
                // focus from whatever you are typing in.
                int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
                Win32.SetWindowLong(h, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            }
            else if (IsMac)
            {
                // On every Space; beside full-screen apps only if the user wants it there.
                nuint behavior = Mac.CanJoinAllSpaces | Mac.Stationary | Mac.IgnoresCycle;
                if (!s.HideOverFullScreen) behavior |= Mac.FullScreenAuxiliary;
                Mac.Send(h, "setCollectionBehavior:", behavior);
                Mac.Send(h, "setHasShadow:", false); // Ringside draws its own soft shadow
            }
        }
        catch (Exception e) { Log.Write("window setup failed: " + e.Message); }
        ApplyTopmost(w, s.Topmost);
        ApplyClickThrough(w, s.ClickThrough);
    }

    /// <summary>Notifications: never take focus, never show in Alt+Tab, visible on every Space.</summary>
    public static void PrepareToast(Window w)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        try
        {
            if (IsWindows)
            {
                int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
                Win32.SetWindowLong(h, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            }
            else if (IsMac)
            {
                Mac.Send(h, "setCollectionBehavior:", Mac.CanJoinAllSpaces | Mac.Stationary | Mac.IgnoresCycle | Mac.FullScreenAuxiliary);
                Mac.Send(h, "setLevel:", (nint)Mac.StatusLevel);
                Mac.Send(h, "setHasShadow:", false);
            }
        }
        catch (Exception e) { Log.Write("toast setup failed: " + e.Message); }
    }

    public static void ApplyTopmost(Window w, bool on)
    {
        if (UseTopmostProperty) { w.Topmost = on; return; }
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        try { Mac.Send(h, "setLevel:", (nint)(on ? Mac.FloatingLevel : Mac.NormalLevel)); }
        catch (Exception e) { Log.Write("level change failed: " + e.Message); }
    }

    public static void ApplyClickThrough(Window w, bool on)
    {
        var h = Handle(w);
        if (h == IntPtr.Zero) return;
        try
        {
            if (IsWindows)
            {
                int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
                Win32.SetWindowLong(h, Win32.GWL_EXSTYLE, on ? ex | Win32.WS_EX_TRANSPARENT : ex & ~Win32.WS_EX_TRANSPARENT);
            }
            else if (IsMac)
            {
                Mac.Send(h, "setIgnoresMouseEvents:", on);
            }
        }
        catch (Exception e) { Log.Write("click-through change failed: " + e.Message); }
    }

    /// <summary>Other always-on-top windows can bury it; take the top back without taking focus.</summary>
    public static void Reassert(Window w)
    {
        if (!IsWindows) return;
        var h = Handle(w);
        if (h != IntPtr.Zero)
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>True while a full-screen game, video or presentation is in front (Windows).</summary>
    public static bool FullScreenAppActive()
    {
        if (!IsWindows) return false;
        // 2 = a full-screen app, 3 = full-screen Direct3D, 4 = presentation mode.
        return Win32.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;
    }

    /// <summary>Hands memory from the large first read back to the system (Windows).</summary>
    public static void TrimWorkingSet()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        if (IsWindows) Win32.SetProcessWorkingSetSize(Win32.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    /// <summary>The program to launch at login and to hand Claude Code: stable across updates.</summary>
    public static string? AppExecutable =>
        IsLinux && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
            ? appImage
            : Environment.ProcessPath;

    /// <summary>macOS runs apps opened straight from Downloads from a random read-only copy.</summary>
    public static bool IsTranslocated =>
        IsMac && (Environment.ProcessPath ?? "").Contains("/AppTranslocation/", StringComparison.Ordinal);

    /// <summary>The .app bundle Ringside runs from on macOS, if any.</summary>
    public static string? MacBundle
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (!IsMac || exe is null) return null;
            var dir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
            return dir is not null && dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? dir : null;
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            var psi = IsWindows
                ? new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }
                : new System.Diagnostics.ProcessStartInfo(IsMac ? "open" : "xdg-open") { ArgumentList = { url }, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception e) { Log.Write("open link failed: " + e.Message); }
    }

    static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    }

    static class Mac
    {
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        public const nuint CanJoinAllSpaces = 1 << 0, Stationary = 1 << 4, IgnoresCycle = 1 << 6, FullScreenAuxiliary = 1 << 8;
        public const int NormalLevel = 0, FloatingLevel = 3, StatusLevel = 25;

        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendU(IntPtr r, IntPtr s, nuint a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendN(IntPtr r, IntPtr s, nint a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendB(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);

        public static void Send(IntPtr obj, string selector, nuint value) => SendU(obj, sel_registerName(selector), value);
        public static void Send(IntPtr obj, string selector, nint value) => SendN(obj, sel_registerName(selector), value);
        public static void Send(IntPtr obj, string selector, bool value) => SendB(obj, sel_registerName(selector), value);
    }
}
