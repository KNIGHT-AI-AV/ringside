using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ringside;

/// <summary>
/// Optional link to Claude Code. Claude Code hands its status line command the account's
/// exact limits after each reply; pointing that command at Ringside's bridge lets Ringside
/// show exact reset times. Whatever status line the user had keeps working: the bridge runs
/// it and prints its output. Disconnecting puts the original back.
/// </summary>
public static class ClaudeCodeHook
{
    /// <summary>
    /// The bridge, set by the app at startup. On Windows it is the native ringside-statusline.exe
    /// next to Ringside.exe; on macOS and Linux it is Ringside itself run with --statusline.
    /// </summary>
    public static string? BridgeExe { get; set; }
    public static bool BridgeIsApp { get; set; }

    static string ConfigDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Paths.Home, ".claude");

    static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    static string PreviousPath => Path.Combine(Paths.Data, "statusline-previous.json");

    public static bool ClaudeCodeFound => Directory.Exists(ConfigDir);
    public static bool BridgeAvailable => BridgeExe is not null && File.Exists(BridgeExe);

    /// <summary>The plugin folder shipped with the app; copied to <see cref="PluginDir"/> on connect.</summary>
    public static string? PluginSource { get; set; }
    public static bool PluginAvailable => PluginSource is not null && Directory.Exists(PluginSource);

    /// <summary>
    /// Where the installed plugin lives: Ringside's own data folder, a path that stays put across
    /// updates on every platform (an AppImage mounts somewhere new each run).
    /// </summary>
    static string PluginDir => Path.Combine(Paths.Data, "claude-plugin");
    const string PluginEnv = "CLAUDE_CODE_PLUGIN_DIRS";
    static char Separator => OperatingSystem.IsWindows() ? ';' : ':';

    /// <summary>Either half of the link is in place: the status line or the plugin.</summary>
    public static bool IsConnected
    {
        get
        {
            try
            {
                var root = Load();
                return IsOurs(root?["statusLine"]) || HasPlugin(root);
            }
            catch (Exception) { return false; }
        }
    }

    static bool HasPlugin(JsonObject? root) =>
        root?["env"] is JsonObject env && env[PluginEnv] is JsonValue v && v.TryGetValue(out string? dirs) &&
        dirs.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(PluginDir, StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a folder to a plugin-dirs list once; keeps everyone else's entries.</summary>
    internal static string AddDir(string? dirs, string dir, char separator)
    {
        var list = (dirs ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir);
        return string.Join(separator, list);
    }

    /// <summary>Removes a folder from a plugin-dirs list; null when nothing is left.</summary>
    internal static string? RemoveDir(string? dirs, string dir, char separator)
    {
        var list = (dirs ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => !string.Equals(d, dir, StringComparison.OrdinalIgnoreCase)).ToList();
        return list.Count == 0 ? null : string.Join(separator, list);
    }

    internal static bool IsOurs(JsonNode? line)
    {
        if (line is not JsonObject o || o["command"]?.GetValueKind() != JsonValueKind.String) return false;
        string c = o["command"]!.GetValue<string>();
        return c.Contains("ringside-statusline", StringComparison.OrdinalIgnoreCase) ||
               (c.Contains("--statusline", StringComparison.Ordinal) && c.Contains("Ringside", StringComparison.OrdinalIgnoreCase));
    }

    // Claude Code runs status lines with bash on every platform (Git Bash on Windows). Single quotes
    // keep the path literal there: no $, backtick or quote in a folder name can turn into shell syntax.
    internal static string Command(string exe, bool isApp) =>
        "'" + exe.Replace('\\', '/').Replace("'", "'\\''") + "'" + (isApp ? " --statusline" : "");

    /// <summary>
    /// Links Claude Code to Ringside in both ways it can: the plugin (live limits after every turn
    /// in the terminal, the Claude app's Code tab and VS Code) and the status line (the terminal).
    /// True when at least one is in place.
    /// </summary>
    public static bool Connect()
    {
        try
        {
            if (Paths.Sandboxed || !ClaudeCodeFound && !PluginAvailable && !BridgeAvailable) return false;
            Directory.CreateDirectory(ConfigDir);
            var root = Load() ?? new JsonObject();
            bool any = false;

            if (PluginAvailable)
            {
                CopyPlugin();
                AddPlugin(root);
                any = true;
            }

            if (BridgeAvailable)
            {
                var current = root["statusLine"];
                if (!IsOurs(current))
                {
                    Directory.CreateDirectory(Paths.Data);
                    File.WriteAllText(PreviousPath, current?.ToJsonString() ?? "null");
                }
                var line = new JsonObject { ["type"] = "command", ["command"] = Command(BridgeExe!, BridgeIsApp) };
                if (current is JsonObject old && old["padding"] is JsonNode padding) line["padding"] = padding.DeepClone();
                root["statusLine"] = line;
                any = true;
            }

            if (any) Save(root);
            return any;
        }
        catch (Exception e)
        {
            Log.Write("claude code connect failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// At every start: refreshes the installed plugin from this version of Ringside, and points
    /// an existing status line at the bridge's current path, e.g. after the app moved. Someone who
    /// linked Claude Code through the status line alone (every 1.x link) also gets the plugin,
    /// since the status line only runs in the terminal and goes quiet everywhere else.
    /// </summary>
    public static void Repair()
    {
        try
        {
            if (Paths.Sandboxed) return;
            var root = Load();
            if (root is null) return;
            if (PluginAvailable && (IsOurs(root["statusLine"]) || HasPlugin(root))) CopyPlugin();
            if (Refresh(root)) Save(root);
        }
        catch (Exception e) { Log.Write("claude code repair failed: " + e.Message); }
    }

    /// <summary>The settings half of <see cref="Repair"/>. True when it changed something.</summary>
    internal static bool Refresh(JsonObject root)
    {
        bool linked = IsOurs(root["statusLine"]);
        bool changed = false;

        if (linked && PluginAvailable && !HasPlugin(root))
        {
            AddPlugin(root);
            changed = true;
        }

        if (linked && BridgeAvailable && root["statusLine"] is JsonObject line)
        {
            string want = Command(BridgeExe!, BridgeIsApp);
            if (line["command"]!.GetValue<string>() != want)
            {
                line["command"] = want;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Takes both halves out again and puts back the status line the user had.</summary>
    public static void Disconnect()
    {
        try
        {
            if (Paths.Sandboxed) return;
            var root = Load();
            if (root is null) return;
            bool changed = false;

            if (IsOurs(root["statusLine"]))
            {
                JsonNode? previous = File.Exists(PreviousPath) ? JsonNode.Parse(File.ReadAllText(PreviousPath)) : null;
                if (previous is null) root.Remove("statusLine");
                else root["statusLine"] = previous;
                changed = true;
            }

            if (root["env"] is JsonObject env && env[PluginEnv] is JsonValue v && v.TryGetValue(out string? dirs))
            {
                string? rest = RemoveDir(dirs, PluginDir, Separator);
                if (rest != dirs)
                {
                    if (rest is null) env.Remove(PluginEnv);
                    else env[PluginEnv] = rest;
                    if (env.Count == 0) root.Remove("env");
                    changed = true;
                }
            }

            if (changed) Save(root);
            if (File.Exists(PreviousPath)) File.Delete(PreviousPath);
        }
        catch (Exception e)
        {
            Log.Write("claude code disconnect failed: " + e.Message);
        }
    }

    /// <summary>
    /// Names the plugin folder in Claude Code's settings env. A plugin-dirs value the user already
    /// has, in settings or in their own environment, is kept: the settings value replaces the
    /// environment one when Claude Code starts, so it has to carry both.
    /// </summary>
    static void AddPlugin(JsonObject root)
    {
        var env = root["env"] as JsonObject ?? new JsonObject();
        string? dirs = env[PluginEnv] is JsonValue v && v.TryGetValue(out string? d)
            ? d
            : Environment.GetEnvironmentVariable(PluginEnv);
        env[PluginEnv] = AddDir(dirs, PluginDir, Separator);
        root["env"] = env;
    }

    /// <summary>Copies the shipped plugin into Ringside's data folder, replacing an older copy.</summary>
    static void CopyPlugin()
    {
        foreach (var file in Directory.EnumerateFiles(PluginSource!, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(PluginDir, Path.GetRelativePath(PluginSource!, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    static JsonObject? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject;
    }

    static void Save(JsonObject root)
    {
        // Keep one copy of the file as it was before Ringside first touched it.
        if (File.Exists(SettingsPath))
        {
            var backup = SettingsPath + ".ringside-backup";
            if (!File.Exists(backup)) File.Copy(SettingsPath, backup);
        }
        var tmp = SettingsPath + ".ringside-tmp";
        // Plain quotes and non-ASCII text, so the file still reads like the user wrote it.
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
