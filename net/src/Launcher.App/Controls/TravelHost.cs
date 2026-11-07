using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Launcher.App.Controls;

/// <summary>How a <see cref="TravelHost"/>'s selector is cut.</summary>
public enum TravelShape
{
    /// <summary>A rectangle with the corners of the control it lights.</summary>
    Box,

    /// <summary>The leaning plate of a mode tab (<see cref="PlateShape.Lean"/>).</summary>
    Lean,

    /// <summary>A box with a 2 px ring (a chip that is on).</summary>
    Ring,
}

/// <summary>
/// A selector that travels: one mark drawn under the host's content, gliding to whichever control a style has marked
/// <see cref="LitProperty"/>. The controls keep their own laid-out places; only this drawing moves. The theme makes a
/// lit control's own fill transparent inside a host (Theme/Motion.axaml).
/// </summary>
public sealed class TravelHost : Decorator
{
    public static readonly AttachedProperty<bool> LitProperty = AvaloniaProperty.RegisterAttached<TravelHost, Control, bool>("Lit");

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<TravelHost, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<TravelHost, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<TravelShape> ShapeProperty = AvaloniaProperty.Register<TravelHost, TravelShape>(nameof(Shape));

    private Control? _target;
    private Rect _from, _to, _now;
    private bool _placed, _gliding, _pending;

    static TravelHost()
    {
        AffectsRender<TravelHost>(FillProperty, StrokeProperty, ShapeProperty);
        LitProperty.Changed.AddClassHandler<Control>(static (control, e) =>
        {
            if (e.NewValue is true)
            {
                Light(control, retry: true);
            }
            else if (control.FindAncestorOfType<TravelHost>() is { } host)
            {
                // the next one may be lit in the same pass, either side of this
                Dispatcher.UIThread.Post(() => host.Release(control), DispatcherPriority.Loaded);
            }
        });
    }

    public static bool GetLit(Control control) => control.GetValue(LitProperty);

    public static void SetLit(Control control, bool lit) => control.SetValue(LitProperty, lit);

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public TravelShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    /// <summary>Where the mark is drawn now, in the host's units (for checks).</summary>
    public Rect Mark => _now;

    /// <summary>The control the mark is on or going to.</summary>
    public Control? Target => _target;

    private static void Light(Control control, bool retry)
    {
        if (control.FindAncestorOfType<TravelHost>() is { } host)
        {
            host.MoveTo(control);
        }
        else if (retry)
        {
            // a style can reach a control before it is in the tree
            Dispatcher.UIThread.Post(() =>
            {
                if (GetLit(control))
                {
                    Light(control, retry: false);
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void MoveTo(Control control)
    {
        if (!IsEffectivelyVisible)
        {
            _placed = false;   // shown again later: it stands, it does not fly in from where it was
        }
        _target = control;
        _pending = true;
        Sync();
    }

    private void Release(Control control)
    {
        if (_target == control && !GetLit(control))
        {
            _target = null;
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        if (_target is not { } target)
        {
            return;
        }
        if (TopLevel.GetTopLevel(target) is null || !GetLit(target))
        {
            _target = null;
            InvalidateVisual();
            return;
        }
        if (Place(target) is not { } box || (box == _to && !_pending))
        {
            return;
        }
        if (!_placed)
        {
            (_from, _to, _now, _placed, _pending) = (box, box, box, true, false);
            InvalidateVisual();
        }
        else if (_pending)
        {
            (_from, _to, _pending, _gliding) = (_now, box, false, true);
            Motion.Play(this, nameof(TravelHost), Motion.Standard, Motion.EaseOut, Step, () => _gliding = false);
        }
        else if (_gliding)
        {
            _to = box;   // laid out anew in flight: it lands on the new place
        }
        else
        {
            (_from, _to, _now) = (box, box, box);
            InvalidateVisual();
        }
    }

    private void Step(double p)
    {
        _now = new Rect(
            _from.X + ((_to.X - _from.X) * p), _from.Y + ((_to.Y - _from.Y) * p),
            _from.Width + ((_to.Width - _from.Width) * p), _from.Height + ((_to.Height - _from.Height) * p));
        InvalidateVisual();
    }

    /// <summary>The control's laid-out box in the host: its layout, not its transforms, so a press or a row still
    /// arriving does not move the place the mark rests on.</summary>
    private Rect? Place(Control target)
    {
        if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            return null;
        }
        var at = target.Bounds.Position;
        for (var up = target.GetVisualParent(); up is not null && up != this; up = up.GetVisualParent())
        {
            at += up.Bounds.Position;
        }
        return new Rect(at, target.Bounds.Size);
    }

    public override void Render(DrawingContext context)
    {
        if (_target is not { } target || !_placed || _now.Width <= 0 || _now.Height <= 0)
        {
            return;
        }
        if (Shape == TravelShape.Lean)
        {
            var cut = Math.Min(_now.Height * 0.2679, _now.Width / 2);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(_now.X + cut, _now.Y), isFilled: true);
                g.LineTo(_now.TopRight);
                g.LineTo(new Point(_now.Right - cut, _now.Bottom));
                g.LineTo(_now.BottomLeft);
                g.EndFigure(isClosed: true);
            }
            context.DrawGeometry(Fill, null, geometry);
            return;
        }
        var radius = target is TemplatedControl templated ? templated.CornerRadius.TopLeft : 0;
        if (Shape == TravelShape.Ring)
        {
            context.DrawRectangle(Fill, new Pen(Stroke, 2), _now.Deflate(1), radius, radius);
        }
        else
        {
            context.DrawRectangle(Fill, null, _now, radius, radius);
        }
    }
}
