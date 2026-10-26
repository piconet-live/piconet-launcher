using System.Numerics;

namespace Launcher.Core;

/// <summary>
/// A course's turning model from the course-select screen (<c>stage/selstNN.gma</c>: a low-detail band of the road,
/// about 1,000 vertices, in the course's own world coordinates, the same as the racers' positions, once the five
/// previews stored moved are put back on their road, <see cref="PreviewPlacement"/>): the track map.
/// Triangles as three indices each.
/// </summary>
public sealed record CourseModel(Vector3[] Positions, Vector3[] Normals, int[] Indices)
{
    /// <summary>The middle of the model's extent (what the map turns around).</summary>
    public Vector3 Center { get; } = Positions.Length == 0 ? Vector3.Zero
        : (Positions.Aggregate(Vector3.Min) + Positions.Aggregate(Vector3.Max)) / 2;

    /// <summary>The farthest vertex's distance from <see cref="Center"/> (what the map frames).</summary>
    public float Radius { get; } = Positions.Length == 0 ? 1
        : MathF.Max(1, Positions.Max(p => Vector3.Distance(p, (Positions.Aggregate(Vector3.Min) + Positions.Aggregate(Vector3.Max)) / 2)));

    /// <summary>The light the map is shaded by: from above, a little to one side.</summary>
    public static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.4f, 1f, 0.3f));

    /// <summary>The map's triangles each with its own three corners (so each is one flat shade) and its shade under
    /// <see cref="Light"/>, from 0.55 edge-on to 1 facing it (either side up: the model's triangles turn both
    /// ways).</summary>
    public (Vector3[] Corners, float[] Shades) Flat()
    {
        var triangles = Indices.Length / 3;
        var corners = new Vector3[triangles * 3];
        var shades = new float[triangles];
        for (var t = 0; t < triangles; t++)
        {
            var (a, b, c) = (Positions[Indices[3 * t]], Positions[Indices[(3 * t) + 1]], Positions[Indices[(3 * t) + 2]]);
            (corners[3 * t], corners[(3 * t) + 1], corners[(3 * t) + 2]) = (a, b, c);
            var normal = Vector3.Cross(b - a, c - a);
            var lit = normal == Vector3.Zero ? 0.5f : MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), Light));
            shades[t] = 0.55f + (0.45f * lit);
        }
        return (corners, shades);
    }
}
