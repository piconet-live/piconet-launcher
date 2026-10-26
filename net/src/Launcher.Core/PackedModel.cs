using System.Numerics;

namespace Launcher.Core;

/// <summary>One mesh of a <see cref="PackedModel"/>: how it is drawn and its run of <see cref="PackedModel.Indices"/>.</summary>
public readonly record struct PackedMesh(MachineMaterial Material, int First, int Count);

/// <summary>A run of a frame's see-through triangles (<see cref="PackedModel.Translucent"/>) sharing a mesh.</summary>
public readonly record struct PackedRun(int Mesh, int First, int Count);

/// <summary>
/// A model laid out as the graphics card takes it: every vertex's numbers one after the other (<see cref="Stride"/>
/// floats each), three indices a triangle, mesh after mesh, each triangle turning counter-clockwise when its front
/// faces the camera (OpenGL's front). A vertex is its position, normal, texture coordinates and colour (12 floats).
/// See-through triangles are drawn last, far to near, so they are kept apart with their middles for sorting each frame
/// (<see cref="Translucent"/>).
/// </summary>
public sealed class PackedModel
{
    /// <summary>Floats a vertex.</summary>
    public const int PlainStride = 12;

    private readonly (int Mesh, int First, Vector3 Middle)[] _translucent;
    private (float Depth, int Mesh, int First)[] _order = [];

    private PackedModel(int stride, float[] vertices, uint[] indices, PackedMesh[] meshes, Vector3[] places)
    {
        Stride = stride;
        Vertices = vertices;
        Indices = indices;
        Meshes = meshes;
        var translucent = new List<(int, int, Vector3)>();
        for (var m = 0; m < meshes.Length; m++)
        {
            if (!meshes[m].Material.Translucent)
            {
                continue;
            }
            for (var i = meshes[m].First; i + 2 < meshes[m].First + meshes[m].Count; i += 3)
            {
                translucent.Add((m, i, (places[indices[i]] + places[indices[i + 1]] + places[indices[i + 2]]) / 3));
            }
        }
        _translucent = [.. translucent];
    }

    public int Stride { get; }

    public float[] Vertices { get; }

    public uint[] Indices { get; }

    public IReadOnlyList<PackedMesh> Meshes { get; }

    /// <summary>How many see-through triangles there are.</summary>
    public int TranslucentTriangles => _translucent.Length;

    /// <summary>A model packed.</summary>
    public static PackedModel Of(MachineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var vertices = new List<float>();
        var indices = new List<uint>();
        var meshes = new List<PackedMesh>();
        foreach (var mesh in model.Meshes)
        {
            var first = (uint)(vertices.Count / PlainStride);
            for (var v = 0; v < mesh.Positions.Length; v++)
            {
                Add(vertices, mesh.Positions[v]);
                Add(vertices, mesh.Normals[v]);
                vertices.Add(mesh.Uvs[v].X);
                vertices.Add(mesh.Uvs[v].Y);
                Add(vertices, mesh.Colors[v]);
            }
            meshes.Add(new PackedMesh(mesh.Material, indices.Count, AddTriangles(indices, mesh.Indices, first,
                Flipped(mesh.Indices, mesh.Positions, mesh.Normals))));
        }
        return new PackedModel(PlainStride, [.. vertices], [.. indices], [.. meshes], [.. model.Meshes.SelectMany(m => m.Positions)]);
    }

    /// <summary>
    /// This frame's see-through triangles far to near as the camera <paramref name="modelView"/> sees them (by their
    /// middles): their indices into <paramref name="indices"/> (grown as needed; returns the count) in runs of one mesh
    /// each, into <paramref name="runs"/> (cleared first).
    /// </summary>
    public int Translucent(Matrix4x4 modelView, ref uint[] indices, List<PackedRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        runs.Clear();
        if (_order.Length != _translucent.Length)
        {
            _order = new (float, int, int)[_translucent.Length];
        }
        for (var t = 0; t < _translucent.Length; t++)
        {
            var (mesh, first, middle) = _translucent[t];
            _order[t] = (Vector3.Transform(middle, modelView).Z, mesh, first);
        }
        // the camera looks down -z: the farthest is the most negative
        Array.Sort(_order, (a, b) => a.Depth.CompareTo(b.Depth));
        if (indices.Length < _order.Length * 3)
        {
            indices = new uint[_order.Length * 3];
        }
        for (var t = 0; t < _order.Length; t++)
        {
            var (_, mesh, first) = _order[t];
            indices[t * 3] = Indices[first];
            indices[(t * 3) + 1] = Indices[first + 1];
            indices[(t * 3) + 2] = Indices[first + 2];
            if (runs.Count > 0 && runs[^1].Mesh == mesh)
            {
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 3 };
            }
            else
            {
                runs.Add(new PackedRun(mesh, t * 3, 3));
            }
        }
        return _order.Length * 3;
    }

    /// <summary>Whether a mesh's triangles turn the other way from OpenGL's front: the model files' triangles turn one
    /// way, and which way is front is read from the normals (most triangles agree).</summary>
    public static bool Flipped(int[] indices, Vector3[] positions, Vector3[] normals)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        var agree = 0;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            agree += Vector3.Dot(face, normals[a] + normals[b] + normals[c]) >= 0 ? 1 : -1;
        }
        return agree < 0;
    }

    private static int AddTriangles(List<uint> into, int[] indices, uint first, bool flip)
    {
        var count = indices.Length / 3 * 3;
        for (var i = 0; i < count; i += 3)
        {
            into.Add(first + (uint)indices[i]);
            into.Add(first + (uint)indices[i + (flip ? 2 : 1)]);
            into.Add(first + (uint)indices[i + (flip ? 1 : 2)]);
        }
        return count;
    }

    private static void Add(List<float> into, Vector3 v)
    {
        into.Add(v.X);
        into.Add(v.Y);
        into.Add(v.Z);
    }

    private static void Add(List<float> into, Vector4 v)
    {
        into.Add(v.X);
        into.Add(v.Y);
        into.Add(v.Z);
        into.Add(v.W);
    }
}
