using System.Numerics;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>
/// The garage's studio on the graphics card, for machines: textured meshes lit per pixel by a warm key light
/// above, a cool fill, a cyan rim from behind and a soft sky reflected in glossy paint (the look the processor's
/// renderer had), cut-out texels left out of solid meshes, see-through meshes blended last far to near, two-sided
/// meshes lit on whichever side shows; and the turntable's floor under a machine.
/// Draw a scene as <see cref="Opaque"/> for each model, then <see cref="Floor"/>, then <see cref="Translucent"/>.
/// </summary>
public sealed class Studio
{
    private const string PlainVertex = """
        in vec3 aPos;
        in vec3 aNormal;
        in vec2 aUv;
        in vec4 aColor;
        uniform mat4 uModelView;
        uniform mat4 uProjection;
        out vec3 vView;
        out vec3 vNormal;
        out vec2 vUv;
        out vec4 vColor;
        void main()
        {
            vec4 view = uModelView * vec4(aPos, 1.0);
            vView = view.xyz;
            vNormal = mat3(uModelView) * aNormal;
            vUv = aUv;
            vColor = aColor;
            gl_Position = uProjection * view;
        }
        """;

    // the studio's lights are in view space (x right, y up, the camera looking down -z)
    private const string LitFragment = """
        in vec3 vView;
        in vec3 vNormal;
        in vec2 vUv;
        in vec4 vColor;
        uniform vec3 uDiffuse;
        uniform vec3 uAmbient;
        uniform vec3 uSpecular;
        uniform float uShininess;
        uniform float uAlpha;
        uniform int uTexture;
        uniform int uTranslucent;
        uniform sampler2D uImage;
        out vec4 fragColor;

        const vec3 KeyLight = vec3(-0.45, 0.8, 0.55) / length(vec3(-0.45, 0.8, 0.55));
        const vec3 KeyColor = vec3(1.0, 0.96, 0.9);
        const vec3 FillLight = vec3(0.85, 0.05, 0.35) / length(vec3(0.85, 0.05, 0.35));
        const vec3 FillColor = vec3(0.22, 0.3, 0.42);
        const vec3 RimLight = vec3(0.25, 0.55, -0.8) / length(vec3(0.25, 0.55, -0.8));
        const vec3 RimColor = vec3(0.35, 0.82, 1.0);
        const vec3 AmbientLight = vec3(0.42, 0.46, 0.54);
        const vec3 SkyHorizon = vec3(0.3, 0.4, 0.52);
        const vec3 SkyTop = vec3(0.7, 0.82, 0.95);
        const vec3 SkyGround = vec3(0.03, 0.05, 0.08);

        float highlight(float x)
        {
            return x <= 0.0 ? 0.0 : pow(min(x, 1.0), uShininess);
        }

        void main()
        {
            vec4 color = vColor;
            if (uTexture != 0)
            {
                vec4 texel = texture(uImage, vUv);
                // a cut-out texel of a solid surface is a hole
                if (uTranslucent == 0 && texel.a < 0.5)
                {
                    discard;
                }
                color = uTexture == 2 ? vec4(color.rgb, color.a * texel.a) : color * texel;
            }
            float alpha = uTranslucent != 0 ? clamp(color.a * uAlpha, 0.0, 1.0) : 1.0;
            vec3 n = normalize(vNormal);
            if (!gl_FrontFacing)
            {
                n = -n;
            }
            vec3 v = normalize(-vView);
            vec3 diffuse = AmbientLight * uAmbient + uDiffuse * (KeyColor * max(0.0, dot(n, KeyLight))
                + FillColor * max(0.0, dot(n, FillLight)) + RimColor * (0.5 * max(0.0, dot(n, RimLight))));
            vec3 rgb = color.rgb * diffuse;
            // highlights from the key and rim lights, and the studio's sky in glossy surfaces, strongest at grazing
            // angles
            float edge = 1.0 - max(0.0, dot(n, v));
            float edge2 = edge * edge;
            vec3 r = reflect(-v, n);
            vec3 sky = r.y > 0.0 ? mix(SkyHorizon, SkyTop, sqrt(r.y)) : mix(SkyHorizon, SkyGround, sqrt(-r.y));
            float fresnel = 0.25 + 0.75 * edge2 * edge2;
            rgb += uSpecular * (KeyColor * highlight(dot(n, normalize(KeyLight + v)))
                + RimColor * highlight(dot(n, normalize(RimLight + v))) + sky * (fresnel * 0.6));
            // a cyan edge from the rim light, as the launcher's accent
            rgb += RimColor * (0.18 * edge2 * edge * max(0.0, n.y * 0.5 + 0.5));
            fragColor = vec4(clamp(rgb, 0.0, 1.0) * alpha, alpha);
        }
        """;

    private const string FloorVertex = """
        in vec3 aPos;
        uniform mat4 uModelView;
        uniform mat4 uProjection;
        uniform vec2 uCenter;
        out vec2 vHit;
        void main()
        {
            vHit = aPos.xz - uCenter;
            gl_Position = uProjection * (uModelView * vec4(aPos, 1.0));
        }
        """;

    // the ground under the machine: only its shadow (an oval of its footprint, soft at its edge): no dark disc, no ring
    // of light at the rim, no light under the machine (direction D).
    private const string FloorFragment = """
        in vec2 vHit;
        uniform vec2 uFootprint;
        out vec4 fragColor;

        float inside(float outer, float inner, float v)
        {
            float k = clamp((outer - v) / (outer - inner), 0.0, 1.0);
            return k * k * (3.0 - 2.0 * k);
        }

        void main()
        {
            float shadow = 0.55 * inside(1.15, 0.35, length(vHit / max(uFootprint, vec2(0.01))));
            fragColor = vec4(0.0, 0.0, 0.0, shadow);
        }
        """;

    private readonly Gl _gl;
    private readonly Lit _plain;
    private readonly int _floor;
    private readonly int _floorModelView;
    private readonly int _floorProjection;
    private readonly int _floorCenter;
    private readonly int _floorFootprint;
    private readonly int _floorVao;
    private readonly int _floorBuffer;

    public Studio(Gl gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _plain = new Lit(gl, gl.Program(PlainVertex, LitFragment, "aPos", "aNormal", "aUv", "aColor"));
        _floor = gl.Program(FloorVertex, FloorFragment, "aPos");
        _floorModelView = gl.Location(_floor, "uModelView");
        _floorProjection = gl.Location(_floor, "uProjection");
        _floorCenter = gl.Location(_floor, "uCenter");
        _floorFootprint = gl.Location(_floor, "uFootprint");
        var api = gl.Api;
        api.BindVertexArray(0);
        _floorVao = api.GenVertexArray();
        api.BindVertexArray(_floorVao);
        _floorBuffer = gl.Buffer(Gl.ArrayBuffer, ReadOnlySpan<float>.Empty, Gl.DynamicDraw);
        gl.Attribute(0, 3, 3, 0);
        api.BindVertexArray(0);
    }

    /// <summary>A model's solid meshes, writing depth.</summary>
    /// <summary>A custom part's colour (0-1 each) for its painted meshes, by part; null, or none given: the mesh's own
    /// colours.</summary>
    public Func<int, Vector3?>? PartColour { get; set; }

    public void Opaque(GpuMesh mesh, GpuTextures textures, Matrix4x4 modelView, Matrix4x4 projection)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var api = _gl.Api;
        api.Enable(Gl.DepthTest);
        // a decal drawn after the surface it lies on shows over it, as the game draws them
        api.DepthFunc(Gl.Lequal);
        api.DepthMask(1);
        api.Disable(Gl.Blend);
        var lit = Use(modelView, projection);
        mesh.Bind();
        var meshes = mesh.Model.Meshes;
        for (var m = 0; m < meshes.Count; m++)
        {
            if (!meshes[m].Material.Translucent && meshes[m].Count > 0)
            {
                Material(lit, meshes[m].Material, textures, translucent: false);
                _gl.DrawTriangles(meshes[m].First, meshes[m].Count);
            }
        }
        api.BindVertexArray(0);
    }

    /// <summary>A model's see-through meshes over what is drawn, far to near, without hiding what is behind them.</summary>
    public void Translucent(GpuMesh mesh, GpuTextures textures, Matrix4x4 modelView, Matrix4x4 projection)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Model.TranslucentTriangles == 0)
        {
            return;
        }
        var api = _gl.Api;
        api.Enable(Gl.DepthTest);
        api.DepthFunc(Gl.Lequal);
        api.DepthMask(0);
        api.Enable(Gl.Blend);
        _gl.BlendFunc(Gl.One, Gl.OneMinusSrcAlpha);
        var lit = Use(modelView, projection);
        mesh.Sort(modelView);
        foreach (var run in mesh.Runs)
        {
            Material(lit, mesh.Model.Meshes[run.Mesh].Material, textures, translucent: true);
            _gl.DrawTriangles(run.First, run.Count);
        }
        api.BindVertexArray(0);
        api.DepthMask(1);
    }

    /// <summary>The turntable under a machine (in its model's space), behind what is drawn over it.</summary>
    public void Floor(TurntableFloor floor, Matrix4x4 modelView, Matrix4x4 projection)
    {
        var api = _gl.Api;
        api.Enable(Gl.DepthTest);
        api.DepthFunc(Gl.Lequal);
        api.DepthMask(0);
        api.Disable(Gl.Culling);
        api.Enable(Gl.Blend);
        _gl.BlendFunc(Gl.One, Gl.OneMinusSrcAlpha);
        api.UseProgram(_floor);
        _gl.Uniform(_floorModelView, modelView);
        _gl.Uniform(_floorProjection, projection);
        _gl.Uniform(_floorCenter, floor.Center.X, floor.Center.Z);
        _gl.Uniform(_floorFootprint, floor.Footprint.X, floor.Footprint.Y);
        var c = floor.Corners;
        Span<float> square =
        [
            c[0].X, c[0].Y, c[0].Z, c[1].X, c[1].Y, c[1].Z, c[2].X, c[2].Y, c[2].Z,
            c[0].X, c[0].Y, c[0].Z, c[2].X, c[2].Y, c[2].Z, c[3].X, c[3].Y, c[3].Z,
        ];
        api.BindVertexArray(_floorVao);
        api.BindBuffer(Gl.ArrayBuffer, _floorBuffer);
        _gl.Fill(Gl.ArrayBuffer, (ReadOnlySpan<float>)square, Gl.DynamicDraw);
        api.DrawArrays(Gl.Triangles, 0, 6);
        api.BindVertexArray(0);
        api.DepthMask(1);
    }

    public void Delete()
    {
        var api = _gl.Api;
        api.DeleteProgram(_plain.Program);
        api.DeleteProgram(_floor);
        api.DeleteVertexArray(_floorVao);
        api.DeleteBuffer(_floorBuffer);
    }

    private Lit Use(Matrix4x4 modelView, Matrix4x4 projection)
    {
        var lit = _plain;
        _gl.Api.UseProgram(lit.Program);
        _gl.Uniform(lit.ModelView, modelView);
        _gl.Uniform(lit.Projection, projection);
        _gl.Api.Uniform1i(lit.Image, 0);
        _gl.Api.ActiveTexture(Gl.Texture0);
        return lit;
    }

    private void Material(Lit lit, MachineMaterial material, GpuTextures textures, bool translucent)
    {
        var api = _gl.Api;
        if (material.TwoSided)
        {
            api.Disable(Gl.Culling);
        }
        else
        {
            api.Enable(Gl.Culling);
            _gl.CullFace(Gl.Back);
            _gl.FrontFace(Gl.Ccw);
        }
        if (material.Painted && PartColour?.Invoke(material.Part) is { } paint)
        {
            // a custom part's painted mesh: diffuse and ambient both the colour, as the game sets them
            _gl.Uniform(lit.Diffuse, paint);
            _gl.Uniform(lit.Ambient, paint);
        }
        else
        {
            _gl.Uniform(lit.Diffuse, material.Diffuse);
            _gl.Uniform(lit.Ambient, material.Ambient);
        }
        _gl.Uniform(lit.Specular, material.Specular);
        api.Uniform1f(lit.Shininess, material.Shininess);
        api.Uniform1f(lit.Alpha, material.Alpha);
        api.Uniform1i(lit.Translucent, translucent ? 1 : 0);
        var texture = textures[material.Texture];
        api.Uniform1i(lit.Texture, texture == 0 ? 0 : textures.AlphaOnly(material.Texture) ? 2 : 1);
        api.BindTexture(Gl.Texture2D, texture);
        if (texture != 0)
        {
            // the same picture can go on one mesh once and on another over and over: set as each is drawn
            api.TexParameteri(Gl.Texture2D, Gl.TextureWrapS, Wrap(material.WrapS));
            api.TexParameteri(Gl.Texture2D, Gl.TextureWrapT, Wrap(material.WrapT));
        }
    }

    private static int Wrap(TextureWrap wrap) => wrap switch
    {
        TextureWrap.Clamp => Gl.ClampToEdge,
        TextureWrap.Mirror => Gl.MirroredRepeat,
        _ => Gl.Repeat,
    };

    /// <summary>A lit program and its uniforms' locations.</summary>
    private sealed class Lit(Gl gl, int program)
    {
        public int Program { get; } = program;

        public int ModelView { get; } = gl.Location(program, "uModelView");

        public int Projection { get; } = gl.Location(program, "uProjection");

        public int Diffuse { get; } = gl.Location(program, "uDiffuse");

        public int Ambient { get; } = gl.Location(program, "uAmbient");

        public int Specular { get; } = gl.Location(program, "uSpecular");

        public int Shininess { get; } = gl.Location(program, "uShininess");

        public int Alpha { get; } = gl.Location(program, "uAlpha");

        public int Texture { get; } = gl.Location(program, "uTexture");

        public int Translucent { get; } = gl.Location(program, "uTranslucent");

        public int Image { get; } = gl.Location(program, "uImage");
    }
}
