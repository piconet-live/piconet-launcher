using System.Numerics;

namespace Launcher.Core;

/// <summary>
/// Where the garage's studio looks from, without drawing: a model framed on a turntable, turned and seen from a little
/// above, and the lens that puts it on screen (the graphics card draws it). Matrices are System.Numerics' (a point
/// times the matrix); the projection gives OpenGL's clip space (depth -1 at the near plane, 1 at the far one).
/// </summary>
public static class StudioCamera
{
    /// <summary>The studio's lens: 30 degrees across the shorter side of the view.</summary>
    public const float FieldOfView = 30 * MathF.PI / 180;

    /// <summary>How far the camera stands from the model's centre so that its whole <paramref name="radius"/> fits
    /// the shorter side at any turn (<paramref name="zoom"/> 1), or closer by the zoom.</summary>
    public static float Distance(float radius, float zoom) => radius / MathF.Sin(FieldOfView / 2) / Math.Max(0.1f, zoom);

    /// <summary>The model's space to the camera's (x right, y up, looking down -z): turned <paramref name="yaw"/>
    /// radians about its upright axis (0 shows its tail), seen from <paramref name="pitch"/> radians above, its centre
    /// straight ahead at <see cref="Distance"/>.</summary>
    public static Matrix4x4 View(Vector3 center, float radius, float yaw, float pitch, float zoom) =>
        Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch)
        * Matrix4x4.CreateTranslation(0, 0, -Distance(radius, zoom));

    /// <summary>A perspective lens whose <paramref name="halfFov"/> spans half the shorter side of a
    /// <paramref name="width"/> by <paramref name="height"/> view, seeing from <paramref name="near"/> to
    /// <paramref name="far"/> in front of the camera.</summary>
    public static Matrix4x4 Projection(float halfFov, int width, int height, float near, float far)
    {
        var focal = 1 / MathF.Tan(halfFov);
        var (w, h) = (Math.Max(1, width), Math.Max(1, height));
        var (sx, sy) = w >= h ? (focal * h / w, focal) : (focal, focal * w / h);
        return new Matrix4x4(
            sx, 0, 0, 0,
            0, sy, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2 * far * near / (near - far), 0);
    }

    /// <summary>Where a point lands on a <paramref name="width"/> by <paramref name="height"/> view (pixels from the
    /// top left) through <paramref name="viewProjection"/>, and how far in front of the camera it is (its clip w;
    /// 0 or less behind it).</summary>
    public static (Vector2 At, float Depth) Project(Vector3 point, Matrix4x4 viewProjection, float width, float height)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        var w = Math.Max(clip.W, 1e-6f);
        return (new Vector2((clip.X / w + 1) * width / 2, (1 - clip.Y / w) * height / 2), clip.W);
    }
}

/// <summary>
/// The ground under a machine, in the model's space: just below its lowest point, the reach of its square (a little
/// wider than the machine is long; the ring the square was sized for is not drawn) and its shadow's half
/// lengths (its footprint), about its centre. Drawn as a square on the ground (<see cref="Corners"/>) whose colour
/// is worked out where each pixel meets it: the machine's soft shadow, nothing else, so it stays under the machine
/// as it turns and tilts (no ring, no light under it: direction D).
/// </summary>
public readonly record struct TurntableFloor(Vector3 Center, float Ground, float Ring, Vector2 Footprint)
{
    /// <summary>How far out the square reaches, in rings: past the shadow's fade (1.15), with room kept as before.</summary>
    public const float Reach = 1.6f;

    public static TurntableFloor Of(MachineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var positions = model.Meshes.SelectMany(m => m.Positions).ToList();
        if (positions.Count == 0)
        {
            return new TurntableFloor(model.Center, 0, 1, Vector2.One);
        }
        var c = model.Center;
        return new TurntableFloor(c,
            positions.Min(p => p.Y) - (0.02f * model.Radius),
            1.06f * positions.Max(p => new Vector2(p.X - c.X, p.Z - c.Z).Length()),
            new Vector2(positions.Max(p => MathF.Abs(p.X - c.X)), positions.Max(p => MathF.Abs(p.Z - c.Z))));
    }

    /// <summary>The square's four corners on the ground, turning counter-clockwise seen from above.</summary>
    public Vector3[] Corners
    {
        get
        {
            var r = Ring * Reach;
            return
            [
                new(Center.X - r, Ground, Center.Z + r), new(Center.X + r, Ground, Center.Z + r),
                new(Center.X + r, Ground, Center.Z - r), new(Center.X - r, Ground, Center.Z - r),
            ];
        }
    }
}
