using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ringside;

/// <summary>Card: the full readout. Pill: one slim line. Orb: the logo, a ring inside a ring.</summary>
public enum ViewKind { Card, Pill, Orb }

/// <summary>Used fills up toward the limit. Left drains like a fuel tank.</summary>
public enum FillMode { Used, Left }

/// <summary>How the card draws each app's main limit.</summary>
public enum GaugeStyle { Ring, Bar }

/// <summary>App colors keep each app's own color. Traffic light is green, then amber, then red.</summary>
public enum ColorMode { AppColors, TrafficLight }

public enum ResetStyle { Countdown, Clock }

public enum FocusWindow { Tightest, FiveHour, Weekly }

public sealed class Settings
{
    public int Version { get; set; } = 2;
    public bool OnboardingDone { get; set; }
    public bool HintShown { get; set; }

    // Physical pixels, so the spot survives monitors with different scaling.
    public int? X { get; set; }
    public int? Y { get; set; }
    // Where each meter sits while they are split apart.
    public int? ClaudeX { get; set; }
    public int? ClaudeY { get; set; }
    public int? CodexX { get; set; }
    public int? CodexY { get; set; }

    // ---- look -------------------------------------------------------------------------
    public ViewKind View { get; set; } = ViewKind.Card;
    public bool Split { get; set; }
    public FillMode Fill { get; set; } = FillMode.Used;
    public GaugeStyle Gauge { get; set; } = GaugeStyle.Ring;
    public ColorMode Colors { get; set; } = ColorMode.AppColors;
    public bool LevelColors { get; set; } = true;
    public string ClaudeColor { get; set; } = Palette.ClaudeDefault;
    public string CodexColor { get; set; } = Palette.CodexDefault;
    public ResetStyle Resets { get; set; } = ResetStyle.Countdown;
    public FocusWindow Focus { get; set; } = FocusWindow.Tightest;
    public bool Forecast { get; set; } = true;

    public bool ShowClaude { get; set; } = true;
    public bool ShowCodex { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;

    // ---- limits, as percent used ---------------------------------------------------------
    public double WarnAt { get; set; } = 75;
    public double AlertAt { get; set; } = 90;

    // ---- behavior ---------------------------------------------------------------------
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool HideOverFullScreen { get; set; } = true;

    public bool NotifyAtWarn { get; set; } = true;
    public bool NotifyAtAlert { get; set; } = true;
    public bool NotifyOnReset { get; set; } = true;
    public bool NotifyForecast { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Ringside 1.x saved the pill view as a flag; read once, then dropped.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Compact { get; set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(Paths.Data, "settings.json");

    public static Settings Load()
    {
        try { return Parse(File.ReadAllText(FilePath)); }
        catch (Exception) { return new Settings(); }
    }

    internal static Settings Parse(string json)
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings(); }
        catch (JsonException) { return new Settings(); }
        s.Normalize();
        return s;
    }

    /// <summary>Clamps every value into a range the UI can draw and upgrades 1.x files.</summary>
    public void Normalize()
    {
        if (Compact == true && View == ViewKind.Card) View = ViewKind.Pill;
        Compact = null;
        Version = 2;
        Scale = Math.Clamp(Scale, 0.7, 1.6);
        Opacity = Math.Clamp(Opacity, 0.4, 1.0);
        WarnAt = Math.Clamp(Math.Round(WarnAt), 30, 90);
        AlertAt = Math.Clamp(Math.Round(AlertAt), WarnAt + 5, 99);
        if (!ShowClaude && !ShowCodex) ShowClaude = ShowCodex = true;
        if (!Palette.TryParse(ClaudeColor, out _)) ClaudeColor = Palette.ClaudeDefault;
        if (!Palette.TryParse(CodexColor, out _)) CodexColor = Palette.CodexDefault;
        // Splitting only means something with two meters.
        if (!(ShowClaude && ShowCodex)) Split = false;
    }

    public string ColorOf(string provider) => provider == "Codex" ? CodexColor : ClaudeColor;

    public bool Shows(string provider) => provider == "Codex" ? ShowCodex : ShowClaude;

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json), Json)!;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, ToJson());
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) { Log.Write("settings save failed: " + e.Message); }
    }
}

public static class Log
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
