using System.Numerics;
using Fzgx.GameCube.Models;

namespace Launcher.Core;

// The courses' models from the course-select screen: the race page's map and the Practice page's pictures.
public sealed partial class DiscArt
{
    /// <summary>A course's model from the course-select screen (<c>stage/selstNN.gma</c>, its grid left out): the
    /// track map, put on the course's road where the preview was stored moved (<see cref="PreviewPlacement"/>). Null
    /// when the file is missing or cannot be read.</summary>
    public CourseModel? Course(int course)
    {
        var file = $"stage/selst{course:00}.gma";
        byte[]? data;
        lock (_lock)
        {
            try
            {
                data = Bytes(file);
            }
            catch (Exception e) when (Unreadable(e))
            {
                data = null;
            }
        }
        if (data is null)
        {
            return null;
        }
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var offset = PreviewPlacement.Offset(course);
        try
        {
            foreach (var model in GmaReader.Read(data))
            {
                if (model is null || model.Name.Contains("GRID", StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (var mesh in model.Meshes)
                {
                    var first = positions.Count;
                    positions.AddRange(mesh.Vertices.Select(v => v.Position + offset));
                    normals.AddRange(mesh.Vertices.Select(v => v.Normal));
                    indices.AddRange(mesh.Indices.Select(i => first + i));
                }
            }
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
        return positions.Count == 0 ? null : new CourseModel([.. positions], [.. normals], [.. indices]);
    }

    /// <summary>The course models' files as the disc holds them (<c>stage/selstNN.gma</c>, packed or not), unread
    /// beyond their bytes: what the Practice page's stills are drawn from, for their cache's key
    /// (<see cref="CourseStillCache.DiscKey"/>). A course whose file is missing or cannot be read is left out.</summary>
    public IReadOnlyList<(string Name, byte[] Bytes)> CourseFiles(IEnumerable<int> courses)
    {
        ArgumentNullException.ThrowIfNull(courses);
        var files = new List<(string Name, byte[] Bytes)>();
        lock (_lock)
        {
            foreach (var course in courses)
            {
                var file = $"stage/selst{course:00}.gma";
                var name = Contains(file + ".lz") ? file + ".lz" : Contains(file) ? file : null;
                try
                {
                    if (name is not null)
                    {
                        files.Add((name, ReadFile(name)));
                    }
                }
                catch (Exception e) when (Unreadable(e))
                {
                    // left out
                }
            }
        }
        return files;
    }
}
