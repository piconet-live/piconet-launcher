using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Launcher.App.Gpu;
using Launcher.Core;

namespace Launcher.App.Controls;

/// <summary>A racer on the map: where its machine is (the course's world coordinates), its colour, whether it is the
/// player's own, out of the race or finished, and its name (shown beside it when the map follows it).</summary>
public sealed record MapRacer(int Seat, Vector3 Position, Color Color, bool You, bool Out, bool Finished, int Rank,
    string Name = "");

/// <summary>
/// The track map: the course's model from the course-select screen (<see cref="CourseModel"/>), shaded by a light
/// from above, with every racer as a dot at its place, drawn on the graphics card (<see cref="CourseMapView"/>); the
/// dots on top, each gliding from its last place to its new one over the time between updates (the player's own game
/// updates every frame; a watched race comes through the coordinator about 10 times a second). While news of the race
/// comes, the course turns slowly as the game's course select shows it and the map draws 30 frames a second (the dots
/// glide between reports, so more would show nothing more); with no news (the games loading, the results, a race
/// gone quiet) it stands still and is not drawn again. With <see cref="Followed"/> set, the camera eases in close behind
/// that racer and stays with it, facing its way (<see cref="MapCamera"/>), its name in a label beside it (drawn over
/// the map by the window, as text).
/// </summary>
public sealed class CourseMap : Panel
{
    public static readonly StyledProperty<CourseModel?> ModelProperty =
        AvaloniaProperty.Register<CourseMap, CourseModel?>(nameof(Model));

    public static readonly StyledProperty<IReadOnlyList<MapRacer>?> RacersProperty =
        AvaloniaProperty.Register<CourseMap, IReadOnlyList<MapRacer>?>(nameof(Racers));

    /// <summary>The racer the camera follows (its seat), or null for the whole course.</summary>
    public static readonly StyledProperty<int?> FollowedProperty =
        AvaloniaProperty.Register<CourseMap, int?>(nameof(Followed));

    private readonly CourseMapView _view;

    public CourseMap()
    {
        ClipToBounds = true;
        var label = new MapLabel();
        _view = new CourseMapView(label);
        Children.Add(new Unscaled { Child = _view });   // drawn at the screen's pixels under the interface scale
        Children.Add(label);
    }

    public CourseModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public IReadOnlyList<MapRacer>? Racers
    {
        get => GetValue(RacersProperty);
        set => SetValue(RacersProperty, value);
    }

    public int? Followed
    {
        get => GetValue(FollowedProperty);
        set => SetValue(FollowedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty)
        {
            _view.SetModel(Model);
        }
        else if (change.Property == RacersProperty)
        {
            _view.SetRacers(change.GetOldValue<IReadOnlyList<MapRacer>?>(), Racers);
        }
        else if (change.Property == FollowedProperty)
        {
            _view.SetFollowed(Followed);
        }
    }
}

/// <summary>The map's picture on the graphics card: the camera, the dots' glide, the track and the dots (see
/// <see cref="CourseMap"/>).</summary>
internal sealed class CourseMapView : GpuView
{
    /// <summary>How quickly the followed view turns to a new heading: through a bend with the machine, not at every
    /// wobble of its line.</summary>
    private const double TurnMs = 350;

    private static readonly Vector4 FollowRing = Premultiplied(Color.FromRgb(0x3C, 0xDC, 0xFF));
    private static readonly Vector4 White = Vector4.One;
    private static readonly Vector4 Dim = Premultiplied(Color.FromArgb(160, 10, 14, 22));
    private static readonly Vector4 OutRing = Premultiplied(Color.FromArgb(150, 160, 170, 185));
    private static readonly Vector4 FollowedOutRing = Premultiplied(Color.FromArgb(200, 160, 170, 185));

    /// <summary>The race counts as running this long after its last news (a watched race reports every 100 ms).</summary>
    private const long RunningMs = 600;

    private readonly MapLabel _label;
    private double _turned;   // seconds the whole-course view has turned for: it turns only while the race runs
    private readonly List<MapDisc> _discs = [];
    private CourseModel? _model;
    private IReadOnlyList<MapRacer>? _racers;
    private int? _followed;
    private Dictionary<int, Vector3> _from = [];
    private long _updated;
    private double _interval = 100;
    private MapPainter? _painter;

    // the camera: the view drawn last, where an eased change of view began and when, and the followed racer's last
    // place while it raced, its heading, and the view's turn chasing that heading
    private MapView? _shown;
    private MapView? _easeFrom;
    private long _changed;
    private long _frame;
    private Vector3? _followAt;
    private readonly HeadingTracker _heading = new();
    private float _yaw;

    public CourseMapView(MapLabel label)
    {
        _label = label;
    }

    public void SetModel(CourseModel? model)
    {
        // another course: nothing to glide over from
        _model = model;
        _easeFrom = null;
        _shown = null;
        Redraw();
    }

    public void SetRacers(IReadOnlyList<MapRacer>? old, IReadOnlyList<MapRacer>? racers)
    {
        // where each dot is drawn now becomes where it glides from
        var now = Environment.TickCount64;
        var t = Progress(now);
        // (a seat named twice keeps one place: a race with one is refused before it comes here, ServerMessages.Sound,
        // and the map does not close the launcher over one that is not)
        _from = MapGlide.BySeat((old ?? []).Select(r => (r.Seat, Glide(r, t))));
        _interval = Math.Clamp(now - _updated, 10, 400);
        _updated = now;
        _racers = racers;
        Redraw();
    }

    // 30 frames a second while the race runs or the view changes; still otherwise
    protected override double FrameMs => 1000.0 / 30;

    protected override bool Animating => Environment.TickCount64 - _updated < RunningMs || _easeFrom is not null;

    public void SetFollowed(int? seat)
    {
        // from the view as it is now; the followed view starts once the racer's place is known
        _followed = seat;
        Ease();
        _followAt = null;
        _heading.Reset();
        Redraw();
    }

    private void Ease()
    {
        _easeFrom = _shown;
        _changed = Environment.TickCount64;
    }

    private double Progress(long now) => Math.Clamp((now - _updated) / _interval, 0, 1);

    private Vector3 Glide(MapRacer r, double t) =>
        _from.TryGetValue(r.Seat, out var from) && Vector3.Distance(from, r.Position) < 300
            ? Vector3.Lerp(from, r.Position, (float)t)
            : r.Position;

    protected override void Init(Gl gl) => _painter = new MapPainter(gl);

    protected override void Deinit(Gl gl, bool lost)
    {
        if (!lost)
        {
            _painter?.Delete();
        }
        _painter = null;
    }

    protected override void Draw(Gl gl, PixelSize size)
    {
        if (_painter is not { } painter || _model is not { Positions.Length: > 0, Indices.Length: > 0 } model || Bounds.Width < 10 || Bounds.Height < 10)
        {
            _label.Hide();
            return;
        }
        var now = Environment.TickCount64;
        var progress = Progress(now);
        var view = View(model, now, progress);
        var camera = view.Camera;
        // close up, a tall part of the course may reach the camera: cut off near it
        var near = view.Distance * 0.1f;
        Matrix4x4.Invert(camera, out var toWorld);
        var far = Math.Max(near * 2, Vector3.Distance(toWorld.Translation, model.Center) + (model.Radius * 1.05f));
        var viewProjection = camera * StudioCamera.Projection(MapView.HalfFov, size.Width, size.Height, near, far);
        painter.Track(model, viewProjection);

        if (_racers is not { Count: > 0 } racers)
        {
            _label.Hide();
            return;
        }
        // the racers, far first; the player's own after, ringed; the followed one over all, larger, with its name
        // pixels to the map's own layout unit (the label's), which the interface scale makes larger than this view's
        var scale = (float)(size.Width / Bounds.Width * ScaledRoot.GetFactor(this));
        var followed = _followed;
        var dots = racers.Select(r => (Racer: r, Projected: StudioCamera.Project(Glide(r, progress), viewProjection, size.Width, size.Height)))
            .Where(d => d.Projected.Depth > 0)
            .OrderBy(d => d.Racer.Seat == followed).ThenBy(d => d.Racer.You).ThenByDescending(d => d.Projected.Depth).ToList();
        _discs.Clear();
        MapRacer? named = null;
        var namedAt = Vector2.Zero;
        foreach (var (racer, (at, _)) in dots)
        {
            if (racer.Seat == followed)
            {
                _discs.Add(new MapDisc(at, 13 * scale, Vector4.Zero, FollowRing, 2.5f * scale));
                _discs.Add(racer.Out
                    ? new MapDisc(at, 6 * scale, Vector4.Zero, FollowedOutRing, 2 * scale)
                    : new MapDisc(at, 8 * scale, Premultiplied(racer.Color), White, 2 * scale));
                (named, namedAt) = (racer, at / scale);
                continue;
            }
            if (racer.Out)
            {
                _discs.Add(new MapDisc(at, 3.5f * scale, Vector4.Zero, OutRing, 1.5f * scale));
                continue;
            }
            _discs.Add(racer.You
                ? new MapDisc(at, 7 * scale, Premultiplied(racer.Color), White, 2 * scale)
                : new MapDisc(at, 4.5f * scale, Premultiplied(racer.Color), Dim, 1.5f * scale));
        }
        painter.Discs(_discs, new Vector2(size.Width, size.Height));
        if (named is { Name.Length: > 0 })
        {
            _label.Show(named.Name, new Point(namedAt.X, namedAt.Y));
        }
        else
        {
            _label.Hide();
        }
    }

    /// <summary>
    /// This frame's view: the whole course turning, or close behind the followed racer facing its way, with an eased
    /// change from the view as it was whenever that changes (following starts, stops or moves to another racer), so
    /// that nothing jumps. A knocked-out or vanished racer's view stays at its last place while it raced.
    /// </summary>
    private MapView View(CourseModel model, long now, double progress)
    {
        var elapsed = _frame == 0 ? 16 : Math.Clamp(now - _frame, 0, 100);
        _frame = now;
        if (now - _updated < RunningMs)
        {
            _turned += elapsed / 1000.0;
        }
        var target = MapCamera.WholeCourse(model.Center, model.Radius, _turned);
        if (_followed is { } seat)
        {
            if (_racers?.FirstOrDefault(r => r.Seat == seat) is { Out: false } racer)
            {
                var at = Glide(racer, progress);
                if (_followAt is null)
                {
                    // seen for the first time: from the view as it is, facing the same way until it moves
                    Ease();
                    _yaw = (_shown ?? target).Yaw;
                }
                _followAt = at;
                _heading.Add(at);
            }
            if (_followAt is { } place)
            {
                if (_heading.Yaw is { } heading)
                {
                    _yaw = MapCamera.Approach(_yaw, heading, elapsed, TurnMs);
                }
                target = MapCamera.Following(place, _yaw);
            }
        }
        var view = target;
        if (_easeFrom is { } from)
        {
            var t = (now - _changed) / MapCamera.EaseMs;
            view = MapCamera.Blend(from, target, t);
            if (t >= 1)
            {
                _easeFrom = null;
            }
        }
        _shown = view;
        return view;
    }

    private static Vector4 Premultiplied(Color c)
    {
        var a = c.A / 255f;
        return new Vector4(c.R / 255f * a, c.G / 255f * a, c.B / 255f * a, a);
    }
}

/// <summary>The followed racer's name beside its dot, over the map: in a dark box right of the ring, or left of it
/// near the map's right edge.</summary>
internal sealed class MapLabel : Control
{
    private static readonly IBrush Box = new SolidColorBrush(Color.FromArgb(190, 10, 14, 22));

    private (string Name, FormattedText Text)? _text;
    private Point? _at;

    public MapLabel()
    {
        IsHitTestVisible = false;
    }

    public void Show(string name, Point at)
    {
        if (_text is not { } text || text.Name != name)
        {
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.SemiBold);
            _text = (name, new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 13, Brushes.White));
        }
        if (_at != at)
        {
            _at = at;
            InvalidateVisual();
        }
    }

    public void Hide()
    {
        if (_at is not null)
        {
            _at = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_at is not { } at || _text is not { } label)
        {
            return;
        }
        var text = label.Text;
        var width = text.Width + 10;
        var x = at.X + 18 + width < Bounds.Width ? at.X + 18 : at.X - 18 - width;
        var box = new Rect(x, at.Y - (text.Height / 2) - 2, width, text.Height + 4);
        context.DrawRectangle(Box, null, box, 5, 5);
        context.DrawText(text, new Point(box.X + 5, box.Y + 2));
    }
}
