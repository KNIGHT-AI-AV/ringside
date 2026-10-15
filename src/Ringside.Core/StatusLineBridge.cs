// Ringside's status line bridge for Claude Code.
//
// Claude Code pipes a JSON description of the session to its status line command after
// each reply. The bridge keeps only the rate_limits block (5-hour and weekly percentages
// and reset times), saves it where Ringside reads it, and prints a status line: the one
// the user had before Ringside, if any, otherwise a short summary of the limits.
// It never sees or stores a login token; Claude Code does not send one.
//
// This file stands alone: the Windows build compiles it into the native
// ringside-statusline.exe, and on macOS and Linux Ringside runs it for --statusline.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Ringside;

public static class StatusLineBridge
{
    /// <summary>Must match Paths.Data; a test holds them together.</summary>
    public static string DataDir
    {
        get
        {
            if (Environment.GetEnvironmentVariable("RINGSIDE_DATA") is { Length: > 0 } custom) return custom;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ringside");
            if (OperatingSystem.IsMacOS())
                return Path.Combine(home, "Library", "Application Support", "Ringside");
            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
            return Path.Combine(xdg, "Ringside");
        }
    }

    public static int Run()
    {
        string input;
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            input = reader.ReadToEnd();
        }
        catch (Exception) { input = ""; }

        JsonObject? limits = null;
        try { limits = Save(input); }
        catch (Exception) { /* a status line must never fail */ }

        string? previous = null;
        try { previous = RunPrevious(input); }
        catch (Exception) { }

        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        stdout.Write(previous ?? Summary(limits, ShowsLeft()));
        return 0;
    }

    /// <summary>Writes {updated, five_hour, seven_day} with resets as Unix seconds.</summary>
    internal static JsonObject? Save(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (JsonNode.Parse(input) is not JsonObject root || root["rate_limits"] is not JsonObject rl) return null;

        var outObj = new JsonObject { ["updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        foreach (var key in new[] { "five_hour", "seven_day" })
        {
            if (rl[key] is not JsonObject w) continue;
            if (!TryNumber(w["used_percentage"], out double pct)) continue;
            var item = new JsonObject { ["used_percentage"] = Math.Round(pct, 2) };
            if (ToUnixSeconds(w["resets_at"]) is long resets) item["resets_at"] = resets;
            outObj[key] = item;
        }
        if (outObj.Count == 1) return null;

        Directory.CreateDirectory(DataDir);
        var path = Path.Combine(DataDir, "claude-statusline.json");
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, outObj.ToJsonString());
        File.Move(tmp, path, overwrite: true);
        return outObj;
    }

    static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue(out double d)) { value = d; return true; }
        if (v.TryGetValue(out long l)) { value = l; return true; }
        if (v.TryGetValue(out int i)) { value = i; return true; }
        return v.TryGetValue(out string? s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // Accepts Unix seconds, Unix milliseconds or an ISO 8601 timestamp.
    static long? ToUnixSeconds(JsonNode? node)
    {
        if (TryNumber(node, out double n) && n > 0)
            return n > 1e11 ? (long)(n / 1000) : (long)n;
        if (node is JsonValue v && v.TryGetValue(out string? s) &&
            DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
            return t.ToUnixTimeSeconds();
        return null;
    }

    // The summary speaks the same way as the widget: "used" or "left".
    static bool ShowsLeft()
    {
        try
        {
            var file = Path.Combine(DataDir, "settings.json");
            return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject s &&
                   s["Fill"] is JsonValue f && f.TryGetValue(out string? fill) &&
                   string.Equals(fill, "Left", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    internal static string Summary(JsonObject? limits, bool left)
    {
        if (limits is null) return "";
        var parts = new StringBuilder();
        if (limits["five_hour"] is JsonObject fh)
        {
            parts.Append("5h ").Append(Pct(fh, left)).Append(left ? "% left" : "%");
            if (fh["resets_at"] is JsonValue r && r.TryGetValue(out long at))
            {
                var remaining = DateTimeOffset.FromUnixTimeSeconds(at) - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero) parts.Append(" · resets ").Append(Span(remaining));
            }
        }
        if (limits["seven_day"] is JsonObject sd)
        {
            if (parts.Length > 0) parts.Append(" | ");
            parts.Append("7d ").Append(Pct(sd, left)).Append(left ? "% left" : "%");
        }
        return parts.ToString();
    }

    static string Pct(JsonObject w, bool left) =>
        TryNumber(w["used_percentage"], out double p)
            ? Math.Round(Math.Clamp(left ? 100 - p : p, 0, 100)).ToString("0")
            : "-";

    static string Span(TimeSpan d) =>
        d.TotalDays >= 1 ? $"{(int)d.TotalDays}d {d.Hours}h"
        : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}m"
        : $"{Math.Max(1, (int)d.TotalMinutes)}m";

    /// <summary>Runs the status line the user had before Ringside, feeding it the same input.</summary>
    static string? RunPrevious(string input)
    {
        var file = Path.Combine(DataDir, "statusline-previous.json");
        if (!File.Exists(file)) return null;
        if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject prev) return null;
        if (prev["command"] is not JsonValue c || !c.TryGetValue(out string? command) || string.IsNullOrWhiteSpace(command)) return null;
        if (command.Contains("ringside-statusline", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("--statusline", StringComparison.Ordinal) && command.Contains("Ringside", StringComparison.OrdinalIgnoreCase))
            return null;

        bool powershell = prev["shell"] is JsonValue sh && sh.TryGetValue(out string? shell) &&
                          string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var psi = Launcher(command, powershell);
        if (psi is null) return null;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;

        using var p = Process.Start(psi);
        if (p is null) return null;
        p.StandardInput.Write(input);
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(4000))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { }
            return null;
        }
        return output.Result.TrimEnd();
    }

    // Claude Code runs status lines with bash (Git Bash on Windows) unless told to use PowerShell.
    static ProcessStartInfo? Launcher(string command, bool powershell)
    {
        if (!OperatingSystem.IsWindows())
        {
            var unixShell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            var u = new ProcessStartInfo(unixShell);
            u.ArgumentList.Add("-c");
            u.ArgumentList.Add(command);
            return u;
        }
        if (powershell)
        {
            var exe = Find("pwsh.exe") ?? Find("powershell.exe");
            if (exe is null) return null;
            var ps = new ProcessStartInfo(exe);
            ps.ArgumentList.Add("-NoProfile");
            ps.ArgumentList.Add("-NonInteractive");
            ps.ArgumentList.Add("-Command");
            ps.ArgumentList.Add(command);
            return ps;
        }
        var bash = Find("bash.exe") ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (bash is not null && File.Exists(bash))
        {
            var b = new ProcessStartInfo(bash);
            b.ArgumentList.Add("-c");
            b.ArgumentList.Add(command);
            return b;
        }
        var cmd = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe");
        cmd.ArgumentList.Add("/d");
        cmd.ArgumentList.Add("/s");
        cmd.ArgumentList.Add("/c");
        cmd.ArgumentList.Add(command);
        return cmd;
    }

    static string? Find(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
