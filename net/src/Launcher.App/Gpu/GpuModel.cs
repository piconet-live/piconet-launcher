using System.Numerics;
using System.Runtime.CompilerServices;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>
/// A <see cref="PackedModel"/> on the graphics card: its vertices and indices uploaded once, with a second index
/// buffer for each frame's see-through triangles, far to near. Belongs to the context that made it.
/// </summary>
public sealed class GpuMesh
{
    private readonly Gl _gl;
    private readonly int _vertices;
    private readonly int _indices;
    private readonly int _sorted;
    private readonly int _vao;
    private readonly int _sortedVao;
    private readonly List<PackedRun> _runs = [];
    private uint[] _order = [];

    public GpuMesh(Gl gl, PackedModel model)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(model);
        _gl = gl;
        Model = model;
        var api = gl.Api;
        // index buffers bind into the bound vertex array: none while making them
        api.BindVertexArray(0);
        _vertices = gl.Buffer(Gl.ArrayBuffer, (ReadOnlySpan<float>)model.Vertices, Gl.StaticDraw);
        _indices = gl.Buffer(Gl.ElementArrayBuffer, (ReadOnlySpan<uint>)model.Indices, Gl.StaticDraw);
        _sorted = gl.Buffer(Gl.ElementArrayBuffer, ReadOnlySpan<uint>.Empty, Gl.DynamicDraw);
        _vao = Layout(_indices);
        _sortedVao = Layout(_sorted);
        api.BindVertexArray(0);
    }

    public PackedModel Model { get; }

    /// <summary>The see-through triangles' runs as last sorted (<see cref="Sort"/>).</summary>
    public IReadOnlyList<PackedRun> Runs => _runs;

    /// <summary>Binds the vertices with the meshes' own indices.</summary>
    public void Bind() => _gl.Api.BindVertexArray(_vao);

    /// <summary>Sorts the see-through triangles far to near for this camera and binds them (see <see cref="Runs"/>).</summary>
    public void Sort(Matrix4x4 modelView)
    {
        var count = Model.Translucent(modelView, ref _order, _runs);
        _gl.Api.BindVertexArray(_sortedVao);
        _gl.Fill(Gl.ElementArrayBuffer, (ReadOnlySpan<uint>)_order.AsSpan(0, count), Gl.DynamicDraw);
    }

    public void Delete()
    {
        var api = _gl.Api;
        api.DeleteVertexArray(_vao);
        api.DeleteVertexArray(_sortedVao);
        api.DeleteBuffer(_vertices);
        api.DeleteBuffer(_indices);
        api.DeleteBuffer(_sorted);
    }

    /// <summary>A vertex array of the vertices' inputs (in the order <see cref="Studio"/> binds them) and these
    /// indices.</summary>
    private int Layout(int indices)
    {
        var api = _gl.Api;
        var vao = api.GenVertexArray();
        api.BindVertexArray(vao);
        api.BindBuffer(Gl.ArrayBuffer, _vertices);
        api.BindBuffer(Gl.ElementArrayBuffer, indices);
        var stride = Model.Stride;
        _gl.Attribute(0, 3, stride, 0);
        _gl.Attribute(1, 3, stride, 3);
        _gl.Attribute(2, 2, stride, 6);
        _gl.Attribute(3, 4, stride, 8);
        return vao;
    }

    /// <summary>A model's packing, made once per model (on a worker thread when first asked from one) and shared by
    /// the views showing it.</summary>
    public static PackedModel Pack(MachineModel model) => Packs.GetValue(model, PackedModel.Of);

    private static readonly ConditionalWeakTable<MachineModel, PackedModel> Packs = [];
}

/// <summary>A model's textures on the graphics card, by the meshes' texture numbers, and which only give an opacity
/// (their colour comes from the material). New pictures replace one in place (new paint).</summary>
public sealed class GpuTextures
{
    private readonly Gl _gl;
    private readonly int[] _ids;
    private readonly bool[] _alphaOnly;

    private readonly IReadOnlyList<MachineTexture?> _pending;
    private int _next;

    /// <summary>The textures on the card: all at once, or with <paramref name="later"/> none yet, each put on by
    /// <see cref="UploadSome"/> a few a frame.</summary>
    public GpuTextures(Gl gl, IReadOnlyList<MachineTexture?> textures, bool later = false)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(textures);
        _gl = gl;
        _ids = new int[textures.Count];
        _alphaOnly = new bool[textures.Count];
        _pending = textures;
        if (!later)
        {
            UploadSome(long.MaxValue);
        }
    }

    /// <summary>Whether every texture is on the card.</summary>
    public bool Complete => _next >= _pending.Count;

    /// <summary>Puts textures on the card until the <see cref="System.Diagnostics.Stopwatch"/> time
    /// <paramref name="deadline"/> (one at least); true when all are on. A texture the paint replaced meanwhile
    /// (<see cref="Set"/>) is not put on again.</summary>
    public bool UploadSome(long deadline)
    {
        var first = true;
        while (_next < _pending.Count && (first || System.Diagnostics.Stopwatch.GetTimestamp() < deadline))
        {
            var i = _next++;
            if (_pending[i] is { } t && _ids[i] == 0)
            {
                _ids[i] = _gl.Texture(t.Picture);
                first = false;
            }
            _alphaOnly[i] = _pending[i]?.AlphaOnly ?? false;
        }
        return Complete;
    }

    /// <summary>Texture <paramref name="index"/>'s name on the card (0 none).</summary>
    public int this[int index] => index >= 0 && index < _ids.Length ? _ids[index] : 0;

    public bool AlphaOnly(int index) => index >= 0 && index < _alphaOnly.Length && _alphaOnly[index];

    /// <summary>Puts a new picture in texture <paramref name="index"/> (made when the slot was empty).</summary>
    public void Set(int index, Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (index < 0 || index >= _ids.Length)
        {
            return;
        }
        if (_ids[index] == 0)
        {
            _ids[index] = _gl.Texture(picture);
            return;
        }
        _gl.Api.BindTexture(Gl.Texture2D, _ids[index]);
        _gl.Upload(picture);
    }

    public void Delete()
    {
        foreach (var id in _ids.Where(id => id != 0))
        {
            _gl.Api.DeleteTexture(id);
        }
        Array.Clear(_ids);
    }
}
