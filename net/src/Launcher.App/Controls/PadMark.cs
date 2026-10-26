using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Launcher.App.Controls;

/// <summary>
/// The pad's focus, drawn over the window: a soft teal frame round the control the pad is on, with a faint wash
/// inside it. It glides from control to control (200 ms, the design language's ease-out) and fades in and out
/// (120 ms in, 200 ms out); square, as direction C's shapes are, with its softness from motion, translucency and a
/// light weight. It takes no pointer and draws nothing while hidden.
/// </summary>
public sealed class PadMark : Control
{
    private static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(200), FadeIn = TimeSpan.FromMilliseconds(120),
        FadeOut = TimeSpan.FromMilliseconds(200);

    /// <summary>The design language's motion curve, cubic-bezier(.2, .8, .2, 1).</summary>
    private static readonly SplineEasing Ease = new() { X1 = 0.2, Y1 = 0.8, X2 = 0.2, Y2 = 1 };

    /// <summary>The ring and its gap when the theme cannot be asked (a control outside an application).</summary>
    private static readonly Color RingFallback = Color.FromRgb(0xFF, 0xFF, 0xFF), GapFallback = Color.FromRgb(0x00, 0x00, 0x00);

    private Rect _from, _to, _now;
    private double _radius;
    private double _shownFrom, _shownTo, _shown;
    private TimeSpan? _moveStart, _fadeStart;
    private bool _asked;

    public PadMark()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The mark shows or is fading in.</summary>
    public bool Showing => _shownTo > 0;

    /// <summary>The mark is gliding to a control (not there yet).</summary>
    public bool Moving => _now != _to;

    /// <summary>The frame round <paramref name="box"/> (in this control's units): glides there from where it is, or
    /// jumps (a page that scrolled under it, a mark that was hidden).</summary>
    /// <param name="radius">The control's corner radius (in this control's units): the ring follows its shape, the
    /// gap 1 px out from it, the ring 2 px further.</param>
    public void Show(Rect box, bool glide, double radius = 0)
    {
        if (Math.Abs(radius - _radius) > 0.01)
        {
            _radius = radius;
            InvalidateVisual();
        }
        var wasHidden = _shown <= 0 && _shownTo <= 0;
        if (box == _to && Showing)
        {
            return;
        }
        if (glide && !wasHidden)
        {
            (_from, _to, _moveStart) = (_now, box, null);
        }
        else
        {
            (_from, _to, _now, _moveStart) = (box, box, box, null);
        }
        if (_shownTo < 1)
        {
            (_shownFrom, _shownTo, _fadeStart) = (_shown, 1, null);
        }
        Animate();
    }

    public void Hide()
    {
        if (_shownTo <= 0)
        {
            return;
        }
        (_shownFrom, _shownTo, _fadeStart) = (_shown, 0, null);
        Animate();
    }

    public override void Render(DrawingContext context)
    {
        if (_shown <= 0.01 || _now.Width <= 0)
        {
            return;
        }
        var ring = ThemeColour("FocusColor", RingFallback);
        var gap = ThemeColour("FocusGapColor", GapFallback);
        // Settled, the box sits on whole device pixels: a 1 px gap and a 2 px ring drawn across a pixel boundary
        // come out as soft grey bands twice their width (the box shows as a blur). While
        // gliding it may sit anywhere.
        var box = Moving ? _now : Snap(_now, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        // the gap: 1 px just outside the control; the ring: 2 px outside that
        var (inner, outer) = (_radius > 0 ? _radius + 0.5 : 0, _radius > 0 ? _radius + 2 : 0);
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(gap, _shown), 1), box.Inflate(0.5), inner, inner);
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(ring, _shown), 2), box.Inflate(2), outer, outer);
    }

    /// <summary>The box's edges moved to the nearest device pixel (the window's scaling), so its lines are crisp.</summary>
    private static Rect Snap(Rect r, double scale)
    {
        double At(double v) => Math.Round(v * scale) / scale;
        var (x, y) = (At(r.X), At(r.Y));
        return new Rect(x, y, At(r.Right) - x, At(r.Bottom) - y);
    }

    private Color ThemeColour(string key, Color fallback) =>
        this.TryFindResource(key, out var value) && value is Color colour ? colour : fallback;

    private void Animate()
    {
        if (_asked)
        {
            return;
        }
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            // not in a window: the end at once
            (_now, _shown) = (_to, _shownTo);
            InvalidateVisual();
            return;
        }
        _asked = true;
        top.RequestAnimationFrame(Step);
    }

    private void Step(TimeSpan at)
    {
        _asked = false;
        _moveStart ??= at;
        _fadeStart ??= at;
        var move = Progress(at - _moveStart.Value, Glide);
        _now = new Rect(
            Lerp(_from.X, _to.X, move), Lerp(_from.Y, _to.Y, move),
            Lerp(_from.Width, _to.Width, move), Lerp(_from.Height, _to.Height, move));
        var fade = Progress(at - _fadeStart.Value, _shownTo > _shownFrom ? FadeIn : FadeOut);
        _shown = Lerp(_shownFrom, _shownTo, fade);
        InvalidateVisual();
        if (move < 1 || fade < 1)
        {
            Animate();
        }
    }

    private static double Progress(TimeSpan gone, TimeSpan whole) =>
        gone >= whole ? 1 : Ease.Ease(Math.Clamp(gone.TotalMilliseconds / whole.TotalMilliseconds, 0, 1));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
