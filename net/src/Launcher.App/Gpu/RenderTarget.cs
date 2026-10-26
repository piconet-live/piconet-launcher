using Avalonia;

namespace Launcher.App.Gpu;

/// <summary>
/// Where a 3D view draws: a multisampled colour and depth buffer of the view's size (smooth edges, a depth buffer of
/// its own, as Avalonia's is 16-bit and single-sampled), resolved into a plain texture, which is then copied into the
/// framebuffer Avalonia composes (whatever its format) or read back as pixels. Cleared to clear, so the page shows
/// round the model; colours are premultiplied by their opacity, as Avalonia composes them.
/// </summary>
internal sealed class RenderTarget
{
    private const string CopyVertex = """
        out vec2 vUv;
        void main()
        {
            // one triangle over the whole view
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string CopyFragment = """
        in vec2 vUv;
        uniform sampler2D uImage;
        out vec4 fragColor;
        void main()
        {
            fragColor = texelFetch(uImage, ivec2(gl_FragCoord.xy), 0);
        }
        """;

    private readonly Gl _gl;
    private readonly int _copy;
    private readonly int _copyImage;
    private readonly int _empty;
    private int _sampledFbo;
    private int _sampledColor;
    private int _sampledDepth;
    private int _fbo;
    private int _texture;

    public RenderTarget(Gl gl)
    {
        _gl = gl;
        _copy = gl.Program(CopyVertex, CopyFragment);
        _copyImage = gl.Location(_copy, "uImage");
        _empty = gl.Api.GenVertexArray();
    }

    public PixelSize Size { get; private set; }

    /// <summary>Binds the buffer at <paramref name="size"/> (made again when the size changes), cleared, with the
    /// view's viewport.</summary>
    public void Begin(PixelSize size)
    {
        var gl = _gl.Api;
        if (size != Size || _sampledFbo == 0)
        {
            Free();
            Size = size;
            var samples = _gl.Samples > 1 ? _gl.Samples : 0;
            _sampledFbo = gl.GenFramebuffer();
            gl.BindFramebuffer(Gl.Framebuffer, _sampledFbo);
            _sampledColor = gl.GenRenderbuffer();
            gl.BindRenderbuffer(Gl.Renderbuffer, _sampledColor);
            _gl.RenderbufferStorageMultisample(samples, Gl.Rgba8, size.Width, size.Height);
            gl.FramebufferRenderbuffer(Gl.Framebuffer, Gl.ColorAttachment0, Gl.Renderbuffer, _sampledColor);
            _sampledDepth = gl.GenRenderbuffer();
            gl.BindRenderbuffer(Gl.Renderbuffer, _sampledDepth);
            _gl.RenderbufferStorageMultisample(samples, Gl.DepthComponent24, size.Width, size.Height);
            gl.FramebufferRenderbuffer(Gl.Framebuffer, Gl.DepthAttachment, Gl.Renderbuffer, _sampledDepth);
            if (gl.CheckFramebufferStatus(Gl.Framebuffer) != Gl.FramebufferComplete)
            {
                throw new InvalidOperationException("The multisampled framebuffer is incomplete.");
            }
            _texture = gl.GenTexture();
            gl.BindTexture(Gl.Texture2D, _texture);
            gl.TexImage2D(Gl.Texture2D, 0, Gl.Rgba8, size.Width, size.Height, 0, Gl.Rgba, Gl.UnsignedByte, IntPtr.Zero);
            gl.TexParameteri(Gl.Texture2D, Gl.TextureMinFilter, Gl.Nearest);
            gl.TexParameteri(Gl.Texture2D, Gl.TextureMagFilter, Gl.Nearest);
            _fbo = gl.GenFramebuffer();
            gl.BindFramebuffer(Gl.Framebuffer, _fbo);
            gl.FramebufferTexture2D(Gl.Framebuffer, Gl.ColorAttachment0, Gl.Texture2D, _texture, 0);
            if (gl.CheckFramebufferStatus(Gl.Framebuffer) != Gl.FramebufferComplete)
            {
                throw new InvalidOperationException("The resolve framebuffer is incomplete.");
            }
        }
        gl.BindFramebuffer(Gl.Framebuffer, _sampledFbo);
        gl.Viewport(0, 0, size.Width, size.Height);
        gl.Disable(Gl.ScissorTest);
        gl.DepthMask(1);
        gl.ClearColor(0, 0, 0, 0);
        gl.ClearDepth(1);
        gl.Clear(Gl.ColorBufferBit | Gl.DepthBufferBit);
    }

    /// <summary>Resolves the samples into the plain texture.</summary>
    public void Resolve()
    {
        var gl = _gl.Api;
        gl.BindFramebuffer(Gl.ReadFramebuffer, _sampledFbo);
        gl.BindFramebuffer(Gl.DrawFramebuffer, _fbo);
        gl.BlitFramebuffer(0, 0, Size.Width, Size.Height, 0, 0, Size.Width, Size.Height, Gl.ColorBufferBit, Gl.Nearest);
    }

    /// <summary>Copies the resolved picture into framebuffer <paramref name="target"/>, pixel for pixel.</summary>
    public void CopyTo(int target)
    {
        var gl = _gl.Api;
        gl.BindFramebuffer(Gl.Framebuffer, target);
        gl.Viewport(0, 0, Size.Width, Size.Height);
        gl.Disable(Gl.DepthTest);
        gl.Disable(Gl.Blend);
        gl.Disable(Gl.Culling);
        gl.UseProgram(_copy);
        gl.ActiveTexture(Gl.Texture0);
        gl.BindTexture(Gl.Texture2D, _texture);
        gl.Uniform1i(_copyImage, 0);
        gl.BindVertexArray(_empty);
        gl.DrawArrays(Gl.Triangles, 0, 3);
        gl.BindVertexArray(0);
    }

    /// <summary>The resolved picture's pixels: premultiplied RGBA, top row first.</summary>
    public byte[] Read()
    {
        var (w, h) = (Size.Width, Size.Height);
        var bottomUp = new byte[w * h * 4];
        _gl.Api.BindFramebuffer(Gl.Framebuffer, _fbo);
        _gl.ReadPixels(w, h, bottomUp);
        var pixels = new byte[bottomUp.Length];
        for (var y = 0; y < h; y++)
        {
            bottomUp.AsSpan((h - 1 - y) * w * 4, w * 4).CopyTo(pixels.AsSpan(y * w * 4));
        }
        return pixels;
    }

    public void Delete()
    {
        Free();
        _gl.Api.DeleteProgram(_copy);
        _gl.Api.DeleteVertexArray(_empty);
    }

    private void Free()
    {
        var gl = _gl.Api;
        if (_sampledFbo != 0)
        {
            gl.DeleteFramebuffer(_sampledFbo);
            gl.DeleteRenderbuffer(_sampledColor);
            gl.DeleteRenderbuffer(_sampledDepth);
            gl.DeleteFramebuffer(_fbo);
            gl.DeleteTexture(_texture);
        }
        _sampledFbo = _sampledColor = _sampledDepth = _fbo = _texture = 0;
    }
}
