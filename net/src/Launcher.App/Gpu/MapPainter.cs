using System.Numerics;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>A disc on the map, in the view's pixels: its centre and radius, its fill (none when clear) and the ring
/// round its edge (centred on it, of <paramref name="RingWidth"/>), colours premultiplied.</summary>
public readonly record struct MapDisc(Vector2 At, float Radius, Vector4 Fill, Vector4 Ring, float RingWidth);

/// <summary>
/// The track map on the graphics card: the course's model in flat shades of the launcher's slate blue, lit towards cyan
/// (<see cref="CourseModel.Flat"/>, uploaded once per course), or of a venue's tint (<see cref="VenueSky"/>), with a
/// depth buffer; and the racers as discs over it, each a square whose pixels are worked out as a filled, ringed
/// circle with soft edges. The Practice page draws a venue's sky behind the model (<see cref="Sky"/>).
/// </summary>
public sealed class MapPainter
{
    // 2 + 2 + 4 + 4 + 2 floats a disc's corner: its pixel, its offset from the centre, the fill, the ring, and the
    // radius and ring width
    private const int DiscStride = 14;

    private const string TrackVertex = """
        in vec3 aPos;
        in float aShade;
        uniform mat4 uViewProjection;
        out float vShade;
        void main()
        {
            vShade = aShade;
            gl_Position = uViewProjection * vec4(aPos, 1.0);
        }
        """;

    private const string TrackFragment = """
        in float vShade;
        uniform vec3 uDark;
        uniform vec3 uLit;
        out vec4 fragColor;
        void main()
        {
            fragColor = vec4(mix(uDark, uLit, vShade), 1.0);
        }
        """;

    // the launcher's slate blues, lit towards cyan: the race page's map
    private static readonly Vector3 SlateDark = new Vector3(28, 40, 62) / 255;
    private static readonly Vector3 SlateLit = new Vector3(98, 160, 212) / 255;

    private const string SkyVertex = """
        out float vDown;
        void main()
        {
            // one triangle over the whole view; vDown 0 at its top, 1 at its foot
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vDown = 1.0 - p.y;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string SkyFragment = """
        in float vDown;
        uniform vec3 uTop;
        uniform vec3 uHorizon;
        uniform vec3 uGlow;
        uniform vec3 uGround;
        uniform float uBand;
        out vec4 fragColor;
        void main()
        {
            // the sky darkens upwards from the horizon; the ground darkens downwards from it; a soft glowing band
            // where they meet, brightest on the line and gone a fifth of the picture away
            vec3 sky = mix(uHorizon, uTop, smoothstep(0.0, 1.0, (uBand - vDown) / uBand));
            vec3 ground = mix(uGround * 1.6, uGround, clamp((vDown - uBand) / (1.0 - uBand), 0.0, 1.0));
            vec3 colour = vDown < uBand ? sky : ground;
            float glow = exp(-abs(vDown - uBand) * (vDown < uBand ? 14.0 : 30.0));
            fragColor = vec4(mix(colour, uGlow, glow * 0.85), 1.0);
        }
        """;

    private const string DiscVertex = """
        in vec2 aPixel;
        in vec2 aOffset;
        in vec4 aFill;
        in vec4 aRing;
        in vec2 aShape;
        uniform vec2 uSize;
        out vec2 vOffset;
        out vec4 vFill;
        out vec4 vRing;
        out vec2 vShape;
        void main()
        {
            vOffset = aOffset;
            vFill = aFill;
            vRing = aRing;
            vShape = aShape;
            gl_Position = vec4(aPixel.x / uSize.x * 2.0 - 1.0, 1.0 - aPixel.y / uSize.y * 2.0, 0.0, 1.0);
        }
        """;

    private const string DiscFragment = """
        in vec2 vOffset;
        in vec4 vFill;
        in vec4 vRing;
        in vec2 vShape;
        out vec4 fragColor;
        void main()
        {
            float d = length(vOffset);
            float fill = clamp(vShape.x - d + 0.5, 0.0, 1.0);
            float ring = clamp(vShape.y * 0.5 - abs(d - vShape.x) + 0.5, 0.0, 1.0);
            fragColor = vRing * ring + vFill * fill * (1.0 - vRing.a * ring);
        }
        """;

    private readonly Gl _gl;
    private readonly int _track;
    private readonly int _trackViewProjection;
    private readonly int _trackDark;
    private readonly int _trackLit;
    private readonly int _sky;
    private readonly int _skyEmpty;
    private readonly (int Top, int Horizon, int Glow, int Ground, int Band) _skyAt;
    private readonly int _discs;
    private readonly int _discSize;
    private readonly int _discVao;
    private readonly int _discBuffer;
    private readonly List<float> _discData = [];
    private int _trackVao;
    private int _trackBuffer;
    private int _trackCorners;
    private CourseModel? _course;

    public MapPainter(Gl gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _track = gl.Program(TrackVertex, TrackFragment, "aPos", "aShade");
        _trackViewProjection = gl.Location(_track, "uViewProjection");
        _trackDark = gl.Location(_track, "uDark");
        _trackLit = gl.Location(_track, "uLit");
        _sky = gl.Program(SkyVertex, SkyFragment);
        _skyAt = (gl.Location(_sky, "uTop"), gl.Location(_sky, "uHorizon"), gl.Location(_sky, "uGlow"), gl.Location(_sky, "uGround"),
            gl.Location(_sky, "uBand"));
        _skyEmpty = gl.Api.GenVertexArray();
        _discs = gl.Program(DiscVertex, DiscFragment, "aPixel", "aOffset", "aFill", "aRing", "aShape");
        _discSize = gl.Location(_discs, "uSize");
        var api = gl.Api;
        api.BindVertexArray(0);
        _discVao = api.GenVertexArray();
        api.BindVertexArray(_discVao);
        _discBuffer = gl.Buffer(Gl.ArrayBuffer, ReadOnlySpan<float>.Empty, Gl.DynamicDraw);
        gl.Attribute(0, 2, DiscStride, 0);
        gl.Attribute(1, 2, DiscStride, 2);
        gl.Attribute(2, 4, DiscStride, 4);
        gl.Attribute(3, 4, DiscStride, 8);
        gl.Attribute(4, 2, DiscStride, 12);
        api.BindVertexArray(0);
    }

    /// <summary>The course's model, solid, as the camera <paramref name="viewProjection"/> sees it, in the map's
    /// slate blues.</summary>
    public void Track(CourseModel course, Matrix4x4 viewProjection) => Track(course, viewProjection, SlateDark, SlateLit);

    /// <summary>The course's model, solid, as the camera <paramref name="viewProjection"/> sees it, its flat shades
    /// from <paramref name="dark"/> edge-on to the light to <paramref name="lit"/> facing it.</summary>
    public void Track(CourseModel course, Matrix4x4 viewProjection, Vector3 dark, Vector3 lit)
    {
        ArgumentNullException.ThrowIfNull(course);
        var api = _gl.Api;
        if (!ReferenceEquals(course, _course))
        {
            Upload(course);
        }
        api.Enable(Gl.DepthTest);
        api.DepthFunc(Gl.Less);
        api.DepthMask(1);
        api.Disable(Gl.Blend);
        api.Disable(Gl.Culling);
        api.UseProgram(_track);
        _gl.Uniform(_trackViewProjection, viewProjection);
        _gl.Uniform(_trackDark, dark);
        _gl.Uniform(_trackLit, lit);
        api.BindVertexArray(_trackVao);
        api.DrawArrays(Gl.Triangles, 0, _trackCorners);
        api.BindVertexArray(0);
    }

    /// <summary>A venue's sky over the whole view, behind everything (nothing of it in the depth buffer).</summary>
    public void Sky(VenueSky sky)
    {
        var api = _gl.Api;
        api.Disable(Gl.DepthTest);
        api.DepthMask(0);
        api.Disable(Gl.Blend);
        api.Disable(Gl.Culling);
        api.UseProgram(_sky);
        _gl.Uniform(_skyAt.Top, sky.Top);
        _gl.Uniform(_skyAt.Horizon, sky.Horizon);
        _gl.Uniform(_skyAt.Glow, sky.Glow);
        _gl.Uniform(_skyAt.Ground, sky.Ground);
        api.Uniform1f(_skyAt.Band, VenueSky.BandAt);
        api.BindVertexArray(_skyEmpty);
        api.DrawArrays(Gl.Triangles, 0, 3);
        api.BindVertexArray(0);
        api.DepthMask(1);
    }

    /// <summary>Discs over everything, in order (the last on top), on a view of <paramref name="size"/> pixels.</summary>
    public void Discs(IReadOnlyList<MapDisc> discs, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(discs);
        if (discs.Count == 0)
        {
            return;
        }
        _discData.Clear();
        foreach (var disc in discs)
        {
            var reach = disc.Radius + (disc.RingWidth / 2) + 1.5f;
            Span<Vector2> corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, -1), new(1, 1), new(-1, 1)];
            foreach (var corner in corners)
            {
                var offset = corner * reach;
                _discData.AddRange([disc.At.X + offset.X, disc.At.Y + offset.Y, offset.X, offset.Y,
                    disc.Fill.X, disc.Fill.Y, disc.Fill.Z, disc.Fill.W, disc.Ring.X, disc.Ring.Y, disc.Ring.Z, disc.Ring.W,
                    disc.Radius, disc.RingWidth]);
            }
        }
        var api = _gl.Api;
        api.Disable(Gl.DepthTest);
        api.Disable(Gl.Culling);
        api.Enable(Gl.Blend);
        _gl.BlendFunc(Gl.One, Gl.OneMinusSrcAlpha);
        api.UseProgram(_discs);
        _gl.Uniform(_discSize, size.X, size.Y);
        api.BindVertexArray(_discVao);
        api.BindBuffer(Gl.ArrayBuffer, _discBuffer);
        _gl.Fill(Gl.ArrayBuffer, (ReadOnlySpan<float>)System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_discData), Gl.DynamicDraw);
        api.DrawArrays(Gl.Triangles, 0, discs.Count * 6);
        api.BindVertexArray(0);
    }

    public void Delete()
    {
        var api = _gl.Api;
        FreeTrack();
        api.DeleteProgram(_track);
        api.DeleteProgram(_discs);
        api.DeleteProgram(_sky);
        api.DeleteVertexArray(_skyEmpty);
        api.DeleteVertexArray(_discVao);
        api.DeleteBuffer(_discBuffer);
    }

    private void Upload(CourseModel course)
    {
        FreeTrack();
        var (corners, shades) = course.Flat();
        var data = new float[corners.Length * 4];
        for (var i = 0; i < corners.Length; i++)
        {
            (data[i * 4], data[(i * 4) + 1], data[(i * 4) + 2], data[(i * 4) + 3]) = (corners[i].X, corners[i].Y, corners[i].Z, shades[i / 3]);
        }
        var api = _gl.Api;
        api.BindVertexArray(0);
        _trackVao = api.GenVertexArray();
        api.BindVertexArray(_trackVao);
        _trackBuffer = _gl.Buffer(Gl.ArrayBuffer, (ReadOnlySpan<float>)data, Gl.StaticDraw);
        _gl.Attribute(0, 3, 4, 0);
        _gl.Attribute(1, 1, 4, 3);
        api.BindVertexArray(0);
        _trackCorners = corners.Length;
        _course = course;
    }

    private void FreeTrack()
    {
        if (_trackVao != 0)
        {
            _gl.Api.DeleteVertexArray(_trackVao);
            _gl.Api.DeleteBuffer(_trackBuffer);
        }
        (_trackVao, _trackBuffer, _trackCorners, _course) = (0, 0, 0, null);
    }
}
