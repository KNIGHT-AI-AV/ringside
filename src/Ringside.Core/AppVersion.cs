using System;

namespace Ringside;

/// <summary>Version arithmetic for updates, kept apart from the network so it can be tested.</summary>
public static class AppVersion
{
    /// <summary>
    /// True on the first run after Ringside updated itself. A copy with settings but no recorded
    /// version came from 2.0.2 or earlier, before Ringside remembered which version last ran.
    /// </summary>
    public static bool JustUpdated(string? lastRun, string current, bool hadSettings)
    {
        if (!Version.TryParse(current, out var now)) return false; // a dev build
        if (lastRun is null) return hadSettings;
        return Version.TryParse(lastRun, out var last) && now > last;
    }

    /// <summary>The version a GitHub release page link names, such as 2.0.3 for .../releases/tag/v2.0.3.</summary>
    public static string? FromReleaseUrl(Uri? url)
    {
        if (url is null) return null;
        string path = url.IsAbsoluteUri ? url.AbsolutePath : url.OriginalString;
        int at = path.LastIndexOf("/tag/", StringComparison.Ordinal);
        if (at < 0) return null;
        string tag = Uri.UnescapeDataString(path[(at + 5)..]).Trim('/').TrimStart('v', 'V');
        return Version.TryParse(tag, out _) ? tag : null;
    }
}
