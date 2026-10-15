using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ringside.Ui;

/// <summary>Colors and icons shared by every window.</summary>
public static class Brand
{
    public static readonly Color Fg1 = Color.Parse("#F4F4F6");
    public static readonly Color Fg2 = Color.Parse("#A6A6B0");
    public static readonly Color Fg3 = Color.Parse("#7C7C86");
    public static readonly Color Accent = Color.Parse("#8B7CF6");
    public static readonly IBrush Fg1Brush = new SolidColorBrush(Fg1);
    public static readonly IBrush Fg2Brush = new SolidColorBrush(Fg2);
    public static readonly IBrush Fg3Brush = new SolidColorBrush(Fg3);
    public static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#1EFFFFFF"));

    public static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static IBrush Solid(Rgb c) => new SolidColorBrush(ToColor(c));

    public static IBrush Diagonal(Rgb from, Rgb to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(ToColor(from), 0), new GradientStop(ToColor(to), 1) },
    };

    public static PathIcon Icon(string name, double size = 11) => new()
    {
        Data = Icons.Get(name),
        Width = size,
        Height = size,
    };
}

/// <summary>Small line icons as path data, drawn the same on every platform.</summary>
public static class Icons
{
    static readonly System.Collections.Generic.Dictionary<string, Geometry> Cache = new();

    public static Geometry Get(string name)
    {
        if (Cache.TryGetValue(name, out var g)) return g;
        g = StreamGeometry.Parse(name switch
        {
            // 16x16 grid, filled shapes so PathIcon can color them.
            "gear" => "M8 5.2A2.8 2.8 0 1 0 8 10.8 2.8 2.8 0 0 0 8 5.2Zm0 1.4a1.4 1.4 0 1 1 0 2.8 1.4 1.4 0 0 1 0-2.8ZM6.9 1h2.2l.4 1.9 1.1.5 1.6-1.1 1.6 1.6-1.1 1.6.5 1.1 1.9.4v2.2l-1.9.4-.5 1.1 1.1 1.6-1.6 1.6-1.6-1.1-1.1.5-.4 1.9H6.9l-.4-1.9-1.1-.5-1.6 1.1-1.6-1.6 1.1-1.6-.5-1.1L1 9.1V6.9l1.9-.4.5-1.1-1.1-1.6 1.6-1.6 1.6 1.1 1.1-.5Zm1 1.3-.3 1.5-2.2.9-1.3-.9-.6.6.9 1.3-.9 2.2-1.5.3v.8l1.5.3.9 2.2-.9 1.3.6.6 1.3-.9 2.2.9.3 1.5h.8l.3-1.5 2.2-.9 1.3.9.6-.6-.9-1.3.9-2.2 1.5-.3v-.8l-1.5-.3-.9-2.2.9-1.3-.6-.6-1.3.9-2.2-.9-.3-1.5Z",
            "close" => "M3.5 2.4 8 6.9l4.5-4.5 1.1 1.1L9.1 8l4.5 4.5-1.1 1.1L8 9.1l-4.5 4.5-1.1-1.1L6.9 8 2.4 3.5Z",
            "pin" => "M10.4 1.3 14.7 5.6 13.6 6.7 12.9 6 10.4 8.5 10.7 11.6 9.6 12.7 7 10.1 3.3 13.8 2.2 12.7 5.9 9 3.3 6.4 4.4 5.3 7.5 5.6 10 3.1 9.3 2.4Z",
            "pinoff" => "M10.4 1.3 14.7 5.6 13.6 6.7 12.9 6 10.4 8.5 10.7 11.6 9.6 12.7 7 10.1 3.3 13.8 2.2 12.7 5.9 9 3.3 6.4 4.4 5.3 7.5 5.6 10 3.1 9.3 2.4ZM10 4.5 8.1 6.4 5.6 6.2 9.8 10.4 9.6 7.9 11.5 6Z",
            "views" => "M1.5 3h13v4h-13Zm1.3 1.3v1.4h10.4V4.3ZM1.5 9h5.5v4H1.5Zm1.3 1.3v1.4h2.9v-1.4ZM11.5 8.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5Zm0 1.3a1.2 1.2 0 1 0 0 2.4 1.2 1.2 0 0 0 0-2.4Z",
            "split" => "M1.5 3.5h5.5v9H1.5Zm1.3 1.3v6.4h2.9V4.8ZM9 3.5h5.5v9H9Zm1.3 1.3v6.4h2.9V4.8Z",
            "combine" => "M1.5 3.5h13v9h-13Zm1.3 1.3v6.4h10.4V4.8ZM7.35 5.5h1.3v5h-1.3Z",
            "hide" => "M2 7.4h12v1.3H2Z",
            "refresh" => "M8 2.5a5.5 5.5 0 0 1 4.6 2.5V3h1.3v4.3H9.6V6h2.2A4.2 4.2 0 1 0 12.2 9h1.3A5.5 5.5 0 1 1 8 2.5Z",
            "check" => "M6.2 10.6 12.6 4.2 13.6 5.2 6.2 12.6 2.4 8.8 3.4 7.8Z",
            "drag" => "M8 1 10.4 3.4 9.5 4.3 8.6 3.4V7.4h4L11.7 6.5l.9-.9L15 8l-2.4 2.4-.9-.9.9-.9H8.6v4l.9-.9.9.9L8 15l-2.4-2.4.9-.9.9.9V8.6h-4l.9.9-.9.9L1 8l2.4-2.4.9.9-.9.9h4V3.4l-.9.9-.9-.9Z",
            "bell" => "M8 1.5a4.5 4.5 0 0 1 4.5 4.5v3l1.5 2.2v.8H2v-.8L3.5 9V6A4.5 4.5 0 0 1 8 1.5Zm0 1.3A3.2 3.2 0 0 0 4.8 6v3.4l-1 1.5h8.4l-1-1.5V6A3.2 3.2 0 0 0 8 2.8ZM6.3 13h3.4a1.7 1.7 0 0 1-3.4 0Z",
            "spark" => "M8 1 9.4 6.6 15 8 9.4 9.4 8 15 6.6 9.4 1 8 6.6 6.6Z",
            "terminal" => "M1.5 2.5h13v11h-13Zm1.3 1.3v8.4h10.4V3.8ZM4 5.6 6.8 8 4 10.4 4.9 11.3 8.7 8 4.9 4.7ZM8.5 10.2h3.5v1.2H8.5Z",
            "shield" => "M8 1 14 3.2v4.3c0 3.6-2.5 6.3-6 7.5C4.5 13.8 2 11.1 2 7.5V3.2Zm0 1.4L3.3 4.1v3.4c0 2.9 1.9 5.1 4.7 6.1 2.8-1 4.7-3.2 4.7-6.1V4.1Z",
            "palette" => "M8 1.5a6.5 6.5 0 0 0 0 13c1 0 1.5-.6 1.5-1.3 0-.9-.8-1.2-.8-2 0-.8.6-1.3 1.4-1.3h1.4a3 3 0 0 0 3-3C14.5 3.9 11.6 1.5 8 1.5ZM4.5 6.8a1 1 0 1 1 0 2 1 1 0 0 1 0-2Zm2-3a1 1 0 1 1 0 2 1 1 0 0 1 0-2Zm3.2 0a1 1 0 1 1 0 2 1 1 0 0 1 0-2Zm2 2.8a1 1 0 1 1 0 2 1 1 0 0 1 0-2Z",
            "info" => "M8 1.5a6.5 6.5 0 1 1 0 13 6.5 6.5 0 0 1 0-13Zm0 1.3a5.2 5.2 0 1 0 0 10.4A5.2 5.2 0 0 0 8 2.8ZM7.35 7h1.3v4.5h-1.3Zm.65-2.6a.85.85 0 1 1 0 1.7.85.85 0 0 1 0-1.7Z",
            "sliders" => "M2 3.5h7v1.3H2Zm10 0h2v1.3h-2ZM9.5 2.5h1.3v3.3H9.5ZM2 7.4h2v1.3H2Zm5 0h7v1.3H7ZM4.5 6.4h1.3v3.3H4.5ZM2 11.3h8v1.3H2Zm11 0h1v1.3h-1Zm-2.5-1h1.3v3.3h-1.3Z",
            "through" => "M3.5 1.5 12.6 9.1 8.5 9.5 10.7 14 9.2 14.7 7 10.2 3.9 13.1Z",
            "power" => "M7.35 1.5h1.3v6.5h-1.3ZM4.3 3.3l.9.9a4.2 4.2 0 1 0 5.6 0l.9-.9a5.5 5.5 0 1 1-7.4 0Z",
            _ => "M0 0",
        });
        Cache[name] = g;
        return g;
    }
}
