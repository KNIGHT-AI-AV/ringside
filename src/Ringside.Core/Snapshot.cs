using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ringside;

public enum ResetKind { None, Exact, Estimate, UpperBound }

public sealed record LimitWindow(string Label, double Percent, DateTimeOffset? ResetsAt, ResetKind Kind, bool Lapsed);

public sealed record Snapshot(
    string Provider,
    string? Plan,
    IReadOnlyList<LimitWindow> Windows,
    DateTimeOffset? AsOf,
    TimeSpan? StaleAfter,
    string? Note,
    string Source)
{
    // The window closest to blocking you is the one worth a glance.
    public LimitWindow? Primary => Windows.OrderByDescending(w => w.Percent).FirstOrDefault();

    /// <summary>The window the big gauge shows: the tightest one, or the one the user pinned.</summary>
    public LimitWindow? Pick(FocusWindow focus) => focus switch
    {
        FocusWindow.FiveHour => Windows.FirstOrDefault(w => w.Label == "5-hour") ?? Primary,
        FocusWindow.Weekly => Windows.FirstOrDefault(w => w.Label == "Weekly") ?? Primary,
        _ => Primary,
    };

    public bool IsStale(DateTimeOffset now) => AsOf is { } t && StaleAfter is { } s && now - t > s;

    public static Snapshot Empty(string provider, string note, string source) =>
        new(provider, null, Array.Empty<LimitWindow>(), null, null, note, source);
}

static class SharedFile
{
    // The apps that write these files keep writing them; never lock them.
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
}

public static class Paths
{
    /// <summary>Where Ringside keeps its settings, log and the Claude Code feed, per platform.</summary>
    public static string Data
    {
        get
        {
            if (Environment.GetEnvironmentVariable("RINGSIDE_DATA") is { Length: > 0 } custom) return custom;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ringside");
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Home, "Library", "Application Support", "Ringside");
            return Path.Combine(XdgConfig, "Ringside");
        }
    }

    public static string StatusLineFeed => Path.Combine(Data, "claude-statusline.json");

    /// <summary>
    /// A test run with its own data folder (RINGSIDE_DATA). It may read everything but must
    /// change nothing outside that folder: no Claude Code link, no login item.
    /// </summary>
    public static bool Sandboxed => Environment.GetEnvironmentVariable("RINGSIDE_DATA") is { Length: > 0 };

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string XdgConfig =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".config");

    /// <summary>What to call this computer in sentences: "this PC" or "this Mac".</summary>
    public static string ThisComputer =>
        OperatingSystem.IsWindows() ? "this PC" : OperatingSystem.IsMacOS() ? "this Mac" : "this computer";
}
