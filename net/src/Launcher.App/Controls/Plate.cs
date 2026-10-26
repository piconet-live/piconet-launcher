using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Launcher.App.Controls;

/// <summary>How a <see cref="Plate"/> is cut.</summary>
public enum PlateShape
{
    /// <summary>A parallelogram leaning forward (the active tab, the primary button).</summary>
    Lean,

    /// <summary>Square on the left, its right edge leaning forward (the hero's title plate).</summary>
    LeanRight,

    /// <summary>A rectangle with its top left corner cut (the rules panel).</summary>
    Chamfer,
}

/// <summary>
/// A filled surface in direction D's one non-square shape (Theme.axaml, CORNERS): a parallelogram that leans about 15
/// degrees, a right edge that leans, or one chamfered corner. The shape is drawn from the plate's own size at every
/// render, so it follows the interface scale (the transform at the window's root) and any width; its child is laid
/// out as a decorator's, inside <see cref="Decorator.Padding"/>.
/// <para>The pointer finds the plate by its box (<see cref="HitTest"/>), never by what it last drew: the toolkit
/// hit-tests a drawn geometry only once the render thread has taken it in, and the plate draws a new one at every
/// render, so while its colour eased (a button's hover, one tone up) the fill was out of the pointer's reach for a
/// moment each frame: the button lost the pointer, eased back, found it again, and a press on the fill could fall in
/// such a gap; only the word, drawn as text, held it (seen on the RACE button).</para>
/// </summary>
public sealed class Plate : Decorator, ICustomHitTest
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<Plate, IBrush?>(nameof(Background));

    public static readonly StyledProperty<PlateShape> ShapeProperty =
        AvaloniaProperty.Register<Plate, PlateShape>(nameof(Shape));

    /// <summary>The cut in px: how far a lean runs across the plate's height, or the chamfer's leg. Unset (NaN): a
    /// lean of tan 15 degrees times the height, a chamfer of 24.</summary>
    public static readonly StyledProperty<double> CutProperty =
        AvaloniaProperty.Register<Plate, double>(nameof(Cut), double.NaN);

    static Plate() => AffectsRender<Plate>(BackgroundProperty, ShapeProperty, CutProperty);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public PlateShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double Cut
    {
        get => GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (w, h) = (Bounds.Width, Bounds.Height);
        if (Background is not { } fill || w <= 0 || h <= 0)
        {
            return;
        }
        var cut = double.IsNaN(Cut) ? (Shape == PlateShape.Chamfer ? 24 : h * 0.2679) : Cut;
        cut = Math.Clamp(cut, 0, Math.Min(w / 2, Shape == PlateShape.Chamfer ? h : w));
        Point[] corners = Shape switch
        {
            PlateShape.Lean => [new(cut, 0), new(w, 0), new(w - cut, h), new(0, h)],
            PlateShape.LeanRight => [new(0, 0), new(w - cut, 0), new(w, h), new(0, h)],
            _ => [new(cut, 0), new(w, 0), new(w, h), new(0, h), new(0, cut)],
        };
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(corners[0], isFilled: true);
            foreach (var corner in corners.Skip(1))
            {
                g.LineTo(corner);
            }
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(fill, null, geometry);
    }

    /// <summary>Whether a point (in the plate's own coordinates) is on the plate: anywhere in its box while it has a
    /// fill (a plate with none is not there to the pointer, as it is not to the eye). The box, not the cut shape: a
    /// press just past a leaning edge still takes the button.</summary>
    public bool HitTest(Point point) => Background is not null && new Rect(Bounds.Size).Contains(point);
}
