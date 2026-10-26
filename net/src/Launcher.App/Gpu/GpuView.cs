using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Launcher.App.Gpu;

/// <summary>
/// A 3D view drawn on the graphics card (all of the launcher's 3D is): Avalonia's
/// OpenGL control, with a multisampled target of its own (<see cref="RenderTarget"/>). It draws a frame only when
/// what it shows has changed (<see cref="Redraw"/>: a new model, new paint, a drag, another size) and while something
/// in it is really moving (<see cref="Animating"/>), and then at most <see cref="FrameMs"/> apart, 60 frames a second
/// whatever the display's rate; a still picture costs the card nothing, and neither does a hidden view or a minimised
/// window (drawn again for every frame the display showed, a machine standing on its turntable would take 2% of the
/// card). Without graphics acceleration (no OpenGL, or one too old) it says so in place of
/// the picture; there is no drawing on the processor to fall back to.
/// </summary>
public abstract class GpuView : OpenGlControlBase
{
    /// <summary>What a view says where its picture would be when the computer cannot draw it.</summary>
    public const string NoAcceleration = "3D preview needs graphics acceleration";

    /// <summary>How long a view waits for its OpenGL context before saying there is none.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(6);

    // (immutable: a brush that can change belongs to the thread that made it, and the tests open the window on several)
    private static readonly IBrush MessageBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Launcher.App.Theme.Palette.Of("DimColor", Color.Parse("#BCC2BC")));

    // a change while hidden (another page or lobby stage on show) is drawn when the view shows again: this looks for
    // that now and then, and only while such a change waits
    private readonly DispatcherTimer _wake = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _watchdog = new(DispatcherPriority.Background) { Interval = Patience };
    private Gl? _gl;
    private RenderTarget? _target;
    private StillPainter.Kit? _stills;
    private CourseStillPainter.Kit? _courseStills;
    private bool _unavailable;
    private string? _why;
    private long _drawn;      // when the last frame was drawn (Stopwatch ticks)
    private bool _pacing;     // a next frame is already waiting for its time

    protected GpuView()
    {
        _wake.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
            {
                _wake.Stop();
                Redraw();
            }
        };
        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();
            if (_gl is null)
            {
                Unavailable("no OpenGL context was made");
            }
        };
    }

    /// <summary>Whether the view says <see cref="NoAcceleration"/> when it cannot draw (not when a view beside it
    /// says it already).</summary>
    protected virtual bool SaysWhenUnavailable => true;

    /// <summary>Whether the view cannot draw (and says so).</summary>
    public bool IsUnavailable => _unavailable;

    /// <summary>The least time between two frames of a moving picture, in milliseconds: 60 frames a second.</summary>
    protected virtual double FrameMs => 1000.0 / 60;

    /// <summary>Whether the picture is moving by itself now (a let-go machine easing to rest, a race on the map): the
    /// view then asks for its own next frame. False: the next frame is the next <see cref="Redraw"/>.</summary>
    protected virtual bool Animating => false;

    /// <summary>How many frames this view has drawn (for checking that a still picture draws none).</summary>
    public int FramesDrawn { get; private set; }

    /// <summary>Asks for a frame (something shown changed). A hidden view draws it when it shows again; changes that
    /// come faster than <see cref="FrameMs"/> (a drag, a race's reports) share the frame that is next due.</summary>
    protected void Redraw()
    {
        if (!IsEffectivelyVisible)
        {
            if (TopLevel.GetTopLevel(this) is not null)
            {
                _wake.Start();
            }
            return;
        }
        Pace();
    }

    /// <summary>Whether a frame may be drawn now: <see cref="FrameMs"/> after the last, with half a millisecond's
    /// leave, so that a 60 Hz display's own frames (16.67 ms apart) all count.</summary>
    private bool Due => _drawn == 0 || Stopwatch.GetElapsedTime(_drawn).TotalMilliseconds >= FrameMs - 0.5;

    /// <summary>The view's size in the display's pixels.</summary>
    protected PixelSize PixelSize
    {
        get
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            return new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
        }
    }

    /// <summary>Makes the view's programs and buffers (the context is current).</summary>
    protected abstract void Init(Gl gl);

    /// <summary>Draws a frame into the bound, cleared target of <paramref name="size"/>.</summary>
    protected abstract void Draw(Gl gl, PixelSize size);

    /// <summary>Deletes what <see cref="Init"/> and the frames made; with <paramref name="lost"/> the context is gone
    /// and its objects with it, so only the references are dropped.</summary>
    protected abstract void Deinit(Gl gl, bool lost);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_unavailable)
        {
            _watchdog.Start();
            _ = CheckAsync();
        }
        StillPainter.Wanted += Redraw;
        CourseStillPainter.Wanted += Redraw;
        Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // another size, or shown again, or another interface scale (what is drawn in layout units): the picture as it is now
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty || change.Property == Controls.ScaledRoot.FactorProperty)
        {
            Redraw();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StillPainter.Wanted -= Redraw;
        CourseStillPainter.Wanted -= Redraw;
        _wake.Stop();
        _watchdog.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Whether the window's compositor can show OpenGL at all (software rendering cannot).</summary>
    private async Task CheckAsync()
    {
        if (ElementComposition.GetElementVisual(this)?.Compositor is { } compositor && await compositor.TryGetCompositionGpuInterop() is null)
        {
            Unavailable("the compositor has no GPU interop");
        }
    }

    private void Unavailable(string why)
    {
        _watchdog.Stop();
        _unavailable = true;
        _why = why;
        GpuDebug.Note(this, $"unavailable: {why}");
        ((Visual)this).InvalidateVisual();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _watchdog.Stop();
        try
        {
            var api = new Gl(gl, GlVersion);
            _target = new RenderTarget(api);
            Init(api);
            _gl = api;
            GpuDebug.Note(this, $"{gl.Renderer} / {gl.Version}, {api.Samples} samples");
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
        {
            _gl = null;
            Unavailable(e.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is { } api)
        {
            Deinit(api, lost: false);
            _target?.Delete();
            _stills?.Delete();
            _courseStills?.Delete();
        }
        _gl = null;
        _target = null;
        _stills = null;
        _courseStills = null;
    }

    protected override void OnOpenGlLost()
    {
        if (_gl is { } api)
        {
            Deinit(api, lost: true);
        }
        _gl = null;
        _target = null;
        _stills = null;
        _courseStills = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is not { } api || _target is not { } target)
        {
            return;
        }
        if (!IsEffectivelyVisible)
        {
            _wake.Start();
            return;
        }
        var start = Stopwatch.GetTimestamp();
        var size = PixelSize;
        try
        {
            target.Begin(size);
            Draw(api, size);
            target.Resolve();
            target.CopyTo(fb);
        }
        catch (InvalidOperationException e)
        {
            Unavailable(e.Message);
            return;
        }
        FramesDrawn++;
        _drawn = Stopwatch.GetTimestamp();
        GpuDebug.Frame(this, api, target, start);
        // the garage chooser's pictures and the Practice page's course stills waiting to be drawn, in this context
        StillPainter.Pump(api, ref _stills);
        CourseStillPainter.Pump(api, ref _courseStills);
        // a still picture is done; a moving one gets its next frame when that is due
        if (Animating || StillPainter.Waiting > 0 || CourseStillPainter.Waiting > 0)
        {
            Pace();
        }
    }

    /// <summary>The next frame, no sooner than <see cref="FrameMs"/> after the last: asked for at once when it is due,
    /// else when the time that is left has passed (<see cref="FrameWait"/>).</summary>
    private void Pace()
    {
        if (Due)
        {
            RequestNextFrameRendering();
            return;
        }
        if (_pacing)
        {
            return;
        }
        _pacing = true;
        FrameWait.After(FrameMs - 0.5 - Stopwatch.GetElapsedTime(_drawn).TotalMilliseconds, () =>
        {
            _pacing = false;
            if (TopLevel.GetTopLevel(this) is not null)
            {
                Redraw();   // due now; or hidden meanwhile, and then drawn when it shows again
            }
        });
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // (under the interface scale the view is laid out in the screen's own units: Controls.Unscaled)
        var factor = Controls.ScaledRoot.GetFactor(this);
        if (!_unavailable || !SaysWhenUnavailable || Bounds.Width < 40 * factor || Bounds.Height < 20 * factor)
        {
            return;
        }
        var text = new FormattedText(NoAcceleration, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this)), 13 * factor, MessageBrush)
        {
            MaxTextWidth = Math.Max(1, Bounds.Width - (24 * factor)),
            TextAlignment = TextAlignment.Center,
        };
        // centred within its box of the view's width
        context.DrawText(text, new Point(12 * factor, (Bounds.Height - text.Height) / 2));
    }

    public override string ToString() => _why is null ? GetType().Name : $"{GetType().Name} ({_why})";
}
