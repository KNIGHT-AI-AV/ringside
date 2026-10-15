using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Ringside;

public partial class ProviderView : UserControl
{
    Brush _accent = Brushes.White;
    double _target = -1;
    bool _first = true;

    public ProviderView() => InitializeComponent();

    /// <summary>False for still renders, where the ring must be drawn at its final value.</summary>
    public bool Animate { get; set; } = true;

    public void Configure(string name, Color color, Brush accent)
    {
        NameText.Text = name;
        Dot.Fill = new SolidColorBrush(color);
        _accent = accent;
    }

    public void Show(Snapshot s, DateTimeOffset now)
    {
        PlanText.Text = s.Plan ?? "";
        PlanChip.Visibility = s.Plan is null ? Visibility.Collapsed : Visibility.Visible;

        bool stale = s.IsStale(now);
        AgeText.Text = s.AsOf is { } t ? (stale ? "as of " + Fmt.Clock(t) : Fmt.Age(t, now)) : "";
        RingHost.Opacity = stale ? 0.5 : 1;
        PrimaryText.Opacity = stale ? 0.7 : 1;

        Bars.Children.Clear();
        var p = s.Primary;
        if (p is null)
        {
            PctRun.Text = "-";
            PctSign.Text = "";
            ShowValue(0);
            PrimaryText.Text = s.Note ?? "No data yet";
            NoteText.Visibility = Visibility.Collapsed;
            RingCtl.Effect = null;
            RingHost.ToolTip = Tip(s.Source);
            return;
        }

        bool alert = p.Percent >= Theme.AlertAt;
        PctRun.Text = Fmt.Pct(p.Percent);
        PctSign.Text = "%";
        PctRun.Foreground = alert ? new SolidColorBrush(Theme.RedColor) : (Brush)FindResource("Fg1");
        RingCtl.Stroke = Theme.State(p.Percent, _accent);
        RingCtl.Effect = alert
            ? new DropShadowEffect { Color = Theme.RedColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.55 }
            : null;
        ShowValue(p.Percent);

        string reset = Fmt.Reset(p, now);
        PrimaryText.Text = reset.Length == 0 ? p.Label : $"{p.Label} · {reset}";

        foreach (var w in s.Windows)
            if (!ReferenceEquals(w, p)) Bars.Children.Add(BarRow(w, now));

        NoteText.Text = s.Note ?? "";
        NoteText.Visibility = string.IsNullOrEmpty(s.Note) ? Visibility.Collapsed : Visibility.Visible;

        var lines = new StringBuilder();
        foreach (var w in s.Windows)
        {
            string r = Fmt.Reset(w, now);
            lines.Append($"{w.Label}: {Fmt.Pct(w.Percent)}% used{(r.Length > 0 ? ", " + r : "")}\n");
        }
        if (s.AsOf is { } at) lines.Append($"Updated {Fmt.Age(at, now)}\n");
        lines.Append('\n').Append(s.Source);
        RingHost.ToolTip = Tip(lines.ToString());
    }

    static TextBlock Tip(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 16 };

    void ShowValue(double v)
    {
        v = Math.Clamp(v, 0, 100);
        if (Math.Abs(v - _target) < 0.01) return;
        _target = v;
        if (!Animate)
        {
            RingCtl.BeginAnimation(Ring.ValueProperty, null);
            RingCtl.Value = v;
            return;
        }
        var anim = new DoubleAnimation(v, TimeSpan.FromMilliseconds(_first ? 900 : 600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        _first = false;
        RingCtl.BeginAnimation(Ring.ValueProperty, anim);
    }

    FrameworkElement BarRow(LimitWindow w, DateTimeOffset now)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

        var label = new TextBlock
        {
            Text = w.Label, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Fg2"),
        };

        double pct = Math.Clamp(w.Percent, 0, 100);
        var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("TrackBrush") });
        var fillHost = new Grid();
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
        if (pct > 0)
            fillHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(2), MinWidth = 4,
                Background = Theme.State(w.Percent, _accent),
            });
        track.Children.Add(fillHost);

        var value = new TextBlock
        {
            Text = Fmt.Pct(w.Percent) + "%", FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Fg1"),
        };
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);

        Grid.SetColumn(track, 1);
        Grid.SetColumn(value, 2);
        g.Children.Add(label);
        g.Children.Add(track);
        g.Children.Add(value);

        string reset = Fmt.Reset(w, now);
        if (reset.Length > 0) g.ToolTip = $"{w.Label} · {reset}";
        return g;
    }
}
