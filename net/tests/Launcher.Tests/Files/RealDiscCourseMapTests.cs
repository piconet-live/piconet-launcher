using System.Buffers.Binary;
using System.Numerics;
using Fzgx.GameCube.Compression;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The race page's map against the game itself, on your own disc image where this computer has it (skipped
/// where it does not): for every course of the pool, the road's centre line from the course's own file lies on the
/// map (<see cref="DiscArt.Course"/>), seen from above. The racers' dots are the machines' places in the course's
/// coordinates, the same as the course file's, so a map that the centre line misses puts the dots beside the road.
/// Players reported it on Multiplex and Loop Cross: five previews were stored moved to the middle of the
/// world (<see cref="PreviewPlacement"/>). Nothing is taken from the disc into the repository.
/// </summary>
public sealed class RealDiscCourseMapTests
{
    /// <summary>A share of the centre line the map may miss: where the course splits into more roads than its preview
    /// draws (Double Branches' lanes between its bands, an eighth of its centres; Meteor Stream and Spiral, under one
    /// in thirty). Before the five previews were put in place, they missed 60 to 100 per cent.</summary>
    private const double Missed = 0.15;

    public static TheoryData<int> Pool() => [.. Courses.Pool];

    [Theory]
    [MemberData(nameof(Pool))]
    public void TheRoadsCentreLineLiesOnTheMap(int course)
    {
        var path = RealDiscStatsTests.PlayersDisc();
        Assert.SkipWhen(path is null || !File.Exists(path), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(path);
        Assert.NotNull(art);
        var map = art.Course(course);
        Assert.NotNull(map);
        var road = RoadCentres(path!, course);
        Assert.NotEmpty(road);

        // on the road: within half the road's width of the map's triangles, seen from above (a dot anywhere across the
        // road, at any height: the map shows the road's band, and a pipe's centre is inside its band)
        var triangles = Enumerable.Range(0, map.Indices.Length / 3)
            .Select(t => (Flat(map.Positions[map.Indices[3 * t]]), Flat(map.Positions[map.Indices[(3 * t) + 1]]), Flat(map.Positions[map.Indices[(3 * t) + 2]])))
            .ToArray();
        var off = road.Where(r => triangles.Min(t => Distance(Flat(r.Centre), t)) > r.Width / 2).ToList();

        Assert.True(off.Count <= road.Count * Missed,
            $"course {course} ({Online.Protocol.CourseNames.Name(course)}): {off.Count} of {road.Count} centres of the road are off the map, " +
            $"for example {string.Join(", ", off.Take(3).Select(r => r.Centre.ToString("F0", null)))}");
    }

    /// <summary>The road's centre where each checkpoint of the course file starts, with the road's width there
    /// (as the course file lays it out): a list of track nodes (count and place at
    /// 0x08), each with its checkpoints (count, place); a checkpoint of 0x50 bytes holds its start plane's centre at
    /// 0x18 and the width at 0x48.</summary>
    private static List<(Vector3 Centre, float Width)> RoadCentres(string path, int course)
    {
        using var image = DiscImage.Open(path);
        var name = $"stage/COLI_COURSE{course:00}.lz";
        var file = image.Table().Files.Single(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));
        var packed = new byte[file.Length];
        image.Read(file.Offset, packed, name);
        var data = AvLz.Decompress(packed);
        int Int(int at) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at));
        float Float(int at) => BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(at));
        var road = new List<(Vector3, float)>();
        var (nodes, nodesAt) = (Int(0x08), Int(0x0C));
        for (var n = 0; n < nodes; n++)
        {
            var (count, at) = (Int(nodesAt + (n * 12)), Int(nodesAt + (n * 12) + 4));
            for (var c = 0; c < count; c++)
            {
                var checkpoint = at + (c * 0x50);
                road.Add((new Vector3(Float(checkpoint + 0x18), Float(checkpoint + 0x1C), Float(checkpoint + 0x20)), Float(checkpoint + 0x48)));
            }
        }
        return road;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// <summary>How far a point is from a triangle, in the plane: 0 inside it.</summary>
    private static float Distance(Vector2 p, (Vector2 A, Vector2 B, Vector2 C) t)
    {
        static float Cross(Vector2 u, Vector2 v) => (u.X * v.Y) - (u.Y * v.X);
        var (s1, s2, s3) = (Cross(t.B - t.A, p - t.A), Cross(t.C - t.B, p - t.B), Cross(t.A - t.C, p - t.C));
        if ((s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0))
        {
            return 0;
        }
        float Edge(Vector2 a, Vector2 b)
        {
            var d = b - a;
            var k = d == Vector2.Zero ? 0 : Math.Clamp(Vector2.Dot(p - a, d) / d.LengthSquared(), 0, 1);
            return Vector2.Distance(p, a + (k * d));
        }
        return MathF.Min(Edge(t.A, t.B), MathF.Min(Edge(t.B, t.C), Edge(t.C, t.A)));
    }
}
