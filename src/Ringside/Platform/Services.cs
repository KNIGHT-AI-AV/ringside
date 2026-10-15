using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Ringside;

/// <summary>Quiet background updates from the project's GitHub releases.</summary>
public sealed class Updates
{
    public const string RepoUrl = "https://github.com/KNIGHT-AI-AV/ringside";

    readonly UpdateManager? _mgr;
    UpdateInfo? _pending;
    bool _checking;

    public event Action<string>? Ready;

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

    public static string CurrentVersion =>
        typeof(Updates).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    public async Task CheckAsync()
    {
        if (_mgr is null || _pending is not null || _checking) return;
        _checking = true;
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info is null) return;
            await _mgr.DownloadUpdatesAsync(info);
            _pending = info;
            Ready?.Invoke(info.TargetFullRelease.Version.ToString());
        }
        catch (Exception e) { Log.Write("update check failed: " + e.Message); }
        finally { _checking = false; }
    }

    public bool Supported => _mgr is not null;

    public void RestartNow()
    {
        if (_mgr is not null && _pending is not null) _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
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

/// <summary>Decides when a limit deserves a notification. The first reading only sets a baseline.</summary>
sealed class Alerts
{
    readonly Dictionary<string, double> _seen = new();

    public IEnumerable<(string Title, string Body)> Evaluate(Snapshot s, Settings settings, DateTimeOffset now)
    {
        foreach (var w in s.Windows)
        {
            string key = s.Provider + "|" + w.Label;
            bool known = _seen.TryGetValue(key, out double prev);
            _seen[key] = w.Percent;
            if (!known) continue;

            string reset = Fmt.Reset(w, now);
            string body = reset.Length == 0 ? "" : char.ToUpperInvariant(reset[0]) + reset[1..] + ".";
            if (settings.NotifyAtAlert && prev < Theme.AlertAt && w.Percent >= Theme.AlertAt)
                yield return ($"{s.Provider} {w.Label} limit at {Fmt.Pct(w.Percent)}%", body);
            else if (settings.NotifyAtWarn && prev < Theme.WarnAt && w.Percent >= Theme.WarnAt)
                yield return ($"{s.Provider} {w.Label} limit at {Fmt.Pct(w.Percent)}%", body);

            if (settings.NotifyOnReset && prev >= 50 && w.Percent < 25 && w.Percent + 30 < prev)
                yield return ($"{s.Provider} {w.Label} limit reset", $"Back to {Fmt.Pct(w.Percent)}% used.");
        }
    }
}
