using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Launcher.App.Controls;

/// <summary>
/// The window's content drawn at the player's interface scale (Settings, Launcher, "Interface scale"), on top of the
/// system's display scaling: one layout transform at the window's root, so that everything in the window grows or
/// shrinks together and stays sharp (text and shapes are laid out at the scale, not stretched as a picture). The
/// content is given at least <see cref="Least"/> at that scale; in a window smaller than that it is scrolled by the
/// <see cref="ScrollViewer"/> this stands in, never cut off. <see cref="FactorProperty"/> tells every control under it
/// the scale (the 3D views draw at the screen's own pixels by it: <see cref="Unscaled"/>); the popups take the scale
/// from the control they open at (<see cref="App"/>: every popup inherits its target's transform).
/// </summary>
public sealed class ScaledRoot : LayoutTransformControl
{
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<ScaledRoot, double>(nameof(Scale), 1.0);

    /// <summary>The least room the content needs at scale 1: the window's least size at 100%.</summary>
    public static readonly StyledProperty<Size> LeastProperty =
        AvaloniaProperty.Register<ScaledRoot, Size>(nameof(Least), new Size(1280, 720));

    /// <summary>The interface scale over a control (1 outside a <see cref="ScaledRoot"/>).</summary>
    public static readonly AttachedProperty<double> FactorProperty =
        AvaloniaProperty.RegisterAttached<ScaledRoot, Control, double>("Factor", 1.0, inherits: true);

    private ScrollViewer? _scroll;

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public Size Least
    {
        get => GetValue(LeastProperty);
        set => SetValue(LeastProperty, value);
    }

    public static double GetFactor(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(FactorProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleProperty)
        {
            var scale = Scale is > 0 and < 10 ? Scale : 1;
            SetValue(FactorProperty, scale);
            LayoutTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
            InvalidateMeasure();
        }
        else if (change.Property == LeastProperty)
        {
            InvalidateMeasure();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null)
        {
            _scroll.PropertyChanged += OnScrollChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroll is not null)
        {
            _scroll.PropertyChanged -= OnScrollChanged;
            _scroll = null;
        }
    }

    private void OnScrollChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty)
        {
            InvalidateMeasure();
        }
    }

    /// <summary>The room the content is laid out in: the window's (the scroller's view of it), and at least
    /// <see cref="Least"/> at the scale.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var view = _scroll is { Viewport: { Width: > 0, Height: > 0 } viewport } ? viewport
            : TopLevel.GetTopLevel(this)?.ClientSize ?? availableSize;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : view.Width;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : view.Height;
        var room = new Size(Math.Max(width, Least.Width * GetFactor(this)), Math.Max(height, Least.Height * GetFactor(this)));
        base.MeasureOverride(room);
        return room;
    }
}
