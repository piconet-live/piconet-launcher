using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Launcher.App.Gpu;
using Launcher.App.Theme;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.Controls;

/// <summary>
/// The garage's machine in 3D: its model from the player's disc (<see cref="GameSkin.Machine3D"/>), drawn on the
/// graphics card (<see cref="Studio"/>) on a turntable that is dragged round (and tilted) and eases to rest when let go.
/// The left column's stands still by itself (a still machine is drawn once, not 60 times a second); the Garage's
/// turns slowly while its page shows (<see cref="Turns"/>). It is drawn in
/// its look: stock colour k is the game's own texture file for it, a painted colour its paint repainted as the race will
/// (<see cref="MachinePaint"/>); only the painted textures change, worked out off the window's thread and put on the
/// card on the next frame. It stands on a turntable drawn in the same scene (<see cref="TurntableFloor"/>), so the
/// ring stays under it as it turns and tilts.
/// </summary>
public sealed partial class MachinePreview : GpuView
{
    public static readonly StyledProperty<int> MachineProperty =
        AvaloniaProperty.Register<MachinePreview, int>(nameof(Machine), -1);

    /// <summary>One of the machine's stock colours (0-3), or null: <see cref="PaintColor"/> painted on it.</summary>
    public static readonly StyledProperty<int?> StockColorProperty =
        AvaloniaProperty.Register<MachinePreview, int?>(nameof(StockColor));

    /// <summary>The painted colour as "#RRGGBB" (anything else keeps the last good one).</summary>
    public static readonly StyledProperty<string?> PaintColorProperty =
        AvaloniaProperty.Register<MachinePreview, string?>(nameof(PaintColor));

    /// <summary>The machine turns slowly by itself (the Garage's, while its page shows);
    /// false: it stands still until it is dragged round (the left column's).</summary>
    public static readonly StyledProperty<bool> TurnsProperty =
        AvaloniaProperty.Register<MachinePreview, bool>(nameof(Turns));

    /// <summary>A custom machine to show in place of <see cref="Machine"/> (the builder's); null: the stock machine.</summary>
    public static readonly StyledProperty<CustomBuild?> CustomProperty =
        AvaloniaProperty.Register<MachinePreview, CustomBuild?>(nameof(Custom));

    /// <summary>A custom machine's colours by part (body, cockpit, booster), each null for the part's own.</summary>
    public static readonly StyledProperty<IReadOnlyList<Online.Protocol.Rgb?>?> PartColoursProperty =
        AvaloniaProperty.Register<MachinePreview, IReadOnlyList<Online.Protocol.Rgb?>?>(nameof(PartColours));

    private const float Slowly = 0.4f;         // radians a second by itself: once round in a quarter of a minute
    private const float Pitch = 0.3f;          // seen from a little above
    private const float Zoom = 1.05f;
    private const float Glide = 0.22f;         // let go, the turn dies away over about this many seconds
    private const float Still = 0.03f;         // radians a second under which the turntable is at rest
    private const float Fastest = 6;           // radians a second a flick can leave it with
    private const int Kept = 4;                // machines kept on the card, so that going back to one is instant
    private const double UploadBudgetMs = 2;   // a frame's time for putting a new machine's textures on the card

    private readonly List<Shown> _kept = [];
    private (GarageMachine Machine, PackedModel Packed, TurntableFloor Floor)? _machine;
    // the look's textures, for the machine they were worked out for
    private (GarageMachine Machine, IReadOnlyList<(int Texture, Picture Picture)> Pictures)? _paint;
    private bool _painting;
    private bool _uploading;   // a machine's textures are still going on the card
    private bool _paintAgain;
    private Studio? _studio;
    private float _yaw = 3.9f;   // its nose towards the viewer, a little to the right
    private float _pitch = Pitch;
    private long _last;
    private long _moved;         // when the drag last moved
    private float _spin;         // radians a second: the drag's own while it lasts, dying away once let go
    private Point? _drag;
    private Window? _window;

    public MachinePreview()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public int Machine
    {
        get => GetValue(MachineProperty);
        set => SetValue(MachineProperty, value);
    }

    public int? StockColor
    {
        get => GetValue(StockColorProperty);
        set => SetValue(StockColorProperty, value);
    }

    public string? PaintColor
    {
        get => GetValue(PaintColorProperty);
        set => SetValue(PaintColorProperty, value);
    }

    public CustomBuild? Custom
    {
        get => GetValue(CustomProperty);
        set => SetValue(CustomProperty, value);
    }

    public IReadOnlyList<Online.Protocol.Rgb?>? PartColours
    {
        get => GetValue(PartColoursProperty);
        set => SetValue(PartColoursProperty, value);
    }

    /// <summary>A custom machine's parts (body, cockpit, booster) as they stand on the view now, in its own
    /// coordinates, as it turns: where the labels' lines point. Empty for a stock machine or none.</summary>
    public event Action<IReadOnlyList<Point>>? AnchorsMoved;

    private Point[] _anchors = [];

    public bool Turns
    {
        get => GetValue(TurnsProperty);
        set => SetValue(TurnsProperty, value);
    }

    /// <summary>Turning by itself now: asked to, not held, and in a window that is not minimised (a hidden view
    /// draws nothing: <see cref="GpuView"/>).</summary>
    private bool TurningNow => Turns && _drag is null && _window is not { WindowState: WindowState.Minimized };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TurnsProperty)
        {
            _last = Environment.TickCount64;
            Redraw();
        }
        else if (change.Property == MachineProperty || (change.Property == CustomProperty && !SameParts(change)))
        {
            _ = LoadAsync(Machine);
        }
        else if (change.Property == PartColoursProperty)
        {
            Redraw();
        }
        else if (change.Property == StockColorProperty || (change.Property == PaintColorProperty && StockColor is null))
        {
            _ = PaintAsync();
        }
    }

    /// <summary>A change of the custom machine that keeps its parts (its colours, its pilot) loads nothing.</summary>
    private static bool SameParts(AvaloniaPropertyChangedEventArgs change) =>
        change.OldValue is CustomBuild a && change.NewValue is CustomBuild b && (a.Body, a.Cockpit, a.Booster) == (b.Body, b.Cockpit, b.Booster);

    private async Task LoadAsync(int machine)
    {
        _machine = null;
        Redraw();
        var custom = Custom;
        var trace = LoadTrace.On ? new Trace(custom is null ? $"machine {machine}" : $"custom {custom.Body}/{custom.Cockpit}/{custom.Booster}") : null;
        _trace = trace;
        // the model, its packing and its look worked out in one go off the window's thread, so that the window's
        // thread is waited for once (it is busy while the window opens or the Garage fills)
        var (stock, color) = (StockColor, Rgb.Parse(PaintColor));
        var ready = await Task.Run<Ready?>(async () =>
        {
            var loaded = custom is not null ? await GameSkin.Custom3D(custom.Body, custom.Cockpit, custom.Booster)
                : machine >= 0 ? await GameSkin.Machine3D(machine) : null;
            if (loaded is null)
            {
                return null;
            }
            trace?.Mark("loaded");
            using var job = LoadTrace.Begin($"{trace?.What} pack and look");
            using var timed = LoadTrace.Stage("pack+look");
            var look = loaded.Paint is { } paint && (stock is not null || color is not null)
                ? paint.PaintedPictures(stock is { } k ? paint.Stock(k) : paint.Painted(color!)) : null;
            return new Ready(loaded, GpuMesh.Pack(loaded.Model), TurntableFloor.Of(loaded.Model), look);
        });
        trace?.Mark("ready");
        if (machine != Machine || !ReferenceEquals(custom, Custom) && (custom is null || Custom is null
            || (custom.Body, custom.Cockpit, custom.Booster) != (Custom.Body, Custom.Cockpit, Custom.Booster)) || ready is not { } r)
        {
            return;   // another machine was chosen meanwhile, or there is no disc
        }
        _machine = (r.Loaded, r.Packed, r.Floor);
        if (r.Look is { } pictures && (stock, color) == (StockColor, Rgb.Parse(PaintColor)))
        {
            _paint = (r.Loaded, pictures);
            Redraw();
            return;
        }
        await PaintAsync();
    }

    /// <summary>Works out the look's textures (a stock file, or the paint repainted) off the window's thread; the next
    /// frame puts them on the card. One at a time: a colour dragged round the wheel changes faster than it is
    /// repainted, so meanwhile changes make one more round, for the latest look.</summary>
    private async Task PaintAsync()
    {
        if (_painting)
        {
            _paintAgain = true;
            return;
        }
        _painting = true;
        try
        {
            do
            {
                _paintAgain = false;
                var (stock, color) = (StockColor, Rgb.Parse(PaintColor));
                if (_machine is not { } machine || (stock is null && color is null))
                {
                    return;
                }
                if (machine.Machine.Paint is not { } paint)
                {
                    return;   // a custom machine: coloured by material as it is drawn
                }
                var pictures = await Task.Run(() =>
                {
                    using var job = LoadTrace.Begin($"{_trace?.What} look");
                    return paint.PaintedPictures(stock is { } k ? paint.Stock(k) : paint.Painted(color!));
                });
                _trace?.Mark("look");
                if (ReferenceEquals(_machine?.Machine, machine.Machine))
                {
                    _paint = (machine.Machine, pictures);
                    Redraw();
                }
            }
            while (_paintAgain);
        }
        finally
        {
            _painting = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _last = Environment.TickCount64;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowChanged;
        }
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The window back from minimised: the turning goes on from where it stood.</summary>
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            _last = Environment.TickCount64;
            Redraw();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _drag = e.GetPosition(this);
        _spin = 0;
        _moved = Environment.TickCount64;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } from)
        {
            return;
        }
        var at = e.GetPosition(this);
        // across turns it, up and down tilts it (within what shows it well), as far for a move across the page
        // whatever the interface scale (the view is laid out in the screen's own units: Unscaled)
        var factor = (float)ScaledRoot.GetFactor(this);
        var turned = (float)(at.X - from.X) * 0.012f / factor;
        _yaw += turned;
        _pitch = Math.Clamp(_pitch + ((float)(at.Y - from.Y) * 0.006f / factor), 0.02f, 0.8f);
        _drag = at;
        // how fast it is being turned, smoothed over the last moves: what it is let go with
        var now = Environment.TickCount64;
        var seconds = Math.Clamp(now - _moved, 4, 100) / 1000f;
        _spin = (_spin * 0.5f) + (turned / seconds * 0.5f);
        _moved = now;
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        LetGo();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        LetGo();
    }

    /// <summary>Let go: it turns on as it was being turned and eases to rest; held still before, it stays.</summary>
    private void LetGo()
    {
        if (_drag is null)
        {
            return;
        }
        _drag = null;
        var now = Environment.TickCount64;
        _spin = now - _moved > 80 ? 0 : Math.Clamp(_spin, -Fastest, Fastest);
        _last = now;
        Redraw();
    }

    // moving by itself while it turns (the Garage's) or eases to rest (or is turned for a measurement, GpuDebug.Turning)
    protected override bool Animating => TurningNow || (_drag is null && MathF.Abs(_spin) > Still) || GpuDebug.Turning || _uploading;

    protected override void Init(Gl gl) => _studio = new Studio(gl);

    protected override void Deinit(Gl gl, bool lost)
    {
        if (!lost)
        {
            _kept.ForEach(k => k.Delete());
            _studio?.Delete();
        }
        _kept.Clear();
        _studio = null;
    }

    protected override void Draw(Gl gl, PixelSize size)
    {
        var now = Environment.TickCount64;
        var seconds = Math.Clamp(now - _last, 0, 100) / 1000f;
        _last = now;
        if (GpuDebug.Turning)
        {
            _yaw += seconds;   // as a steady drag would turn it
        }
        else if (TurningNow)
        {
            // let go, a flick's turn eases into the slow turn, whichever way it went
            _spin = Slowly + ((_spin - Slowly) * MathF.Exp(-seconds / Glide));
            _yaw += _spin * seconds;
        }
        else if (_drag is null && MathF.Abs(_spin) > Still)
        {
            _yaw += _spin * seconds;
            _spin *= MathF.Exp(-seconds / Glide);
        }
        if (_studio is not { } studio || _machine is not { } machine || Show(gl, machine) is not { } shown)
        {
            return;
        }
        var model = machine.Machine.Model;
        var view = StudioCamera.View(model.Center, model.Radius, _yaw, _pitch, Zoom);
        var distance = StudioCamera.Distance(model.Radius, Zoom);
        // the floor's square reaches past the machine: room for it in front and behind
        var reach = Math.Max(model.Radius, machine.Floor.Ring * TurntableFloor.Reach * 1.45f);
        var projection = StudioCamera.Projection(StudioCamera.FieldOfView / 2, size.Width, size.Height,
            Math.Max(distance - reach, distance * 0.05f), distance + reach);
        var colours = machine.Machine.Paint is null ? PartColours : null;
        studio.PartColour = colours is null ? null
            : part => part >= 0 && part < colours.Count && colours[part] is { } c ? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : null;
        MoveAnchors(machine.Machine.Anchors, view * projection, size);
        studio.Opaque(shown.Mesh, shown.Textures, view, projection);
        studio.Floor(machine.Floor, view, projection);
        studio.Translucent(shown.Mesh, shown.Textures, view, projection);
        if (_trace is { Shown: false })
        {
            _trace.Shown = true;
            LoadTrace.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{_trace.What}: first frame with its model {_trace.Since:F0} ms after it was asked ({_trace.Marks}; " +
                $"on the card in {_trace.Upload:F1} ms over {_trace.Frames} frames: mesh {_trace.Mesh:F1}, {_trace.Textures} textures {_trace.Upload - _trace.Mesh:F1})"));
        }
    }


    /// <summary>Where a custom machine's parts stand on the view, told when they have moved by half a point.</summary>
    private void MoveAnchors(IReadOnlyList<Vector3>? anchors, Matrix4x4 viewProjection, PixelSize size)
    {
        if (anchors is null || size.Width <= 0 || Bounds.Width <= 0)
        {
            return;
        }
        var (sx, sy) = (Bounds.Width / size.Width, Bounds.Height / size.Height);
        var points = anchors.Select(a => StudioCamera.Project(a, viewProjection, size.Width, size.Height).At)
            .Select(p => new Point(p.X * sx, p.Y * sy)).ToArray();
        if (points.Length != _anchors.Length || points.Where((p, i) => Math.Abs(p.X - _anchors[i].X) + Math.Abs(p.Y - _anchors[i].Y) > 0.5).Any())
        {
            _anchors = points;
            AnchorsMoved?.Invoke(points);
        }
    }

    /// <summary>The machine on the card (uploaded the first time it is shown), in its look.</summary>
    private Shown? Show(Gl gl, (GarageMachine Machine, PackedModel Packed, TurntableFloor Floor) machine)
    {
        var shown = _kept.Find(k => ReferenceEquals(k.Machine, machine.Machine));
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (shown is null)
        {
            var mesh = new GpuMesh(gl, machine.Packed);
            if (_trace is { Shown: false } trace)
            {
                (trace.Mesh, trace.Textures) = (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    machine.Machine.Model.Textures.Count(t => t is not null));
            }
            // its textures go on the card a few a frame (UploadBudgetMs), so that no frame waits long for them
            shown = new Shown(machine.Machine, mesh, new GpuTextures(gl, machine.Machine.Model.Textures, later: true));
            _kept.Add(shown);
            if (_kept.Count > Kept)
            {
                _kept[0].Delete();
                _kept.RemoveAt(0);
            }
        }
        else if (_kept[^1] != shown)
        {
            // shown again: kept longest, and its look put back on
            _kept.Remove(shown);
            _kept.Add(shown);
            shown.Look = null;
        }
        _uploading = !shown.Textures.Complete
            && !shown.Textures.UploadSome(start + (long)(UploadBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000));
        if (_trace is { Shown: false } uploads)
        {
            uploads.Upload += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            uploads.Frames++;
        }
        if (_uploading)
        {
            return null;   // drawn when its last texture is on the card
        }
        if (_paint is { } paint && ReferenceEquals(paint.Machine, machine.Machine) && !ReferenceEquals(shown.Look, paint.Pictures))
        {
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var (texture, picture) in paint.Pictures)
            {
                shown.Textures.Set(texture, picture);
            }
            shown.Look = paint.Pictures;
            if (LoadTrace.On)
            {
                LoadTrace.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{_trace?.What}: look on the card, {paint.Pictures.Count} textures in {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms"));
            }
        }
        return shown;
    }

    /// <summary>A machine on the card, and the look last put on it.</summary>
    private sealed class Shown(GarageMachine machine, GpuMesh mesh, GpuTextures textures)
    {
        public GarageMachine Machine { get; } = machine;

        public GpuMesh Mesh { get; } = mesh;

        public GpuTextures Textures { get; } = textures;

        public object? Look { get; set; }

        public void Delete()
        {
            Mesh.Delete();
            Textures.Delete();
        }
    }
}
