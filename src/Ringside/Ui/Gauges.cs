using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ringside.Ui;

/// <summary>Shared look for the gauges: a two-color gradient and an optional glow.</summary>
public abstract class GaugeBase : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeBase, double>(nameof(Value));
    public static readonly StyledProperty<Color> FromProperty =
        AvaloniaProperty.Register<GaugeBase, Color>(nameof(From), Colors.White);
    public static readonly StyledProperty<Color> ToProperty =
        AvaloniaProperty.Register<GaugeBase, Color>(nameof(To), Colors.White);
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<GaugeBase, IBrush?>(nameof(Track), new SolidColorBrush(Color.Parse("#1EFFFFFF")));
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<GaugeBase, double>(nameof(Thickness), 5);

    static GaugeBase()
    {
        AffectsRender<GaugeBase>(ValueProperty, FromProperty, ToProperty, TrackProperty, ThicknessProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color From { get => GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public Color To { get => GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Glides to new values; off for still renders, which must show the final value.</summary>
    public bool Animate
    {
        get => Transitions is { Count: > 0 };
        set => Transitions = value
            ? new Transitions { new DoubleTransition { Property = ValueProperty, Duration = TimeSpan.FromMilliseconds(650), Easing = new CubicEaseOut() } }
            : null;
    }

    public void SetColors((Rgb From, Rgb To) c)
    {
        From = Brand.ToColor(c.From);
        To = Brand.ToColor(c.To);
    }

    protected IBrush Gradient(Point a, Point b) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(a, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(b, RelativeUnit.Absolute),
        GradientStops = { new GradientStop(From, 0), new GradientStop(To, 1) },
    };
}

/// <summary>A round gauge drawn as one arc from 12 o'clock, clockwise.</summary>
public sealed class RingGauge : GaugeBase
{
    public override void Render(DrawingContext ctx)
    {
        double t = Thickness;
        double r = (Math.Min(Bounds.Width, Bounds.Height) - t) / 2;
        if (r <= 0) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Track is { } track) ctx.DrawEllipse(null, new Pen(track, t), c, r, r);

        double v = Math.Clamp(Value, 0, 100);
        if (v < 0.05) return;
        var pen = new Pen(Gradient(new Point(c.X - r, c.Y - r), new Point(c.X + r, c.Y + r)), t, lineCap: PenLineCap.Round);
        if (v > 99.95) { ctx.DrawEllipse(null, pen, c, r, r); return; }

        double a = v / 100 * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(c.X, c.Y - r), false);
            s.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise);
            s.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }
}

/// <summary>A horizontal gauge: a rounded track with a gradient fill from the left.</summary>
public sealed class BarGauge : GaugeBase
{
    public override void Render(DrawingContext ctx)
    {
        double h = Math.Min(Thickness, Bounds.Height), w = Bounds.Width;
        if (w <= 0 || h <= 0) return;
        double y = (Bounds.Height - h) / 2, rad = h / 2;
        if (Track is { } track) ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, y, w, h), rad));
        double v = Math.Clamp(Value, 0, 100);
        if (v < 0.05) return;
        double fw = Math.Max(h, w * v / 100);
        ctx.DrawRectangle(Gradient(new Point(0, 0), new Point(w, 0)), null, new RoundedRect(new Rect(0, y, fw, h), rad));
    }
}
