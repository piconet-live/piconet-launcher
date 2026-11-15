using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Launcher.App.Gpu;

namespace Launcher.App.Controls.NextUi;

/// <summary>
/// The redesigned interface's one background: a chequered flag as a hologram, drawn live on the graphics card over
/// the whole window (one triangle, one fragment shader). A dark cloth with three slow waves; its lit squares glow
/// faintly in the accent, their outlines a little more, the crests catch a glint of gold. It never gets in the way of
/// the text in front: no pixel is brighter than about 0.3, so the dim text keeps 4.5:1 on it with nothing between.
/// A page that needs it quieter turns it down (<see cref="Fade"/>: towards the flat ground) and softens it
/// (<see cref="Soft"/>: the squares' edges, outlines and scan lines go). It stands still under Reduced motion and
/// while <see cref="Resting"/> (a game runs: the graphics card is the game's), and without graphics acceleration
/// nothing is drawn: the ground behind it shows.
/// </summary>
public sealed class HoloFlag : GpuView
{
    /// <summary>After this many seconds every wave is where it began (their speeds are whole twentieths: 40π).</summary>
    private const double Lap = 40 * Math.PI;

    /// <summary>How long the wave of a change of area runs across the cloth.</summary>
    private static readonly TimeSpan WaveLength = TimeSpan.FromMilliseconds(700);

    private const string Vertex = """
        void main()
        {
            // one triangle over the whole view
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    // The numbers are the redesign's (its mockup's FLAG_FS): 6 squares to the window's height, turned 0.14 rad; three
    // waves; the ground (0.020, 0.024, 0.024); a lit square the tint x (0.05 + 0.14 x light) under scan lines of 14%,
    // a dark one 0.012 x light, the outline 0.03 + 0.05 x light; the tint the accent, up to half towards turquoise and
    // up to 30% gold on the crests; a vignette of 0.3.
    private const string Fragment = """
        uniform vec2 uRes;
        uniform float uTime;
        uniform float uFade;
        uniform float uSoft;
        uniform vec3 uGlow;
        uniform float uPulse;
        uniform float uDir;
        out vec4 fragColor;
        void main()
        {
            vec2 p = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
            p = vec2(p.x * 0.9902 + p.y * 0.1395, p.y * 0.9902 - p.x * 0.1395);
            float a1 = p.x * 3.1 + p.y * 1.3 - uTime * 0.55;
            float a2 = p.x * 5.7 - p.y * 2.1 - uTime * 0.8 + 1.7;
            float a3 = p.x * 1.4 + p.y * 0.6 - uTime * 0.3 + 4.0;
            float h = 0.55 * sin(a1) + 0.28 * sin(a2) + 0.4 * sin(a3);
            float slope = 1.705 * cos(a1) + 1.596 * cos(a2) + 0.56 * cos(a3);
            vec2 q = p + vec2(0.02 * cos(a1) + 0.01 * cos(a2), 0.045 * h);
            // a change of area sends one wave across the cloth, the way the change went (uPulse runs 0 to 1)
            float bd = (p.x * uDir - (uPulse * 2.8 - 1.4)) * 4.5;
            float band = exp(-bd * bd) * sin(3.14159265 * uPulse);
            q.y += band * 0.05;
            float sx = sin(18.849556 * q.x);
            float sy = sin(18.849556 * q.y);
            // softened, the squares' edges widen and the outlines and scan lines go
            float px = 18.849556 / uRes.y * (1.0 + 28.0 * uSoft);
            float cell = 0.5 + 0.5 * (smoothstep(-px, px, sx) * 2.0 - 1.0) * (smoothstep(-px, px, sy) * 2.0 - 1.0);
            float edge = min(abs(sx), abs(sy));
            float rim = (1.0 - smoothstep(px * 0.5, px * 2.2, edge)) * (1.0 - uSoft);
            float shade = clamp(0.5 + slope * 0.13 + band * 0.45, 0.0, 1.0);
            float scan = mix(0.86 + 0.14 * sin(q.y * 301.59289), 0.86, uSoft);
            float iri = 0.5 + 0.5 * sin(h * 2.4 + p.x * 1.2 + uTime * 0.15);
            vec3 tint = mix(uGlow, vec3(0.29, 0.76, 0.85), iri * 0.5);
            tint = mix(tint, vec3(1.0, 0.84, 0.0), shade * shade * shade * 0.3);
            vec3 col = vec3(0.02, 0.024, 0.024);
            // the brightest pixel stays near 0.3, so that dim text keeps 4.5:1 on it
            col += tint * cell * scan * (0.05 + 0.14 * shade);
            col += tint * (1.0 - cell) * 0.012 * shade;
            col += tint * rim * (0.03 + 0.05 * shade);
            col += tint * cell * exp(-edge * 9.0) * 0.03 * (1.0 - uSoft);
            col += tint * band * 0.04;
            col *= 1.0 - 0.3 * dot(p, p);
            col = mix(col, vec3(0.027, 0.031, 0.031), uFade);
            fragColor = vec4(col, 1.0);
        }
        """;

    /// <summary>How far the flag is turned down towards the flat ground: 0 the flag as it is, 1 the ground alone.</summary>
    public static readonly StyledProperty<double> FadeProperty = AvaloniaProperty.Register<HoloFlag, double>(nameof(Fade));

    /// <summary>How soft the flag is: 0 its squares sharp, with outlines and scan lines; 1 a soft pattern of light.</summary>
    public static readonly StyledProperty<double> SoftProperty = AvaloniaProperty.Register<HoloFlag, double>(nameof(Soft));

    /// <summary>The flag stands still (a game runs): it draws no frame of its own.</summary>
    public static readonly StyledProperty<bool> RestingProperty = AvaloniaProperty.Register<HoloFlag, bool>(nameof(Resting));

    /// <summary>The colour the squares glow in: the accent.</summary>
    public static readonly StyledProperty<Color> GlowProperty =
        AvaloniaProperty.Register<HoloFlag, Color>(nameof(Glow), Color.FromRgb(0x3D, 0xFF, 0x6A));

    private readonly Stopwatch _clock = new();
    private double _time = 12;   // (the waves well apart at the first frame)
    private long _waveBegan;
    private int _waveWay = 1;
    private int _program;
    private int _empty;
    private (int Res, int Time, int Fade, int Soft, int Glow, int Pulse, int Dir) _at;

    public HoloFlag() => IsHitTestVisible = false;

    public double Fade
    {
        get => GetValue(FadeProperty);
        set => SetValue(FadeProperty, value);
    }

    public double Soft
    {
        get => GetValue(SoftProperty);
        set => SetValue(SoftProperty, value);
    }

    public bool Resting
    {
        get => GetValue(RestingProperty);
        set => SetValue(RestingProperty, value);
    }

    public Color Glow
    {
        get => GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    /// <summary>The flag moves by itself: not under Reduced motion, and not while it rests.</summary>
    public bool Moving => !Resting && !Motion.Reduced;

    protected override bool SaysWhenUnavailable => false;

    /// <summary>The waves are slow: 30 frames a second show them as well as 60 would, for half the card's work.</summary>
    protected override double FrameMs => 1000.0 / 30;

    protected override bool Animating => Moving;

    /// <summary>A change of area: one wave across the cloth, to the right for the next area (1), to the left for
    /// the one before (-1). Nothing under Reduced motion or while the flag rests.</summary>
    public void Wave(int way)
    {
        if (!Moving)
        {
            return;
        }
        _waveWay = way < 0 ? -1 : 1;
        _waveBegan = Stopwatch.GetTimestamp();
        Redraw();
    }

    private void OnMotionChanged() => Redraw();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FadeProperty || change.Property == SoftProperty || change.Property == GlowProperty
            || change.Property == RestingProperty)
        {
            Redraw();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Motion.ReducedChanged += OnMotionChanged;
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Motion.ReducedChanged -= OnMotionChanged;
        _clock.Reset();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void Init(Gl gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _program = gl.Program(Vertex, Fragment);
        _at = (gl.Location(_program, "uRes"), gl.Location(_program, "uTime"), gl.Location(_program, "uFade"),
            gl.Location(_program, "uSoft"), gl.Location(_program, "uGlow"), gl.Location(_program, "uPulse"),
            gl.Location(_program, "uDir"));
        _empty = gl.Api.GenVertexArray();
    }

    /// <summary>The flag's time: it runs only while the flag moves, and begins again after a lap of every wave.</summary>
    private double Now()
    {
        if (Moving)
        {
            if (!_clock.IsRunning)
            {
                _clock.Start();
            }
            _time = (_time + _clock.Elapsed.TotalSeconds) % Lap;
            _clock.Restart();
        }
        else
        {
            _clock.Reset();
        }
        return _time;
    }

    /// <summary>Where the wave of a change of area is, 0 to 1; 0: none runs.</summary>
    private double WaveAt()
    {
        if (_waveBegan == 0 || !Moving)
        {
            _waveBegan = 0;
            return 0;
        }
        var at = Stopwatch.GetElapsedTime(_waveBegan) / WaveLength;
        if (at >= 1)
        {
            _waveBegan = 0;
            return 0;
        }
        return at;
    }

    protected override void Draw(Gl gl, PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(gl);
        var api = gl.Api;
        var glow = Glow;
        api.Disable(Gl.DepthTest);
        api.Disable(Gl.Blend);
        api.Disable(Gl.Culling);
        api.UseProgram(_program);
        gl.Uniform(_at.Res, size.Width, size.Height);
        api.Uniform1f(_at.Time, (float)Now());
        api.Uniform1f(_at.Fade, (float)Math.Clamp(Fade, 0, 1));
        api.Uniform1f(_at.Soft, (float)Math.Clamp(Soft, 0, 1));
        gl.Uniform(_at.Glow, new Vector3(glow.R / 255f, glow.G / 255f, glow.B / 255f));
        api.Uniform1f(_at.Pulse, (float)WaveAt());
        api.Uniform1f(_at.Dir, _waveWay);
        api.BindVertexArray(_empty);
        api.DrawArrays(Gl.Triangles, 0, 3);
        api.BindVertexArray(0);
    }

    protected override void Deinit(Gl gl, bool lost)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (!lost)
        {
            gl.Api.DeleteProgram(_program);
            gl.Api.DeleteVertexArray(_empty);
        }
        _program = _empty = 0;
    }
}
