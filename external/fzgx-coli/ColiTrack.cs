using System.Collections.Immutable;
using System.Numerics;

namespace Fzgx.Coli;

/// <summary>What a track segment is; a segment with none of these flags is an embedded property.</summary>
[Flags]
public enum ColiSegmentKind : byte
{
    /// <summary>An embedded property (strip, open pipe, modulated road); see <see cref="ColiEmbeddedProperty"/>.</summary>
    Embedded = 0,

    /// <summary>A pipe (driven inside) or, with <see cref="ColiPipeShape.Cylinder"/>, a cylinder (driven outside).</summary>
    PipeOrCylinder = 0x01,

    /// <summary>A road surface; carries the rails.</summary>
    Track = 0x02,

    /// <summary>One of several parallel children, told apart by <see cref="ColiTrackSegment.BranchIndex"/>.</summary>
    Branch = 0x04,

    /// <summary>A transform only; always has children.</summary>
    Matrix = 0x08,
}

/// <summary>What an embedded segment adds to its parent's surface.</summary>
[Flags]
public enum ColiEmbeddedProperty : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Damages machines (lava).</summary>
    Damage = 0x02,

    /// <summary>Dirt: slows machines.</summary>
    Dirt = 0x04,

    /// <summary>Slippery surface (ice).</summary>
    Slip = 0x08,

    /// <summary>Recovers energy (the pit strips).</summary>
    Recover = 0x10,

    /// <summary>A road whose cross-section is shaped by curves.</summary>
    Modulated = 0x20,

    /// <summary>A capsule-shaped pipe.</summary>
    CapsulePipe = 0x40,

    /// <summary>A pipe or cylinder that is open along its length (a half pipe).</summary>
    OpenPipeOrCylinder = 0x80,
}

/// <summary>Rails and turn hints of a segment.</summary>
[Flags]
public enum ColiPerimeter : byte
{
    /// <summary>No rails.</summary>
    None = 0,

    /// <summary>A left rail, as high as <see cref="ColiTrackSegment.RailLeft"/>.</summary>
    RailLeft = 0x04,

    /// <summary>A right rail, as high as <see cref="ColiTrackSegment.RailRight"/>.</summary>
    RailRight = 0x08,

    /// <summary>Turn hint, probably for the computer drivers.</summary>
    RightTurn = 0x10,

    /// <summary>Turn hint, probably for the computer drivers.</summary>
    LeftTurn = 0x20,
}

/// <summary>Pipe and cylinder details.</summary>
[Flags]
public enum ColiPipeShape : byte
{
    /// <summary>A pipe, driven inside.</summary>
    None = 0,

    /// <summary>A cylinder, driven outside.</summary>
    Cylinder = 0x01,

    /// <summary>Open along its length.</summary>
    Open = 0x02,
}

/// <summary>One key of an animation curve.</summary>
/// <param name="Mode">
/// How the span starting at this key is interpolated: 0 holds the value
/// (assumed), 1 is linear, 2 is a cubic Hermite spline using the tangents.
/// Mode 3 also occurs and is treated like 2 until shown otherwise.
/// </param>
/// <param name="Time">Curve time; its unit and range differ between segments.</param>
/// <param name="Value">The value at <paramref name="Time"/>.</param>
/// <param name="TangentIn">Slope arriving at the key, in value per time unit.</param>
/// <param name="TangentOut">Slope leaving the key, in value per time unit.</param>
public readonly record struct ColiKey(uint Mode, float Time, float Value, float TangentIn, float TangentOut);

/// <summary>An animation curve: keys in time order. Empty means "use the segment's fallback value".</summary>
/// <param name="Keys">The keys.</param>
public sealed record ColiCurve(ImmutableArray<ColiKey> Keys)
{
    /// <summary>A curve without keys.</summary>
    public static ColiCurve Empty { get; } = new(ImmutableArray<ColiKey>.Empty);

    /// <summary>
    /// The value at <paramref name="time"/>, or <paramref name="fallback"/> without keys.
    /// Times before the first or after the last key hold the end value.
    /// </summary>
    public float Evaluate(float time, float fallback)
    {
        var keys = Keys;
        if (keys.IsDefaultOrEmpty)
        {
            return fallback;
        }
        if (time <= keys[0].Time)
        {
            return keys[0].Value;
        }
        if (time >= keys[^1].Time)
        {
            return keys[^1].Value;
        }
        var i = 0;
        while (keys[i + 1].Time < time)
        {
            i++;
        }
        var (a, b) = (keys[i], keys[i + 1]);
        var span = b.Time - a.Time;
        var u = (time - a.Time) / span;
        switch (a.Mode)
        {
            case 0:
                return a.Value;
            case 1:
                return a.Value + ((b.Value - a.Value) * u);
            default:
                var u2 = u * u;
                var u3 = u2 * u;
                return (((2 * u3) - (3 * u2) + 1) * a.Value)
                    + ((u3 - (2 * u2) + u) * a.TangentOut * span)
                    + (((-2 * u3) + (3 * u2)) * b.Value)
                    + ((u3 - u2) * b.TangentIn * span);
        }
    }
}

/// <summary>
/// One node of the course's track tree. Its nine curves animate scale,
/// rotation (degrees) and position over curve time; a segment's world
/// transform is its own transform times its parent's.
/// </summary>
/// <param name="Offset">Where the segment is in the file; identifies it.</param>
/// <param name="Kind">What the segment is.</param>
/// <param name="Property">What an embedded segment adds.</param>
/// <param name="Perimeter">Rails and turn hints.</param>
/// <param name="Pipe">Pipe and cylinder details.</param>
/// <param name="Curves">Scale X, Y, Z, rotation X, Y, Z, position X, Y, Z.</param>
/// <param name="FallbackScale">Scale used where a scale curve is empty.</param>
/// <param name="FallbackRotation">Rotation in degrees used where a rotation curve is empty.</param>
/// <param name="FallbackPosition">Position used where a position curve is empty.</param>
/// <param name="Unknown38">Unknown value at 0x38; often 0.</param>
/// <param name="Unknown3A">Unknown value at 0x3A.</param>
/// <param name="RailLeft">Left rail height in meters.</param>
/// <param name="RailRight">Right rail height in meters.</param>
/// <param name="BranchIndex">Which parallel path the segment belongs to, 0 to 3.</param>
/// <param name="HasCorner">True if the segment carries a corner record (probably a hint for computer drivers).</param>
/// <param name="Children">Child segments.</param>
public sealed record ColiTrackSegment(
    int Offset,
    ColiSegmentKind Kind,
    ColiEmbeddedProperty Property,
    ColiPerimeter Perimeter,
    ColiPipeShape Pipe,
    ImmutableArray<ColiCurve> Curves,
    Vector3 FallbackScale,
    Vector3 FallbackRotation,
    Vector3 FallbackPosition,
    ushort Unknown38,
    ushort Unknown3A,
    float RailLeft,
    float RailRight,
    int BranchIndex,
    bool HasCorner,
    ImmutableArray<ColiTrackSegment> Children)
{
    /// <summary>True for embedded properties, which decorate their parent's surface rather than place it.</summary>
    public bool IsEmbedded => Kind == ColiSegmentKind.Embedded;

    /// <summary>Scale at <paramref name="time"/>; X is the road width in meters.</summary>
    public Vector3 ScaleAt(float time) => new(
        Curves[0].Evaluate(time, FallbackScale.X),
        Curves[1].Evaluate(time, FallbackScale.Y),
        Curves[2].Evaluate(time, FallbackScale.Z));

    /// <summary>Rotation in degrees at <paramref name="time"/>.</summary>
    public Vector3 RotationAt(float time) => new(
        Curves[3].Evaluate(time, FallbackRotation.X),
        Curves[4].Evaluate(time, FallbackRotation.Y),
        Curves[5].Evaluate(time, FallbackRotation.Z));

    /// <summary>Position at <paramref name="time"/>.</summary>
    public Vector3 PositionAt(float time) => new(
        Curves[6].Evaluate(time, FallbackPosition.X),
        Curves[7].Evaluate(time, FallbackPosition.Y),
        Curves[8].Evaluate(time, FallbackPosition.Z));

    /// <summary>
    /// The segment's own pose at <paramref name="time"/> as the game builds
    /// it (fn_1_18214 in main.rel): rotation about X, Y and Z in that order,
    /// then translation by its position multiplied, axis by axis, by the
    /// scale its ancestors hand down, <paramref name="inheritedScale"/>.
    /// Scale never enters the matrix, so a scaled parent does not shear a
    /// rotated child (row-vector convention, like
    /// <see cref="ColiSceneReader.ComposeTrs"/>).
    /// </summary>
    public Matrix4x4 PoseAt(float time, Vector3 inheritedScale) =>
        ColiSceneReader.ComposeTrs(PositionAt(time) * inheritedScale, RotationAt(time) * (MathF.PI / 180), Vector3.One);
}

/// <summary>
/// Where a segment is at a curve time, composed as the game does: an
/// orthonormal <paramref name="Pose"/> (the segment's pose times its
/// parent's) and the <paramref name="Scale"/> accumulated along the chain,
/// axis by axis. For a road, the pose's origin is the track centre, its
/// -Z axis points forward (or backward, on a piece built facing the other
/// way) and Scale.X is the width.
/// </summary>
/// <param name="Segment">The segment.</param>
/// <param name="Pose">Rotation and translation to world space, without scale.</param>
/// <param name="Scale">Accumulated scale: its own times its ancestors', axis by axis.</param>
public readonly record struct ColiFrame(ColiTrackSegment Segment, Matrix4x4 Pose, Vector3 Scale)
{
    /// <summary>The pose with the scale applied: from the segment's unit coordinates (±0.5 across) to world space.</summary>
    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Pose;

    /// <summary>The frame's origin in world space.</summary>
    public Vector3 Origin => Pose.Translation;

    /// <summary>The frame's -Z axis in world space, a unit vector.</summary>
    public Vector3 Forward => Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, Pose));

    /// <summary>The frame of <paramref name="child"/> (placed or embedded) inside this one at <paramref name="time"/>.</summary>
    public ColiFrame Child(ColiTrackSegment child, float time)
    {
        ArgumentNullException.ThrowIfNull(child);
        return new ColiFrame(child, child.PoseAt(time, Scale) * Pose, Scale * child.ScaleAt(time));
    }
}

/// <summary>A plane bounding a checkpoint.</summary>
/// <param name="Distance">The plane's distance term, as stored.</param>
/// <param name="Normal">Unit normal: forward for a start plane, backward for an end plane.</param>
/// <param name="Origin">A point on the plane: the track centre where the checkpoint begins or ends.</param>
public readonly record struct ColiPlane(float Distance, Vector3 Normal, Vector3 Origin);

/// <summary>A slice of track that the game uses to tell where a machine is.</summary>
/// <param name="TimeStart">Curve time where the slice starts, on its node's root segment.</param>
/// <param name="TimeEnd">Curve time where the slice ends.</param>
/// <param name="Start">Plane at the start, facing forward.</param>
/// <param name="End">Plane at the end, facing backward.</param>
/// <param name="DistanceStart">Distance along the lap where the slice starts, in meters.</param>
/// <param name="DistanceEnd">Distance along the lap where the slice ends, in meters.</param>
/// <param name="Width">Track width in meters.</param>
/// <param name="ConnectsIn">False if there is a gap before the slice.</param>
/// <param name="ConnectsOut">False if there is a gap after the slice.</param>
public sealed record ColiCheckpoint(
    float TimeStart,
    float TimeEnd,
    ColiPlane Start,
    ColiPlane End,
    float DistanceStart,
    float DistanceEnd,
    float Width,
    bool ConnectsIn,
    bool ConnectsOut);

/// <summary>One stretch of track: its root segment and one checkpoint per branch.</summary>
/// <param name="Root">Index into <see cref="ColiTrack.Roots"/>.</param>
/// <param name="Checkpoints">One checkpoint where the track is single, more where it branches.</param>
public sealed record ColiTrackNode(int Root, ImmutableArray<ColiCheckpoint> Checkpoints);

/// <summary>A course's track: root segments in file order and the track nodes that refer to them.</summary>
/// <param name="Roots">Root segments, each the start of a segment tree.</param>
/// <param name="Nodes">Track nodes in lap order.</param>
public sealed record ColiTrack(ImmutableArray<ColiTrackSegment> Roots, ImmutableArray<ColiTrackNode> Nodes)
{
    /// <summary>
    /// The frames of the paths through <paramref name="root"/> at
    /// <paramref name="time"/>: for every chain of non-embedded segments from
    /// the root down to one whose children are all embedded (or that has
    /// none), that last segment's frame. One frame where the track is single,
    /// one per branch where it splits.
    /// </summary>
    public static IReadOnlyList<ColiFrame> FramesAt(ColiTrackSegment root, float time)
    {
        ArgumentNullException.ThrowIfNull(root);
        var frames = new List<ColiFrame>();
        Collect(new ColiFrame(root, root.PoseAt(time, Vector3.One), root.ScaleAt(time)), time, frames);
        return frames;
    }

    /// <summary>
    /// The frame of the segment that <paramref name="root"/>'s branches hang
    /// from at <paramref name="time"/> (the deepest one above them all), or
    /// the root's own frame where there are none. A split node's first
    /// checkpoint is taken from it.
    /// </summary>
    public static ColiFrame SplitFrameAt(ColiTrackSegment root, float time)
    {
        ArgumentNullException.ThrowIfNull(root);
        var frame = new ColiFrame(root, root.PoseAt(time, Vector3.One), root.ScaleAt(time));
        while (frame.Segment.Children.Where(child => !child.IsEmbedded).ToList() is [var only] && !only.Kind.HasFlag(ColiSegmentKind.Branch))
        {
            frame = frame.Child(only, time);
        }
        return frame;
    }

    private static void Collect(ColiFrame frame, float time, List<ColiFrame> frames)
    {
        var placed = frame.Segment.Children.Where(child => !child.IsEmbedded).ToList();
        if (placed.Count == 0)
        {
            frames.Add(frame);
            return;
        }
        foreach (var child in placed)
        {
            Collect(frame.Child(child, time), time, frames);
        }
    }
}
