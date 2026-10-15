using System;
using System.Globalization;

namespace Ringside;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Moves lightness toward white (positive) or black (negative), keeping the hue.</summary>
    public Rgb Shade(double amount)
    {
        var (h, s, l) = ToHsl();
        l = Math.Clamp(amount >= 0 ? l + (1 - l) * amount : l * (1 + amount), 0, 1);
        return FromHsl(h, s, l);
    }

    /// <summary>Relative luminance (WCAG), for picking readable text on a color.</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    static Rgb FromHsl(double h, double s, double l)
    {
        if (s < 1e-9) { byte v = (byte)Math.Round(l * 255); return new Rgb(v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        return new Rgb(
            (byte)Math.Round(Hue(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
    }
}

public static class Palette
{
    public const string ClaudeDefault = "#D97757";
    public const string CodexDefault = "#19C39C";
    public const string Brand = "#8B7CF6";

    /// <summary>The swatches offered for each app. Any #RRGGBB also works.</summary>
    public static readonly (string Name, string Hex)[] Swatches =
    {
        ("Clay", ClaudeDefault),
        ("Mint", CodexDefault),
        ("Violet", Brand),
        ("Sky", "#4EA8F5"),
        ("Indigo", "#6475F0"),
        ("Teal", "#22B8CF"),
        ("Lime", "#8CCB45"),
        ("Gold", "#E2B53C"),
        ("Coral", "#FF7E67"),
        ("Rose", "#EF5D8C"),
        ("Orchid", "#C26AE8"),
        ("Silver", "#C9CCD6"),
    };

    public static readonly Rgb Amber = new(0xF3, 0xB0, 0x48);
    public static readonly Rgb Red = new(0xF2, 0x58, 0x5E);
    public static readonly Rgb Green = new(0x3D, 0xD6, 0x8C);

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        rgb = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string? hex, string fallback) =>
        TryParse(hex, out var c) ? c : TryParse(fallback, out var f) ? f : new Rgb(0x8B, 0x7C, 0xF6);

    /// <summary>The two ends of a gauge's gradient: lighter at the start, deeper at the end.</summary>
    public static (Rgb From, Rgb To) Gradient(Rgb c) => c.Hex switch
    {
        // The 1.x tints, kept exactly so upgraded meters look the same.
        ClaudeDefault => (new Rgb(0xF4, 0xA6, 0x88), new Rgb(0xCF, 0x64, 0x40)),
        CodexDefault => (new Rgb(0x62, 0xE5, 0xBF), new Rgb(0x0F, 0x9E, 0x7B)),
        _ => (c.Shade(0.28), c.Shade(-0.14)),
    };

    public static (Rgb From, Rgb To) AmberGradient => (new Rgb(0xFF, 0xD5, 0x84), new Rgb(0xEF, 0xA1, 0x36));
    public static (Rgb From, Rgb To) RedGradient => (new Rgb(0xFF, 0x8F, 0x86), new Rgb(0xEC, 0x40, 0x4D));
    public static (Rgb From, Rgb To) GreenGradient => (new Rgb(0x7D, 0xEB, 0xB4), new Rgb(0x1F, 0xB5, 0x6E));

    /// <summary>The gauge color for a limit at <paramref name="used"/> percent under these settings.</summary>
    public static (Rgb From, Rgb To) For(string provider, double used, Settings s)
    {
        var level = Display.LevelOf(used, s);
        if (s.Colors == ColorMode.TrafficLight)
            return level switch { Level.Alert => RedGradient, Level.Warn => AmberGradient, _ => GreenGradient };
        if (s.LevelColors && level == Level.Alert) return RedGradient;
        if (s.LevelColors && level == Level.Warn) return AmberGradient;
        return Gradient(Parse(s.ColorOf(provider), provider == "Codex" ? CodexDefault : ClaudeDefault));
    }

    /// <summary>The flat color used for dots, labels and the tray icon.</summary>
    public static Rgb Solid(string provider, double used, Settings s)
    {
        var level = Display.LevelOf(used, s);
        if (s.Colors == ColorMode.TrafficLight)
            return level switch { Level.Alert => Red, Level.Warn => Amber, _ => Green };
        if (s.LevelColors && level == Level.Alert) return Red;
        if (s.LevelColors && level == Level.Warn) return Amber;
        return Parse(s.ColorOf(provider), provider == "Codex" ? CodexDefault : ClaudeDefault);
    }
}
