using System.Numerics;

namespace Launcher.Core;

/// <summary>
/// Where a course-select preview (<c>stage/selstNN.gma</c>) sits in its course: what is added to its vertices to put
/// it on the road, where the racers are. Most previews are made in the course's own coordinates and need nothing.
/// Five were moved to the middle of the world before they were stored (their grid's centre is 0, 0, 0): Multiplex,
/// Loop Cross, Split Oval, Surface Slide and Double Branches. The game's course select turns a preview around its own
/// middle and never needs the offset, so nothing on the disc gives it: these numbers were measured on the disc, by
/// fitting each preview to the road's centre line in its course file (<c>COLI_COURSENN</c>, the checkpoints'
/// centres) in X and Z, to the nearest unit. Height needs nothing. The real disc test in Launcher.Tests
/// (<c>RealDiscCourseMapTests</c>) holds every course of the pool to its road.
/// </summary>
public static class PreviewPlacement
{
    private static readonly Dictionary<int, Vector3> Offsets = new()
    {
        [5] = new(-526, 0, -1),      // Aeropolis: Multiplex
        [8] = new(1031, 0, -634),    // Lightning: Loop Cross
        [16] = new(-500, 0, 238),    // Casino Palace: Split Oval
        [26] = new(-346, 0, 716),    // Sand Ocean: Surface Slide
        [29] = new(-1137, 0, -332),  // Casino Palace: Double Branches
    };

    /// <summary>What a course's preview is moved by to lie on its road; zero for a preview made in place.</summary>
    public static Vector3 Offset(int course) => Offsets.GetValueOrDefault(course);
}
