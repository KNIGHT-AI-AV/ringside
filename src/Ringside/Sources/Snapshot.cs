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

static class Paths
{
    public static string Data => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ringside");

    public static string StatusLineFeed => Path.Combine(Data, "claude-statusline.json");

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
