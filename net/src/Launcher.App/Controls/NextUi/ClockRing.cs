using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.App.Controls.NextUi;

/// <summary>
/// A clock as a ring round its figure (the course vote's): a faint track, and over it what is left of the time in
/// the accent, from the top round with the clock. <see cref="Share"/> is 1 with all of the time left, 0 at
/// its end; the figure in the middle is the control's content. Only drawing: nothing is laid out as it runs.
/// </summary>
public sealed class ClockRing : Decorator
{
    private const double Line = 5;

    public static readonly StyledProperty<double> ShareProperty = AvaloniaProperty.Register<ClockRing, double>(nameof(Share), 1);

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(Fill));

    static ClockRing()
    {
        AffectsRender<ClockRing>(ShareProperty, TrackProperty, StrokeProperty, FillProperty);
    }

    /// <summary>What is left, 1 down to 0.</summary>
    public double Share
    {
        get => GetValue(ShareProperty);
        set => SetValue(ShareProperty, value);
    }

    /// <summary>The whole ring, faint.</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>The part of the ring that is left.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The disc inside the ring, under the figure.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The angle the part that is left covers, in degrees, for a share.</summary>
    public static double Sweep(double share) => 360 * (double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Line * 2)
        {
            return;
        }
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Line) / 2;
        context.DrawEllipse(Fill, Track is { } track ? new Pen(track, Line) : null, centre, radius, radius);
        var sweep = Sweep(Share);
        if (Stroke is not { } stroke || sweep <= 0)
        {
            return;
        }
        var pen = new Pen(stroke, Line);
        if (sweep >= 359.9)
        {
            context.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }
        // from the top round with the clock: its end comes back towards the top as the time goes
        var end = (-90 + sweep) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(centre.X, centre.Y - radius), isFilled: false);
            draw.ArcTo(new Point(centre.X + (radius * Math.Cos(end)), centre.Y + (radius * Math.Sin(end))), new Size(radius, radius), 0,
                sweep > 180, SweepDirection.Clockwise);
            draw.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
