using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ringside.Ui;

/// <summary>
/// The meter itself, in any of its three views. The live widget, the settings preview, the
/// onboarding cards and the README renders all draw through this one control.
/// </summary>
public sealed class WidgetView : UserControl
{
    public const double Gutter = 18; // room for the soft shadow around the card

    Settings _s = new();
    IReadOnlyList<string> _providers = new[] { "Claude", "Codex" };
    readonly bool _animate;
    Reading? _last;

    readonly LayoutTransformControl _scaler = new() { ClipToBounds = false };
    readonly Border _card = new();
    readonly Grid _layer = new();
    readonly Border _controls;
    readonly List<IPart> _parts = new();
    Button? _splitButton, _pinButton;

    public event Action? SettingsRequested;
    public event Action? CycleViewRequested;
    public event Action? SplitToggleRequested;
    public event Action? PinToggleRequested;
    public event Action? HideRequested;

    /// <summary>First-run tip under the card; hidden once the user has moved or clicked it.</summary>
    public bool ShowHint { get; set; }

    /// <summary>The hover buttons on the card; off for previews and still renders.</summary>
    public bool Interactive { get; init; } = true;

    public ViewKind View => _s.View;

    public WidgetView(bool animate = true)
    {
        _animate = animate;
        _card.BorderThickness = new Thickness(1);
        _card.Background = (IBrush)Application.Current!.FindResource("CardFill")!;
        _card.BorderBrush = (IBrush)Application.Current!.FindResource("CardEdge")!;
        _card.BoxShadow = BoxShadows.Parse("0 7 26 0 #80000000");
        _card.Child = _layer;
        _scaler.Child = _card;
        _scaler.Margin = new Thickness(Gutter);
        // The card keeps its own size even when a parent stretches the view.
        _scaler.HorizontalAlignment = HorizontalAlignment.Center;
        _scaler.VerticalAlignment = VerticalAlignment.Center;
        Content = _scaler;

        _controls = BuildControls();
    }

    public void Configure(Settings settings, IReadOnlyList<string> providers)
    {
        _s = settings;
        _providers = providers.Where(settings.Shows).ToList();
        if (_providers.Count == 0) _providers = providers.ToList();
        Rebuild();
        if (_last is not null) Show(_last);
    }

    public void Show(Reading r)
    {
        _last = r;
        foreach (var p in _parts) p.Update(r, _s);
    }

    public void ShowControls(bool on)
    {
        bool want = on && Interactive && _s.View == ViewKind.Card;
        _controls.IsHitTestVisible = want;
        _controls.Opacity = want ? 1 : 0;
    }

    // ---- layout -----------------------------------------------------------------------

    void Rebuild()
    {
        _parts.Clear();
        _layer.Children.Clear();
        double scale = _s.Scale;
        _scaler.LayoutTransform = Math.Abs(scale - 1) < 0.01 ? null : new ScaleTransform(scale, scale);

        Control content = _s.View switch
        {
            ViewKind.Pill => BuildPill(),
            ViewKind.Orb => BuildOrb(),
            _ => BuildCard(),
        };
        _layer.Children.Add(content);
        if (Interactive && _s.View == ViewKind.Card)
        {
            _layer.Children.Add(_controls);
            _splitButton!.IsVisible = _s.ShowClaude && _s.ShowCodex;
            ((PathIcon)_splitButton.Content!).Data = Icons.Get(_s.Split ? "combine" : "split");
            ToolTip.SetTip(_splitButton, _s.Split ? "Combine into one" : "Split apart");
            ((PathIcon)_pinButton!.Content!).Data = Icons.Get(_s.Topmost ? "pin" : "pinoff");
            ToolTip.SetTip(_pinButton, _s.Topmost ? "Stays on top (click to release)" : "Keep on top");
        }
        ShowControls(false);
    }

    Control BuildCard()
    {
        _card.CornerRadius = new CornerRadius(16);
        _card.Padding = new Thickness(16, 14, 16, 14);
        var stack = new StackPanel { Width = 262 };
        for (int i = 0; i < _providers.Count; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Background = Brush("Hairline"), Margin = new Thickness(0, 13) });
            var row = new CardRow(_providers[i], _s.Gauge, _animate);
            _parts.Add(row);
            stack.Children.Add(row.Root);
        }
        if (ShowHint)
            stack.Children.Add(new TextBlock
            {
                Text = "Drag to move. Double-click to switch view.\nRight-click for views, colors and more.",
                FontSize = 10.5, LineHeight = 15, Foreground = Brand.Fg3Brush, Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        return stack;
    }

    Control BuildPill()
    {
        _card.CornerRadius = new CornerRadius(19);
        _card.Padding = new Thickness(11, 8, 14, 8);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var p in _providers)
        {
            var item = new PillItem(p, _animate);
            _parts.Add(item);
            row.Children.Add(item.Root);
        }
        return row;
    }

    Control BuildOrb()
    {
        var orb = new OrbPart(_providers, _animate);
        _parts.Add(orb);
        double d = orb.Diameter + 20;
        _card.CornerRadius = new CornerRadius(d / 2);
        _card.Padding = new Thickness(10);
        return orb.Root;
    }

    Border BuildControls()
    {
        Button Icon(string name, string tip, Action click)
        {
            var b = new Button { Classes = { "icon" }, Content = Brand.Icon(name) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, e) => { click(); e.Handled = true; };
            return b;
        }
        _splitButton = Icon("split", "Split apart", () => SplitToggleRequested?.Invoke());
        _pinButton = Icon("pin", "Keep on top", () => PinToggleRequested?.Invoke());
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -8, 0),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#1E1E23")),
            BoxShadow = BoxShadows.Parse("0 2 8 0 #66000000"),
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    Icon("gear", "Settings", () => SettingsRequested?.Invoke()),
                    Icon("views", "Switch view (or double-click)", () => CycleViewRequested?.Invoke()),
                    _splitButton,
                    _pinButton,
                    Icon("hide", "Hide (the tray icon brings it back)", () => HideRequested?.Invoke()),
                },
            },
        };
    }

    static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;

    // ---- the pieces --------------------------------------------------------------------

    interface IPart
    {
        void Update(Reading r, Settings s);
    }

    // Above the meter (below near the top of the screen), never under the pointer: a tooltip
    // window there would catch the clicks meant for the meter.
    static void Tip(Control c, string text)
    {
        ToolTip.SetTip(c, text);
        ToolTip.SetPlacement(c, PlacementMode.Top);
        ToolTip.SetVerticalOffset(c, -6);
        ToolTip.SetShowDelay(c, 650);
    }

    static string Tooltip(Snapshot snap, Reading r, Settings s)
    {
        var sb = new StringBuilder();
        sb.Append(snap.Provider);
        if (snap.Plan is { } plan) sb.Append(" (").Append(plan).Append(')');
        sb.Append('\n');
        if (snap.Windows.Count == 0) sb.Append(snap.Note ?? "No data yet").Append('\n');
        foreach (var w in snap.Windows) sb.Append(Display.Line(w, r.Now, s)).Append('\n');
        if (s.Forecast && Display.Forecast(r.RunsOutFor(snap.Provider), r.Now, s.Fill) is { Length: > 0 } f) sb.Append(f).Append('\n');
        if (snap.AsOf is { } at) sb.Append("Updated ").Append(Fmt.Age(at, r.Now)).Append('\n');
        if (snap.Source.Length > 0) sb.Append('\n').Append(snap.Source);
        return sb.ToString().TrimEnd();
    }

    static Color SolidColor(string provider, double used, Settings s) => Brand.ToColor(Palette.Solid(provider, used, s));

    static Color BaseColor(string provider, Settings s) =>
        Brand.ToColor(Palette.Parse(s.ColorOf(provider), provider == "Codex" ? Palette.CodexDefault : Palette.ClaudeDefault));

    static Avalonia.Media.IEffect? Glow(Level level) => level == Level.Alert
        ? new DropShadowEffect { Color = Brand.ToColor(Palette.Red), BlurRadius = 14, OffsetX = 0, OffsetY = 0, Opacity = 0.55 }
        : null;

    /// <summary>One app's full readout in the card: a ring or a bar, then the details.</summary>
    sealed class CardRow : IPart
    {
        readonly string _p;
        readonly GaugeStyle _style;
        public readonly Control Root;
        readonly GaugeBase _gauge;
        readonly Run _num = new() { FontWeight = FontWeight.SemiBold };
        readonly Run _sign = new() { FontWeight = FontWeight.Medium };
        readonly TextBlock _word = new() { FontSize = 8.5, Foreground = Brand.Fg3Brush, HorizontalAlignment = HorizontalAlignment.Center, LetterSpacing = 0.4 };
        readonly Ellipse _dot = new() { Width = 6, Height = 6, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _age = new() { FontSize = 10.5, Foreground = Brand.Fg3Brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _planText = new() { FontSize = 9.5, FontWeight = FontWeight.Medium, Foreground = Brand.Fg2Brush };
        readonly Border _plan;
        readonly TextBlock _primary = new() { FontSize = 11, Foreground = Brand.Fg2Brush, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly StackPanel _bars = new();
        readonly StackPanel _forecast;
        readonly TextBlock _forecastText = new() { FontSize = 10.5, Foreground = new SolidColorBrush(Color.Parse("#F3C77A")), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _note = new() { FontSize = 10.5, Foreground = Brand.Fg3Brush, Margin = new Thickness(0, 4, 0, 0) };
        readonly Control _gaugeHost;
        readonly Control _detailHost;

        public CardRow(string provider, GaugeStyle style, bool animate)
        {
            _p = provider;
            _style = style;
            _plan = new Border
            {
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#2BFFFFFF")),
                Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = _planText, IsVisible = false,
            };
            var header = new Grid
            {
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                        Children = { _dot, new TextBlock { Text = provider, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush, VerticalAlignment = VerticalAlignment.Center }, _plan },
                    },
                    _age,
                },
            };
            _forecast = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 5, 0, 0), IsVisible = false,
                Children = { new PathIcon { Data = Icons.Get("spark"), Width = 9, Height = 9, Foreground = new SolidColorBrush(Color.Parse("#F3C77A")) }, _forecastText },
            };

            if (style == GaugeStyle.Bar)
            {
                _num.FontSize = 19;
                _sign.FontSize = 11;
                var bar = new BarGauge { Thickness = 8, Height = 12, VerticalAlignment = VerticalAlignment.Center, Animate = animate };
                _gauge = bar;
                _word.FontSize = 10;
                _word.Margin = new Thickness(4, 0, 0, 2);
                _word.VerticalAlignment = VerticalAlignment.Bottom;
                var value = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Children = { new TextBlock { Inlines = new InlineCollection { _num, _sign }, Foreground = Brand.Fg1Brush }, _word },
                };
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
                Grid.SetColumn(value, 1);
                line.Children.Add(bar);
                line.Children.Add(value);
                _gaugeHost = line;
                _detailHost = new StackPanel { Children = { _primary, _bars, _forecast, _note } };
                Root = new StackPanel { Children = { header, line, _detailHost } };
                _sign.Foreground = Brand.Fg2Brush;
            }
            else
            {
                _num.FontSize = 15;
                _sign.FontSize = 9.5;
                _sign.Foreground = Brand.Fg2Brush;
                var ring = new RingGauge { Thickness = 5, Animate = animate };
                _gauge = ring;
                var center = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1, 1, 0, 0),
                    Children =
                    {
                        new TextBlock { Inlines = new InlineCollection { _num, _sign }, Foreground = Brand.Fg1Brush, HorizontalAlignment = HorizontalAlignment.Center },
                        _word,
                    },
                };
                _word.Margin = new Thickness(0, -2, 0, 0);
                var host = new Grid { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Children = { ring, center } };
                _gaugeHost = host;
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { header, _primary, _bars, _forecast, _note } };
                _detailHost = right;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,14,*") };
                Grid.SetColumn(right, 2);
                grid.Children.Add(host);
                grid.Children.Add(right);
                Root = grid;
            }
        }

        public void Update(Reading r, Settings s)
        {
            var snap = r.Of(_p);
            _dot.Fill = new SolidColorBrush(BaseColor(_p, s));
            _planText.Text = snap.Plan ?? "";
            _plan.IsVisible = snap.Plan is not null;
            bool stale = snap.IsStale(r.Now);
            _age.Text = snap.AsOf is { } t ? (stale ? Fmt.AsOf(t, r.Now) : Fmt.Age(t, r.Now)) : "";
            _gaugeHost.Opacity = stale ? 0.5 : 1;
            _detailHost.Opacity = stale ? 0.75 : 1;
            Tip(Root, Tooltip(snap, r, s));
            _bars.Children.Clear();

            var p = snap.Pick(s.Focus);
            if (p is null || p.Lapsed)
            {
                _num.Text = "-";
                _sign.Text = "";
                _word.Text = "";
                _gauge.Value = 0;
                _gauge.Effect = null;
                _primary.Text = p is { Lapsed: true } ? $"{p.Label} reset since the last reading" : snap.Note ?? "No data yet";
                _note.Text = p is { Lapsed: true } ? snap.Note ?? "" : "";
                _note.IsVisible = _note.Text.Length > 0;
                _forecast.IsVisible = false;
                return;
            }

            var level = Display.LevelOf(p.Percent, s);
            _num.Text = Display.Number(p.Percent, s.Fill);
            _sign.Text = "%";
            _word.Text = Display.Word(s.Fill);
            _num.Foreground = level == Level.Alert ? Brand.Solid(Palette.Red) : Brand.Fg1Brush;
            _gauge.SetColors(Palette.For(_p, p.Percent, s));
            _gauge.Effect = Glow(level);
            _gauge.Value = Display.Shown(p.Percent, s.Fill);

            string reset = Fmt.Reset(p, r.Now, s.Resets);
            _primary.Text = reset.Length == 0 ? p.Label : $"{p.Label} · {reset}";

            foreach (var w in snap.Windows)
                if (!ReferenceEquals(w, p)) _bars.Children.Add(BarRow(w, r, s));

            string forecast = s.Forecast ? Display.Forecast(r.RunsOutFor(_p), r.Now, s.Fill) : "";
            _forecastText.Text = forecast;
            _forecast.IsVisible = forecast.Length > 0;

            _note.Text = snap.Note ?? "";
            _note.IsVisible = !string.IsNullOrEmpty(snap.Note);
        }

        Control BarRow(LimitWindow w, Reading r, Settings s)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            var label = new TextBlock { Text = w.Label, FontSize = 10.5, Foreground = Brand.Fg2Brush, VerticalAlignment = VerticalAlignment.Center };
            var bar = new BarGauge { Thickness = 4, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.SetColors(Palette.For(_p, w.Percent, s));
            bar.Value = Display.Shown(w.Percent, s.Fill);
            var value = new TextBlock
            {
                Text = Display.Number(w.Percent, s.Fill) + "%", FontSize = 10.5, Foreground = Brand.Fg1Brush,
                MinWidth = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(value, 2);
            g.Children.Add(label);
            g.Children.Add(bar);
            g.Children.Add(value);
            Tip(g, Display.Line(w, r.Now, s));
            return g;
        }
    }

    /// <summary>One app in the slim pill: a small ring and its number.</summary>
    sealed class PillItem : IPart
    {
        readonly string _p;
        public readonly Control Root;
        readonly RingGauge _ring;
        readonly Run _name, _num, _sign, _word;

        public PillItem(string provider, bool animate)
        {
            _p = provider;
            _ring = new RingGauge { Width = 18, Height = 18, Thickness = 3, VerticalAlignment = VerticalAlignment.Center, Animate = animate };
            _name = new Run(provider + " ") { FontSize = 11, Foreground = Brand.Fg2Brush };
            _num = new Run("-") { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brand.Fg1Brush };
            _sign = new Run("") { FontSize = 11, Foreground = Brand.Fg1Brush };
            _word = new Run("") { FontSize = 10.5, Foreground = Brand.Fg3Brush };
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), Inlines = new InlineCollection { _name, _num, _sign, _word } };
            Root = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { _ring, text } };
        }

        public void Update(Reading r, Settings s)
        {
            var snap = r.Of(_p);
            var p = snap.Pick(s.Focus);
            Tip(Root, Tooltip(snap, r, s));
            Root.Opacity = snap.IsStale(r.Now) ? 0.6 : 1;
            if (p is null || p.Lapsed)
            {
                _num.Text = "-";
                _sign.Text = _word.Text = "";
                _ring.Value = 0;
                return;
            }
            var level = Display.LevelOf(p.Percent, s);
            _num.Text = Display.Number(p.Percent, s.Fill);
            _sign.Text = "%";
            _word.Text = s.Fill == FillMode.Left ? " left" : "";
            _num.Foreground = level == Level.Alert ? Brand.Solid(Palette.Red) : Brand.Fg1Brush;
            _ring.SetColors(Palette.For(_p, p.Percent, s));
            _ring.Value = Display.Shown(p.Percent, s.Fill);
        }
    }

    /// <summary>The logo view: Claude's ring outside, Codex's ring inside, numbers in the middle.</summary>
    sealed class OrbPart : IPart
    {
        readonly IReadOnlyList<string> _providers;
        public readonly Control Root;
        public readonly double Diameter;
        readonly List<RingGauge> _rings = new();
        readonly List<Run> _nums = new();
        readonly List<TextBlock> _lines = new();
        readonly TextBlock _word = new() { FontSize = 8.5, Foreground = Brand.Fg3Brush, HorizontalAlignment = HorizontalAlignment.Center, LetterSpacing = 0.5, Margin = new Thickness(0, 1, 0, 0) };
        readonly TextBlock? _name;

        public OrbPart(IReadOnlyList<string> providers, bool animate)
        {
            _providers = providers;
            bool both = providers.Count > 1;
            Diameter = both ? 132 : 100;
            const double thick = 10, gap = 6;
            var grid = new Grid { Width = Diameter, Height = Diameter, Background = Brushes.Transparent };
            for (int i = 0; i < providers.Count; i++)
            {
                double inset = i * (thick + gap);
                var ring = new RingGauge { Thickness = thick, Margin = new Thickness(inset), Animate = animate };
                _rings.Add(ring);
                grid.Children.Add(ring);
            }

            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var _ in providers)
            {
                var num = new Run("-") { FontSize = both ? 15 : 24, FontWeight = FontWeight.SemiBold };
                var sign = new Run("%") { FontSize = both ? 9.5 : 13, FontWeight = FontWeight.Medium };
                var tb = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Inlines = new InlineCollection { num, sign }, LineHeight = both ? 17 : 26 };
                _nums.Add(num);
                _lines.Add(tb);
                center.Children.Add(tb);
            }
            center.Children.Add(_word);
            if (!both)
            {
                _name = new TextBlock { Text = providers[0], FontSize = 9, Foreground = Brand.Fg2Brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
                center.Children.Insert(0, _name);
            }
            grid.Children.Add(center);
            Root = grid;
        }

        public void Update(Reading r, Settings s)
        {
            var tips = new List<string>();
            bool anyStale = false;
            for (int i = 0; i < _providers.Count; i++)
            {
                var snap = r.Of(_providers[i]);
                var p = snap.Pick(s.Focus);
                anyStale |= snap.IsStale(r.Now);
                tips.Add(Tooltip(snap, r, s));
                var color = p is null ? BaseColor(_providers[i], s) : SolidColor(_providers[i], p.Percent, s);
                _lines[i].Foreground = new SolidColorBrush(_providers.Count > 1 ? color : Brand.Fg1);
                if (_providers.Count == 1 && _name is not null) _name.Foreground = new SolidColorBrush(BaseColor(_providers[i], s));
                if (p is null || p.Lapsed)
                {
                    _nums[i].Text = "-";
                    _rings[i].Value = 0;
                    _rings[i].Effect = null;
                    continue;
                }
                _nums[i].Text = Display.Number(p.Percent, s.Fill);
                _rings[i].SetColors(Palette.For(_providers[i], p.Percent, s));
                _rings[i].Effect = Glow(Display.LevelOf(p.Percent, s));
                _rings[i].Value = Display.Shown(p.Percent, s.Fill);
            }
            _word.Text = Display.Word(s.Fill).ToUpperInvariant();
            Root.Opacity = anyStale ? 0.75 : 1;
            Tip(Root, string.Join("\n\n", tips));
        }
    }
}
