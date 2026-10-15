using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ringside;

/// <summary>
/// Optional link to Claude Code. Claude Code hands its status line command the account's
/// exact limits after each reply; pointing that command at ringside-statusline.exe lets
/// Ringside show exact reset times. Whatever status line the user had keeps working: the
/// bridge runs it and prints its output. Disconnecting puts the original back.
/// </summary>
static class ClaudeCodeHook
{
    const string BridgeName = "ringside-statusline.exe";

    static string ConfigDir =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Paths.Home, ".claude");

    static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    static string PreviousPath => Path.Combine(Paths.Data, "statusline-previous.json");
    static string BridgePath => Path.Combine(AppContext.BaseDirectory, BridgeName);

    public static bool ClaudeCodeFound => Directory.Exists(ConfigDir);
    public static bool BridgeAvailable => File.Exists(BridgePath);

    public static bool IsConnected
    {
        get
        {
            try { return IsOurs(Load()?["statusLine"]); }
            catch (Exception) { return false; }
        }
    }

    static bool IsOurs(JsonNode? line) =>
        line is JsonObject o && o["command"]?.GetValueKind() == JsonValueKind.String &&
        o["command"]!.GetValue<string>().Contains(BridgeName, StringComparison.OrdinalIgnoreCase);

    // Forward slashes and quotes work in the bash Claude Code uses on Windows, and in cmd.
    static string Command() => "\"" + BridgePath.Replace('\\', '/') + "\"";

    public static bool Connect()
    {
        try
        {
            if (!BridgeAvailable) return false;
            Directory.CreateDirectory(ConfigDir);
            var root = Load() ?? new JsonObject();
            var current = root["statusLine"];
            if (!IsOurs(current))
            {
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(PreviousPath, current?.ToJsonString() ?? "null");
            }
            var line = new JsonObject { ["type"] = "command", ["command"] = Command() };
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

    public static void Disconnect()
    {
        try
        {
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
