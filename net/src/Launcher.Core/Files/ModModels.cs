using System.Numerics;
using System.Text.RegularExpressions;
using Fzgx.GameCube.Models;

namespace Launcher.Core;

/// <summary>
/// The guard's word on the models a race is driven on (the owner, 2026-11-05: "looks are free" is too loose there),
/// checked at the import against the disc's own and kept in the manifest: nothing of it costs a race. A course's own
/// models (<c>stage/stNN.gma</c>) are for the race only when their shape is the disc's: the same models by name, the
/// same meshes and triangles, each vertex where the disc has it within <see cref="TrackTolerance"/> of the course's
/// size. Textures, colours, lighting, materials and texture coordinates are free (a re-skin changes the <c>.tpl</c>
/// only). Backgrounds (<c>bg/</c>, nothing to collide with) and machines' models stay free; a course whose models the
/// disc has none of (a new slot) is its course file's to answer.
/// </summary>
public static partial class ModModels
{
    /// <summary>How far a vertex of a course's model may be from the disc's: this share of the course's size (the
    /// largest reach of its models from the origin), well above the rounding of a float of that size (one in eight
    /// million) and far below what shows.</summary>
    public const double TrackTolerance = 1e-5;

    public const string TrackWhy = "Track shape differs: single player only";

    [GeneratedRegex(@"^stage/st\d+\.gma(\.lz)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrackModel();

    /// <summary>The word on a course's model file (<paramref name="disc"/> null: the disc has no such file); null for
    /// any other file, which the class's own check speaks for.</summary>
    public static ModVerdict? Check(string discPath, byte[] mod, byte[]? disc)
    {
        ArgumentNullException.ThrowIfNull(discPath);
        ArgumentNullException.ThrowIfNull(mod);
        var path = discPath.Replace('\\', '/').TrimStart('/');
        if (!TrackModel().IsMatch(path))
        {
            return null;
        }
        return disc is null || SameShape(Models(mod), Models(disc)) ? ModVerdict.Anywhere : new ModVerdict(false, TrackWhy);
    }

    /// <summary>A model file's models (packed or not); null when it cannot be read.</summary>
    internal static IReadOnlyList<GmaModel?>? Models(byte[] file)
    {
        try
        {
            return ModGuard.Unpacked(file) is { } data ? GmaReader.Read(data) : null;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException
                                      or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Two courses' models have one shape: the same models by name, meshes and triangles, every vertex within
    /// the tolerance. A model whose geometry cannot be read counts as changed.</summary>
    internal static bool SameShape(IReadOnlyList<GmaModel?>? mod, IReadOnlyList<GmaModel?>? disc)
    {
        if (mod is null || disc is null || mod.Count != disc.Count)
        {
            return false;
        }
        var reach = disc.Where(m => m is { UnsupportedReason: null }).Select(m => m!.BoundingCenter.Length() + m.BoundingRadius).DefaultIfEmpty(1).Max();
        var tolerance = (float)Math.Max(reach * TrackTolerance, 1e-4);
        for (var i = 0; i < disc.Count; i++)
        {
            var (a, b) = (mod[i], disc[i]);
            if (a is null || b is null)
            {
                if (a is not null || b is not null)
                {
                    return false;
                }
                continue;
            }
            if (a.Name != b.Name || a.UnsupportedReason is not null || b.UnsupportedReason is not null || a.Meshes.Length != b.Meshes.Length)
            {
                return false;
            }
            for (var k = 0; k < a.Meshes.Length; k++)
            {
                var (ma, mb) = (a.Meshes[k], b.Meshes[k]);
                if (ma.Vertices.Length != mb.Vertices.Length || !ma.Indices.AsSpan().SequenceEqual(mb.Indices.AsSpan()))
                {
                    return false;
                }
                for (var v = 0; v < ma.Vertices.Length; v++)
                {
                    var d = Vector3.Abs(ma.Vertices[v].Position - mb.Vertices[v].Position);
                    if (d.X > tolerance || d.Y > tolerance || d.Z > tolerance)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }
}
