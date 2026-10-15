using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ringside.Ui;

/// <summary>The pictures on the welcome cards and in settings, drawn from real controls.</summary>
public static class Art
{
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, SF Mono, DejaVu Sans Mono, monospace");

    /// <summary>A terminal running Claude Code, with Ringside's status line at the bottom.</summary>
    public static Control Terminal(FillMode fill, double width = 470)
    {
        TextBlock Line(string text, IBrush fg, double top = 0) => new()
        {
            Text = text, FontFamily = Mono, FontSize = 11.5, Foreground = fg, Margin = new Thickness(0, top, 0, 0),
        };
        var dim = new SolidColorBrush(Color.Parse("#6E6E78"));
        var prompt = new SolidColorBrush(Color.Parse("#D97757"));
        string status = fill == FillMode.Used ? "5h 42% · resets 2h 14m | 7d 31%" : "5h 58% left · resets 2h 14m | 7d 69% left";
        var statusLine = new Border
        {
            Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#1A8B7CF6")), BorderBrush = new SolidColorBrush(Color.Parse("#558B7CF6")), BorderThickness = new Thickness(1),
            Child = Line(status, new SolidColorBrush(Color.Parse("#D9D3FF"))),
        };
        var body = new StackPanel
        {
            Margin = new Thickness(14, 10, 14, 12),
            Children =
            {
                new TextBlock { FontFamily = Mono, FontSize = 11.5, Inlines = new InlineCollection { new Run("> ") { Foreground = prompt }, new Run("tidy up the release script") { Foreground = Brand.Fg1Brush } } },
                Line("● Read build.ps1", dim, 6),
                Line("● Updated 3 files", dim, 2),
                statusLine,
            },
        };
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 9, 0, 0),
            Children = { Kit.Dot(Color.Parse("#FF5F57"), 9), Kit.Dot(Color.Parse("#FEBC2E"), 9), Kit.Dot(Color.Parse("#28C840"), 9) },
        };
        return new Border
        {
            Width = width, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.Parse("#0E0E12")),
            BorderBrush = new SolidColorBrush(Color.Parse("#22FFFFFF")), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 10 30 0 #80000000"),
            Child = new StackPanel { Children = { dots, body } },
        };
    }

    /// <summary>The three apps Ringside reads, each marked found or not found.</summary>
    public static Control AppTiles(Detection d)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Tile("Claude app", "Usage log", Brand.Diagonal(new Rgb(0xF4, 0xA6, 0x88), new Rgb(0xCF, 0x64, 0x40)), Glyph("spark", 22), d.ClaudeDesktop));
        row.Children.Add(Tile("Claude Code", "Status line", new SolidColorBrush(Color.Parse("#26262E")), Glyph("terminal", 22), d.ClaudeCode));
        row.Children.Add(Tile("Codex", "Session logs", Brand.Diagonal(new Rgb(0x62, 0xE5, 0xBF), new Rgb(0x0F, 0x9E, 0x7B)), Glyph("refresh", 22), d.Codex));
        return row;
    }

    static Control Glyph(string icon, double size) => new PathIcon { Data = Icons.Get(icon), Width = size, Height = size, Foreground = Brushes.White };

    static Control Tile(string name, string what, IBrush iconFill, Control glyph, bool found)
    {
        var icon = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = iconFill,
            BoxShadow = BoxShadows.Parse("0 6 16 0 #66000000"), HorizontalAlignment = HorizontalAlignment.Center, Child = glyph,
        };
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var badgeColor = found ? Color.Parse("#3DD68C") : Color.Parse("#7C7C86");
        var badge = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x26, badgeColor.R, badgeColor.G, badgeColor.B)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children =
                {
                    new PathIcon { Data = Icons.Get(found ? "check" : "close"), Width = 9, Height = 9, Foreground = new SolidColorBrush(badgeColor) },
                    new TextBlock { Text = found ? "Found" : "Not found", FontSize = 10.5, FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(badgeColor) },
                },
            },
        };
        return new Border
        {
            Width = 150, Padding = new Thickness(12, 16, 12, 14), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#12FFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF")), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    icon,
                    new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) },
                    new TextBlock { Text = what, FontSize = 11, Foreground = Brand.Fg3Brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) },
                    badge,
                },
            },
        };
    }

    /// <summary>Four everyday moves, one tile each.</summary>
    public static Control Moves(string trayName)
    {
        Control Move(string icon, string title, string text) => new Border
        {
            Width = 262, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#12FFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF")), BorderThickness = new Thickness(1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Border
                    {
                        Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.Parse("#2E8B7CF6")), VerticalAlignment = VerticalAlignment.Top,
                        Child = new PathIcon { Data = Icons.Get(icon), Width = 14, Height = 14, Foreground = new SolidColorBrush(Color.Parse("#C9C1FF")) },
                    },
                    Col1(new StackPanel
                    {
                        Margin = new Thickness(12, 0, 0, 0),
                        Children =
                        {
                            new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush },
                            new TextBlock { Text = text, FontSize = 11, LineHeight = 15, Foreground = Brand.Fg2Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
                        },
                    }),
                },
            },
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Place(grid, Move("drag", "Drag it anywhere", "It snaps to screen edges and remembers its spot."), 0, 0);
        Place(grid, Move("views", "Double-click", "Switches between Card, Pill and Orb."), 0, 1);
        Place(grid, Move("sliders", "Right-click", "Views, used or left, split, settings."), 1, 0);
        Place(grid, Move("split", "Split and join", "Split the two apart, or drop one onto the other to join them."), 1, 1);
        return grid;

        static Control Col1(Control c) { Grid.SetColumn(c, 1); return c; }
        static void Place(Grid g, Control c, int row, int col)
        {
            c.Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 6, col == 0 ? 6 : 0, row == 0 ? 6 : 0);
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            g.Children.Add(c);
        }
    }

    /// <summary>What an alert looks like.</summary>
    public static Control ToastMock(FillMode fill, Color accent)
    {
        string title = fill == FillMode.Used ? "Claude 5-hour limit at 91%" : "Claude 5-hour: 9% left";
        return new Border
        {
            Width = 300, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(14),
            Background = Kit.Res("CardFill"), BorderBrush = Kit.Res("CardEdge"), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #8C000000"),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent), Margin = new Thickness(0, 2, 12, 2) },
                    Col1(new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush },
                            new TextBlock { Text = "Resets in 48m.", FontSize = 12, Foreground = Brand.Fg2Brush, Margin = new Thickness(0, 3, 0, 0) },
                        },
                    }),
                },
            },
        };

        static Control Col1(Control c) { Grid.SetColumn(c, 1); return c; }
    }

    public static Control Chip(string text) => new Border
    {
        CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4),
        Background = new SolidColorBrush(Color.Parse("#14FFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF")), BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.Medium, Foreground = Brand.Fg1Brush },
    };
}
