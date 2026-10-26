using System.Numerics;

namespace Launcher.Core;

/// <summary>Where a course's picture looks from: the point it aims at and how far back the camera stands.</summary>
public readonly record struct CourseFrame(Vector3 Target, float Distance);

/// <summary>
/// The Practice page's pictures of a course's model (<see cref="CourseModel"/>, the course select's), without drawing:
/// the hero, turning once in <see cref="TurnSeconds"/> seen a little above, and each tile's still from one fixed angle,
/// above at three quarters. Both are framed so that every vertex of the model lies inside the picture with a
/// <see cref="Margin"/> round it: the hero at one distance for the whole turn (it does not breathe as it turns), the
/// still centred on the model as that angle sees it. Matrices as <see cref="StudioCamera"/>'s.
/// </summary>
public static class CourseShot
{
    /// <summary>The share of each half of the picture kept clear round the model.</summary>
    public const float Margin = 0.08f;

    /// <summary>One turn of the hero, as the game's course select turns its model.</summary>
    public const double TurnSeconds = 40;

    /// <summary>The hero looks down this far: low, so that a wide, short picture holds the whole course large.</summary>
    public const float HeroPitch = 18 * MathF.PI / 180;

    /// <summary>Half the hero's lens across the picture's shorter side: long, so that the near end of the course
    /// does not swell as it turns past.</summary>
    public const float HeroHalfFov = 12 * MathF.PI / 180;

    /// <summary>The tiles' angle, the same for every course: three quarters round, a little above.</summary>
    public const float StillYaw = 45 * MathF.PI / 180;

    public const float StillPitch = 32 * MathF.PI / 180;

    /// <summary>Half the tiles' lens across the picture's shorter side: a long lens, the model's near end not
    /// swollen.</summary>
    public const float StillHalfFov = 18 * MathF.PI / 180;

    /// <summary>How many angles of the turn the hero's framing is worked out at.</summary>
    private const int TurnSteps = 72;

    /// <summary>A hero narrower than this (in layout units) shows its title plate small: the name at 40, not 64
    /// (at a larger interface scale the large plate hides the model).</summary>
    public const double NarrowHero = 900;

    /// <summary>The most of the hero's width the title plate may take.</summary>
    public const double PlateShare = 0.40;

    /// <summary>How wide the plate's name may be in a hero of <paramref name="heroWidth"/>, so that the plate, with
    /// <paramref name="aroundName"/> of padding and stripes beside the name, takes at most <see cref="PlateShare"/> of
    /// it (the name is shrunk to fit beyond that).</summary>
    public static double NameRoom(double heroWidth, double aroundName) => Math.Max(96, (heroWidth * PlateShare) - aroundName);

    /// <summary>The part of a hero picture <paramref name="width"/> pixels wide that the model is framed in: right of
    /// the title plate, which covers <paramref name="covered"/> pixels from the left, with a small gap; never less than
    /// a quarter of the picture.</summary>
    public static (int X, int Width) HeroArea(int width, int covered)
    {
        var x = Math.Clamp(covered + (int)MathF.Ceiling(width * 0.02f), 0, width - Math.Max(1, width / 4));
        return (x, width - x);
    }

    /// <summary>The hero's yaw after <paramref name="seconds"/> of turning.</summary>
    public static float HeroYaw(double seconds) => (float)(seconds % TurnSeconds / TurnSeconds * 2 * Math.PI);

    /// <summary>The hero's framing (<see cref="Turning"/> at its pitch and lens).</summary>
    public static CourseFrame Hero(CourseModel model, int width, int height) => Turning(model, HeroPitch, HeroHalfFov, width, height);

    /// <summary>The whole view and lens of the hero after <paramref name="seconds"/>.</summary>
    public static Matrix4x4 HeroViewProjection(CourseModel model, CourseFrame frame, double seconds, int width, int height) =>
        ViewProjection(model, frame, HeroYaw(seconds), HeroPitch, HeroHalfFov, width, height);

    /// <summary>A tile's framing (<see cref="Centred"/> at its angle and lens).</summary>
    public static CourseFrame Still(CourseModel model, int width, int height) =>
        Centred(model, StillYaw, StillPitch, StillHalfFov, width, height);

    /// <summary>The whole view and lens of a tile's still.</summary>
    public static Matrix4x4 StillViewProjection(CourseModel model, CourseFrame frame, int width, int height) =>
        ViewProjection(model, frame, StillYaw, StillPitch, StillHalfFov, width, height);

    /// <summary>World to the picture through a camera aimed at the frame's target, turned <paramref name="yaw"/>, seen
    /// from <paramref name="pitch"/> above, the frame's distance back, with a lens whose <paramref name="halfFov"/>
    /// spans half the picture's shorter side.</summary>
    public static Matrix4x4 ViewProjection(CourseModel model, CourseFrame frame, float yaw, float pitch, float halfFov, int width, int height)
    {
        var (near, far) = Planes(model, frame);
        return Orientation(frame.Target, yaw, pitch) * Matrix4x4.CreateTranslation(0, 0, -frame.Distance)
            * StudioCamera.Projection(halfFov, width, height, near, far);
    }

    /// <summary>The near and far planes that hold the whole model for a camera at the frame's distance.</summary>
    public static (float Near, float Far) Planes(CourseModel model, CourseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(model);
        // the camera is the frame's distance from the target, the model within its radius of its centre
        var aside = Vector3.Distance(frame.Target, model.Center);
        var near = MathF.Max(frame.Distance - aside - (model.Radius * 1.05f), model.Radius * 0.04f);
        return (near, frame.Distance + aside + (model.Radius * 1.05f));
    }

    /// <summary>A turning model's framing: aimed at its centre, at the least distance that holds every vertex inside
    /// the margin at every angle of the turn (one distance: it does not breathe as it turns).</summary>
    public static CourseFrame Turning(CourseModel model, float pitch, float halfFov, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(model);
        var distance = 0f;
        for (var i = 0; i < TurnSteps; i++)
        {
            distance = MathF.Max(distance, Fit(model, model.Center, i * 2 * MathF.PI / TurnSteps, pitch, halfFov, width, height));
        }
        return new CourseFrame(model.Center, distance);
    }

    /// <summary>A still's framing: aimed so that the model's picture has as much room left as right and above as below,
    /// at the least distance that holds every vertex inside the margin (a few rounds: under perspective the near side
    /// of the model looks larger, so the middle of its extent is not the middle of its picture).</summary>
    public static CourseFrame Centred(CourseModel model, float yaw, float pitch, float halfFov, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Positions.Length == 0)
        {
            return new CourseFrame(model.Center, 1);
        }
        Matrix4x4.Invert(Orientation(Vector3.Zero, yaw, pitch), out var back);
        var (sx, sy) = Lens(halfFov, width, height);
        var target = model.Center;
        var frame = new CourseFrame(target, Fit(model, target, yaw, pitch, halfFov, width, height));
        for (var round = 0; round < 12; round++)
        {
            // where the picture's extent is centred, in the picture's -1..1 across and up
            var (low, high) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
            var aimed = Orientation(frame.Target, yaw, pitch);
            foreach (var p in model.Positions)
            {
                var q = Vector3.Transform(p, aimed);
                var at = new Vector2(sx * q.X, sy * q.Y) / (frame.Distance - q.Z);
                (low, high) = (Vector2.Min(low, at), Vector2.Max(high, at));
            }
            var off = (low + high) / 2;
            if (MathF.Abs(off.X) < 1e-4f && MathF.Abs(off.Y) < 1e-4f)
            {
                break;
            }
            // moved across by as much as that is at the target's depth
            var move = new Vector3(off.X * frame.Distance / sx, off.Y * frame.Distance / sy, 0);
            target = frame.Target + Vector3.TransformNormal(move, back);
            frame = new CourseFrame(target, Fit(model, target, yaw, pitch, halfFov, width, height));
        }
        return frame;
    }

    /// <summary>The least distance back from <paramref name="target"/> at which every vertex lands inside the margin:
    /// a vertex at (x, y, z) in the camera's turned space lands at x·sx / (d − z) across, so d ≥ z + sx·|x| / (1 −
    /// margin), and the same up the picture.</summary>
    private static float Fit(CourseModel model, Vector3 target, float yaw, float pitch, float halfFov, int width, int height)
    {
        var (sx, sy) = Lens(halfFov, width, height);
        var turn = Orientation(target, yaw, pitch);
        var keep = 1 - Margin;
        var distance = model.Radius * 0.05f;
        foreach (var p in model.Positions)
        {
            var q = Vector3.Transform(p, turn);
            distance = MathF.Max(distance, q.Z + (MathF.Max(sx * MathF.Abs(q.X), sy * MathF.Abs(q.Y)) / keep));
            // never at or behind the camera
            distance = MathF.Max(distance, q.Z + (model.Radius * 0.05f));
        }
        return distance;
    }

    /// <summary>The lens's scale across and up (a point's x and y over its depth, to the picture's -1..1).</summary>
    private static (float X, float Y) Lens(float halfFov, int width, int height)
    {
        var lens = StudioCamera.Projection(halfFov, width, height, 1, 2);
        return (lens.M11, lens.M22);
    }

    private static Matrix4x4 Orientation(Vector3 target, float yaw, float pitch) =>
        Matrix4x4.CreateTranslation(-target) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch);
}
