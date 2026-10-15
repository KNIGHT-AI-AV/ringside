using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ringside.Ui;

/// <summary>Small builders so the settings, onboarding and quick panel read like a layout.</summary>
public static class Kit
{
    public static TextBlock Text(string text, string cls, double? top = null)
    {
        var t = new TextBlock { Text = text, Classes = { cls } };
        if (top is double m) t.Margin = new Thickness(0, m, 0, 0);
        return t;
    }

    public static TextBlock Section(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "section" } };

    /// <summary>A label (and optional hint) with a switch on the right.</summary>
    public static CheckBox Switch(string label, string? hint, bool value, Action<bool> changed)
    {
        Control content = hint is null
            ? new TextBlock { Text = label, Classes = { "label" } }
            : new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Classes = { "label" } },
                    new TextBlock { Text = hint, Classes = { "hint" }, Margin = new Thickness(0, 2, 0, 0) },
                },
            };
        var cb = new CheckBox { Classes = { "switch" }, Content = content, IsChecked = value };
        cb.IsCheckedChanged += (_, _) => changed(cb.IsChecked == true);
        return cb;
    }

    /// <summary>A row of mutually exclusive choices drawn as one pill.</summary>
    public static Border Segments<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> changed, bool fill = false)
    {
        string group = "seg" + Guid.NewGuid().ToString("N");
        Panel panel = fill
            ? new Avalonia.Controls.Primitives.UniformGrid { Columns = options.Count, Rows = 1 }
            : new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in options)
        {
            var rb = new RadioButton { Classes = { "segment" }, Content = label, GroupName = group, IsChecked = Equals(value, selected), HorizontalAlignment = HorizontalAlignment.Stretch };
            var v = value;
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) changed(v); };
            panel.Children.Add(rb);
        }
        return new Border { Classes = { "segments" }, Child = panel, HorizontalAlignment = fill ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
    }

    /// <summary>A labelled row: text on the left, any control on the right.</summary>
    public static Grid Row(string label, Control right, string? hint = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6) };
        Control left = hint is null
            ? new TextBlock { Text = label, Classes = { "label" } }
            : new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = label, Classes = { "label" } },
                    new TextBlock { Text = hint, Classes = { "hint" }, Margin = new Thickness(0, 2, 12, 0) },
                },
            };
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    /// <summary>The color choices for one app, plus a box for any #RRGGBB.</summary>
    public static Control Swatches(string current, Action<string> changed)
    {
        string group = "sw" + Guid.NewGuid().ToString("N");
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var hex = new TextBox { Classes = { "hex" }, Text = current.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 8) };
        var buttons = new List<(RadioButton Button, string Hex)>();
        bool syncing = false;
        foreach (var (name, h) in Palette.Swatches)
        {
            var rb = new RadioButton { Classes = { "swatch" }, GroupName = group, Background = new SolidColorBrush(Color.Parse(h)) };
            ToolTip.SetTip(rb, name);
            var hexValue = h;
            rb.IsChecked = string.Equals(h, current, StringComparison.OrdinalIgnoreCase);
            rb.IsCheckedChanged += (_, _) =>
            {
                if (rb.IsChecked != true || syncing) return;
                syncing = true;
                hex.Text = hexValue;
                syncing = false;
                changed(hexValue);
            };
            buttons.Add((rb, h));
            wrap.Children.Add(rb);
        }
        hex.TextChanged += (_, _) =>
        {
            if (syncing || !Palette.TryParse(hex.Text, out var rgb)) return;
            syncing = true;
            foreach (var (b, h) in buttons) b.IsChecked = string.Equals(h, rgb.Hex, StringComparison.OrdinalIgnoreCase);
            syncing = false;
            changed(rgb.Hex);
        };
        wrap.Children.Add(hex);
        return wrap;
    }

    /// <summary>The Ringside mark: two rings, like the app icon.</summary>
    public static Control Logo(double size, double claude = 72, double codex = 46)
    {
        double t = Math.Max(2.5, size / 9);
        var outer = new RingGauge { Width = size, Height = size, Thickness = t, Value = claude, Animate = false };
        outer.SetColors(Palette.Gradient(Palette.Parse(Palette.ClaudeDefault, "")));
        double inner = size - 2 * (t + Math.Max(1.5, size / 14));
        var innerRing = new RingGauge { Width = inner, Height = inner, Thickness = t, Value = codex, Animate = false };
        innerRing.SetColors(Palette.Gradient(Palette.Parse(Palette.CodexDefault, "")));
        return new Grid { Width = size, Height = size, Children = { outer, innerRing } };
    }

    /// <summary>A soft stage with a violet glow for hero art and previews.</summary>
    public static Border Stage(Control child, double height, double radius = 16)
    {
        var glow = new Border { Background = Res("StageGlow"), CornerRadius = new CornerRadius(radius) };
        var grid = new Grid { Children = { glow, child } };
        return new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = Res("StageFill"),
            BorderBrush = new SolidColorBrush(Color.Parse("#14FFFFFF")),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = grid,
        };
    }

    public static IBrush Res(string key) => (IBrush)Application.Current!.FindResource(key)!;

    public static Button Primary(string text, Action click)
    {
        var b = new Button { Classes = { "primary" }, Content = text };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Ghost(string text, Action click)
    {
        var b = new Button { Classes = { "ghost" }, Content = text };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Link(string text, Action click)
    {
        var b = new Button { Classes = { "link" }, Content = text };
        b.Click += (_, _) => click();
        return b;
    }

    public static Ellipse Dot(Color c, double size = 7) => new() { Width = size, Height = size, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center };
}
