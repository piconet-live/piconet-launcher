using System.Numerics;

namespace Launcher.Core;

/// <summary>Where the track map looks from: the point it turns around, how much of the world it frames (a radius in
/// the course's units), which way it faces (yaw, radians about the vertical) and how far above it looks down.</summary>
public readonly record struct MapView(Vector3 Center, float Radius, float Yaw, float Pitch)
{
    /// <summary>Half the field of view: the map's 55 degrees, like the course select's.</summary>
    public const float HalfFov = 27.5f * MathF.PI / 180;

    /// <summary>How far the camera stands back so that <see cref="Radius"/> fills the view.</summary>
    public float Distance => Radius / MathF.Sin(HalfFov);

    /// <summary>World to camera: the camera stands at +Z <see cref="Distance"/> and looks along -Z.</summary>
    public Matrix4x4 Matrix => Matrix4x4.CreateTranslation(-Center) * Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateRotationX(Pitch);

    /// <summary>World to the camera's own space, the camera at the origin looking down -Z (<see cref="Matrix"/> moved
    /// back by <see cref="Distance"/>), for a lens (<see cref="StudioCamera.Projection"/>).</summary>
    public Matrix4x4 Camera => Matrix * Matrix4x4.CreateTranslation(0, 0, -Distance);
}

/// <summary>
/// The track map's camera, without drawing: the whole course turning slowly, or one racer followed close up, and the
/// eased change between them (click a racer to follow them, zoomed in, to make the race easier
/// to watch).
/// </summary>
public static class MapCamera
{
    /// <summary>How much road a followed racer's view frames around its centre: a few hundred metres, the machine and
    /// the corner ahead readable.</summary>
    public const float FollowRadius = 240;

    /// <summary>The followed view's centre lies this far ahead of the machine, so that more of the road ahead shows
    /// than of the road behind.</summary>
    public const float LookAhead = 70;

    /// <summary>Steeper than the whole course's view: close up, a low camera hides the road behind walls and banks.</summary>
    public const float FollowPitch = 52 * MathF.PI / 180;

    /// <summary>How long following starts or stops: about half a second.</summary>
    public const double EaseMs = 550;

    /// <summary>The course select's view: turning about the model's centre once a minute, from 30 to 50 degrees above
    /// it (the game's 22.5 to 67.5, gentler) and back every 23 seconds.</summary>
    public static MapView WholeCourse(Vector3 center, float radius, double seconds) => new(center, radius,
        (float)(seconds * 2 * Math.PI / 60), (float)((40 + 10 * Math.Sin(seconds * 2 * Math.PI / 23)) * Math.PI / 180));

    /// <summary>Close behind a racer at <paramref name="at"/>, facing <paramref name="yaw"/> (see <see cref="YawAlong"/>).</summary>
    public static MapView Following(Vector3 at, float yaw) =>
        new(at + Ahead(yaw) * LookAhead, FollowRadius, yaw, FollowPitch);

    /// <summary>The yaw that puts a direction of travel straight up the screen (away from the camera), or null when
    /// the move is too nearly vertical to say (a loop, a drop).</summary>
    public static float? YawAlong(Vector3 direction)
    {
        var flat = new Vector2(direction.X, direction.Z);
        if (flat.LengthSquared() < 1e-6f || flat.LengthSquared() < 0.04f * direction.LengthSquared())
        {
            return null;
        }
        // RotationY(yaw) takes (sin yaw, 0, -cos yaw) to (0, 0, -1): away from the camera
        return MathF.Atan2(direction.X, -direction.Z);
    }

    /// <summary>The level direction a yaw faces (the inverse of <see cref="YawAlong"/>).</summary>
    public static Vector3 Ahead(float yaw) => new(MathF.Sin(yaw), 0, -MathF.Cos(yaw));

    /// <summary>Slow in, slow out: 0 to 1 as <paramref name="t"/> goes 0 to 1.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Between two views, eased by <see cref="Ease"/>: the centre in a line, the zoom at an even pace (the
    /// radius geometrically, so that going from the whole course to a few hundred metres does not rush at the end),
    /// and the turn the short way round.</summary>
    public static MapView Blend(MapView from, MapView to, double t)
    {
        var e = (float)Ease(t);
        if (e <= 0 || e >= 1)
        {
            return e <= 0 ? from : to;
        }
        return new MapView(Vector3.Lerp(from.Center, to.Center, e),
            MathF.Exp(float.Lerp(MathF.Log(MathF.Max(from.Radius, 1)), MathF.Log(MathF.Max(to.Radius, 1)), e)),
            from.Yaw + Wrap(to.Yaw - from.Yaw) * e, float.Lerp(from.Pitch, to.Pitch, e));
    }

    /// <summary>A yaw turned towards <paramref name="target"/> the short way, a step of a smooth chase with this time
    /// constant (so that the view follows the road's bends without twitching at every wobble).</summary>
    public static float Approach(float yaw, float target, double elapsedMs, double timeMs)
    {
        var k = 1 - Math.Exp(-Math.Max(elapsedMs, 0) / timeMs);
        return yaw + Wrap(target - yaw) * (float)k;
    }

    /// <summary>An angle into -pi..pi.</summary>
    public static float Wrap(float angle)
    {
        angle %= 2 * MathF.PI;
        return angle > MathF.PI ? angle - 2 * MathF.PI : angle < -MathF.PI ? angle + 2 * MathF.PI : angle;
    }
}

/// <summary>Where the map's dots glide from: each racer's place as drawn now, by seat.</summary>
public static class MapGlide
{
    /// <summary>The places by seat. A seat given twice keeps its last place: the map is drawn on the window's
    /// thread, where an error over a racer listed twice would close the launcher.</summary>
    public static Dictionary<int, Vector3> BySeat(IEnumerable<(int Seat, Vector3 At)> dots)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var places = new Dictionary<int, Vector3>();
        foreach (var (seat, at) in dots)
        {
            places[seat] = at;
        }
        return places;
    }
}

/// <summary>
/// Which way a followed racer is going, from where it has been: each move of at least <see cref="Step"/> sets the
/// heading (a racer's place glides between updates, so these come every frame or two at racing speeds); a jump (a
/// respawn, a missed stretch) starts over without turning.
/// </summary>
public sealed class HeadingTracker
{
    public const float Step = 8;
    public const float Jump = 300;

    private Vector3? _last;

    /// <summary>The yaw along the latest move (see <see cref="MapCamera.YawAlong"/>), null until there is one.</summary>
    public float? Yaw { get; private set; }

    public void Reset()
    {
        _last = null;
        Yaw = null;
    }

    public void Add(Vector3 at)
    {
        if (_last is not { } last || Vector3.Distance(last, at) > Jump)
        {
            _last = at;
            return;
        }
        if (Vector3.Distance(last, at) < Step)
        {
            return;
        }
        Yaw = MapCamera.YawAlong(at - last) ?? Yaw;
        _last = at;
    }
}
