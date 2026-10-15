using System;
using System.IO;
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

    public static bool IsConnected
    {
        get
        {
            try { return IsOurs(Load()?["statusLine"]); }
            catch (Exception) { return false; }
        }
    }

    internal static bool IsOurs(JsonNode? line)
    {
        if (line is not JsonObject o || o["command"]?.GetValueKind() != JsonValueKind.String) return false;
        string c = o["command"]!.GetValue<string>();
        return c.Contains("ringside-statusline", StringComparison.OrdinalIgnoreCase) ||
               (c.Contains("--statusline", StringComparison.Ordinal) && c.Contains("Ringside", StringComparison.OrdinalIgnoreCase));
    }

    // Forward slashes and quotes work in the bash Claude Code uses on every platform, and in cmd.
    internal static string Command(string exe, bool isApp) =>
        "\"" + exe.Replace('\\', '/') + "\"" + (isApp ? " --statusline" : "");

    public static bool Connect()
    {
        try
        {
            if (!BridgeAvailable || Paths.Sandboxed) return false;
            Directory.CreateDirectory(ConfigDir);
            var root = Load() ?? new JsonObject();
            var current = root["statusLine"];
            if (!IsOurs(current))
            {
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(PreviousPath, current?.ToJsonString() ?? "null");
            }
            var line = new JsonObject { ["type"] = "command", ["command"] = Command(BridgeExe!, BridgeIsApp) };
            if (current is JsonObject old && old["padding"] is JsonNode padding) line["padding"] = padding.DeepClone();
            root["statusLine"] = line;
            Save(root);
            return true;
        }
        catch (Exception e)
        {
            Log.Write("claude code connect failed: " + e.Message);
            return false;
        }
    }

    /// <summary>Points an existing link at the bridge's current path, e.g. after the app moved.</summary>
    public static void Repair()
    {
        try
        {
            if (!BridgeAvailable || Paths.Sandboxed) return;
            var root = Load();
            if (root?["statusLine"] is not JsonObject line || !IsOurs(line)) return;
            string want = Command(BridgeExe!, BridgeIsApp);
            if (line["command"]!.GetValue<string>() == want) return;
            line["command"] = want;
            Save(root);
        }
        catch (Exception e) { Log.Write("claude code repair failed: " + e.Message); }
    }

    public static void Disconnect()
    {
        try
        {
            if (Paths.Sandboxed) return;
            var root = Load();
            if (root is null || !IsOurs(root["statusLine"])) return;
            JsonNode? previous = File.Exists(PreviousPath) ? JsonNode.Parse(File.ReadAllText(PreviousPath)) : null;
            if (previous is null) root.Remove("statusLine");
            else root["statusLine"] = previous;
            Save(root);
            if (File.Exists(PreviousPath)) File.Delete(PreviousPath);
        }
        catch (Exception e)
        {
            Log.Write("claude code disconnect failed: " + e.Message);
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
