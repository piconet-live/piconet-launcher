using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Launcher.App.Controls.NextUi;

/// <summary>
/// The redesigned interface's plate: a rectangle with two parallel corners cut off, the top left and the bottom right
/// (cards, panels, banners); <see cref="OneCut"/>: the top left alone (a panel that stands at the window's edge). It
/// draws its fill in that shape and cuts its content to it; <see cref="Ring"/> is the focus outline, 2 px wide and
/// 2 px off the plate, in the plate's shape; <see cref="Bar"/> a 4 px bar down its left edge (a banner's tier, a
/// racer's paint). Only drawing: a change of any of them lays nothing out again.
/// </summary>
public sealed class Chamfer : Decorator
{
    /// <summary>How far the ring's middle line is outside the plate: a 2 px line, 2 px off it.</summary>
    private const double RingOff = 3, RingWidth = 2, BarWidth = 4;

    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<Chamfer>();

    /// <summary>The length cut off each of the two corners, along either side (6, 8, 10, 14 or 18).</summary>
    public static readonly StyledProperty<double> CutProperty = AvaloniaProperty.Register<Chamfer, double>(nameof(Cut), 10);

    /// <summary>Only the top left corner is cut.</summary>
    public static readonly StyledProperty<bool> OneCutProperty = AvaloniaProperty.Register<Chamfer, bool>(nameof(OneCut));

    public static readonly StyledProperty<IBrush?> RingProperty = AvaloniaProperty.Register<Chamfer, IBrush?>(nameof(Ring));

    public static readonly StyledProperty<IBrush?> BarProperty = AvaloniaProperty.Register<Chamfer, IBrush?>(nameof(Bar));

    static Chamfer()
    {
        AffectsRender<Chamfer>(BackgroundProperty, CutProperty, OneCutProperty, RingProperty, BarProperty);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double Cut
    {
        get => GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    public bool OneCut
    {
        get => GetValue(OneCutProperty);
        set => SetValue(OneCutProperty, value);
    }

    public IBrush? Ring
    {
        get => GetValue(RingProperty);
        set => SetValue(RingProperty, value);
    }

    public IBrush? Bar
    {
        get => GetValue(BarProperty);
        set => SetValue(BarProperty, value);
    }

    /// <summary>The plate's outline in a box: from the top left's cut clockwise. A cut longer than half the shorter
    /// side is held to it, and none (0 or less) leaves the rectangle. <paramref name="oneCut"/>: the bottom right
    /// keeps its corner.</summary>
    public static IReadOnlyList<Point> Outline(Rect box, double cut, bool oneCut = false)
    {
        var c = Math.Clamp(cut, 0, Math.Max(0, Math.Min(box.Width, box.Height) / 2));
        return oneCut
            ? [new Point(box.X + c, box.Y), new Point(box.Right, box.Y), new Point(box.Right, box.Bottom), new Point(box.X, box.Bottom), new Point(box.X, box.Y + c)]
            :
            [
                new Point(box.X + c, box.Y), new Point(box.Right, box.Y), new Point(box.Right, box.Bottom - c),
                new Point(box.Right - c, box.Bottom), new Point(box.X, box.Bottom), new Point(box.X, box.Y + c),
            ];
    }

    /// <summary>The cut of the outline that runs <paramref name="off"/> outside a plate cut by <paramref name="cut"/>,
    /// every side the same distance from the plate's (the cut's line too).</summary>
    public static double CutOutside(double cut, double off) => cut <= 0 ? 0 : cut + (off * (2 - Math.Sqrt(2)));

    private StreamGeometry Shape(Rect box, double cut)
    {
        var points = Outline(box, cut, OneCut);
        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(points[0], isFilled: true);
            for (var i = 1; i < points.Count; i++)
            {
                draw.LineTo(points[i]);
            }
            draw.EndFigure(isClosed: true);
        }
        return geometry;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (Child is { } child)
        {
            // the content is cut to the plate (a picture in its corner never shows past the cut), in its own place
            child.Clip = Shape(new Rect(-child.Bounds.X, -child.Bounds.Y, finalSize.Width, finalSize.Height), Cut);
        }
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CutProperty || change.Property == OneCutProperty)
        {
            InvalidateArrange();
        }
    }

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }
        var plate = Shape(box, Cut);
        if (Background is { } fill)
        {
            context.DrawGeometry(fill, null, plate);
        }
        if (Bar is { } bar)
        {
            using (context.PushGeometryClip(plate))
            {
                context.FillRectangle(bar, new Rect(0, 0, BarWidth, box.Height));
            }
        }
        if (Ring is { } ring)
        {
            context.DrawGeometry(null, new Pen(ring, RingWidth), Shape(box.Inflate(RingOff), CutOutside(Cut, RingOff)));
        }
    }
}
