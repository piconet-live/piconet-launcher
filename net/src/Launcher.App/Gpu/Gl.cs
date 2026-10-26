using System.Numerics;
using Avalonia.OpenGL;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>
/// OpenGL for the launcher's 3D views: Avalonia's <see cref="GlInterface"/> and the few entry points it leaves out
/// (uniform vectors, blending, culling, mipmaps, multisampled buffers, reading pixels back), loaded from the context.
/// On Windows Avalonia draws through ANGLE (OpenGL ES 3.0 over Direct3D 11), so everything here keeps to ES 3.0;
/// <see cref="Shader"/> heads the shaders for ES or for desktop OpenGL 3.3, whichever the context is.
/// </summary>
public sealed unsafe class Gl
{
    public const int Triangles = 0x0004;
    public const int UnsignedByte = 0x1401;
    public const int UnsignedInt = 0x1405;
    public const int Floats = 0x1406;
    public const int ArrayBuffer = 0x8892;
    public const int ElementArrayBuffer = 0x8893;
    public const int StaticDraw = 0x88E4;
    public const int DynamicDraw = 0x88E8;
    public const int Texture2D = 0x0DE1;
    public const int Texture0 = 0x84C0;
    public const int Rgba = 0x1908;
    public const int Rgba8 = 0x8058;
    public const int TextureMagFilter = 0x2800;
    public const int TextureMinFilter = 0x2801;
    public const int TextureWrapS = 0x2802;
    public const int TextureWrapT = 0x2803;
    public const int Nearest = 0x2600;
    public const int Linear = 0x2601;
    public const int LinearMipmapLinear = 0x2703;
    public const int Repeat = 0x2901;
    public const int ClampToEdge = 0x812F;
    public const int MirroredRepeat = 0x8370;
    public const int Framebuffer = 0x8D40;
    public const int ReadFramebuffer = 0x8CA8;
    public const int DrawFramebuffer = 0x8CA9;
    public const int Renderbuffer = 0x8D41;
    public const int ColorAttachment0 = 0x8CE0;
    public const int DepthAttachment = 0x8D00;
    public const int DepthComponent24 = 0x81A6;
    public const int FramebufferComplete = 0x8CD5;
    public const int ColorBufferBit = 0x4000;
    public const int DepthBufferBit = 0x0100;
    public const int DepthTest = 0x0B71;
    public const int Blend = 0x0BE2;
    public const int Culling = 0x0B44;
    public const int ScissorTest = 0x0C11;
    public const int Back = 0x0405;
    public const int Ccw = 0x0901;
    public const int Less = 0x0201;
    public const int Lequal = 0x0203;
    public const int One = 1;
    public const int OneMinusSrcAlpha = 0x0303;
    public const int MaxSamples = 0x8D57;
    public const int VertexShader = 0x8B31;
    public const int FragmentShader = 0x8B30;
    public const int PackAlignment = 0x0D05;
    public const int UnpackAlignment = 0x0CF5;

    private readonly delegate* unmanaged[Stdcall]<int, float, float, void> _uniform2f;
    private readonly delegate* unmanaged[Stdcall]<int, float, float, float, void> _uniform3f;
    private readonly delegate* unmanaged[Stdcall]<int, float, float, float, float, void> _uniform4f;
    private readonly delegate* unmanaged[Stdcall]<int, int, void> _blendFunc;
    private readonly delegate* unmanaged[Stdcall]<int, void> _cullFace;
    private readonly delegate* unmanaged[Stdcall]<int, void> _frontFace;
    private readonly delegate* unmanaged[Stdcall]<int, void> _generateMipmap;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, int, void> _renderbufferStorageMultisample;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, int, int, void*, void> _readPixels;
    private readonly delegate* unmanaged[Stdcall]<int, int, void> _pixelStorei;

    public Gl(GlInterface gl, GlVersion version)
    {
        Api = gl;
        Es = version.Type == GlProfileType.OpenGLES;
        // ES 3.0 (or desktop 3.3) for vertex arrays, multisampled buffers and 32-bit indices
        if (version.Major < 3 || (!Es && version.Major == 3 && version.Minor < 3))
        {
            throw new NotSupportedException($"OpenGL {version.Major}.{version.Minor} is older than the 3D views need.");
        }
        IntPtr Load(string name) => gl.GetProcAddress(name) is var p && p != IntPtr.Zero
            ? p : throw new NotSupportedException($"OpenGL has no {name}.");
        _uniform2f = (delegate* unmanaged[Stdcall]<int, float, float, void>)Load("glUniform2f");
        _uniform3f = (delegate* unmanaged[Stdcall]<int, float, float, float, void>)Load("glUniform3f");
        _uniform4f = (delegate* unmanaged[Stdcall]<int, float, float, float, float, void>)Load("glUniform4f");
        _blendFunc = (delegate* unmanaged[Stdcall]<int, int, void>)Load("glBlendFunc");
        _cullFace = (delegate* unmanaged[Stdcall]<int, void>)Load("glCullFace");
        _frontFace = (delegate* unmanaged[Stdcall]<int, void>)Load("glFrontFace");
        _generateMipmap = (delegate* unmanaged[Stdcall]<int, void>)Load("glGenerateMipmap");
        _renderbufferStorageMultisample = (delegate* unmanaged[Stdcall]<int, int, int, int, int, void>)Load("glRenderbufferStorageMultisample");
        _readPixels = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, void*, void>)Load("glReadPixels");
        _pixelStorei = (delegate* unmanaged[Stdcall]<int, int, void>)Load("glPixelStorei");
        if (!gl.IsBlitFramebufferAvailable || !gl.IsGenVertexArraysAvailable || !gl.IsBindVertexArrayAvailable)
        {
            throw new NotSupportedException("OpenGL has no framebuffer blits or vertex arrays.");
        }
        gl.GetIntegerv(MaxSamples, out var samples);
        Samples = Math.Clamp(samples, 1, 4);
    }

    public GlInterface Api { get; }

    /// <summary>Whether the context is OpenGL ES (ANGLE on Windows) rather than desktop OpenGL.</summary>
    public bool Es { get; }

    /// <summary>Samples a pixel for the views' smooth edges: 4 where the card allows (as the processor's 2×2 did).</summary>
    public int Samples { get; }

    public void Uniform(int location, float x, float y) => _uniform2f(location, x, y);

    public void Uniform(int location, Vector3 v) => _uniform3f(location, v.X, v.Y, v.Z);

    public void Uniform(int location, Vector4 v) => _uniform4f(location, v.X, v.Y, v.Z, v.W);

    public void Uniform(int location, Matrix4x4 m) => Api.UniformMatrix4fv(location, 1, false, &m);

    public void BlendFunc(int source, int destination) => _blendFunc(source, destination);

    public void CullFace(int face) => _cullFace(face);

    public void FrontFace(int mode) => _frontFace(mode);

    public void GenerateMipmap(int target) => _generateMipmap(target);

    public void RenderbufferStorageMultisample(int samples, int format, int width, int height) =>
        _renderbufferStorageMultisample(Renderbuffer, samples, format, width, height);

    /// <summary>The bound framebuffer's pixels as RGBA, bottom row first.</summary>
    public void ReadPixels(int width, int height, byte[] into)
    {
        _pixelStorei(PackAlignment, 1);
        fixed (byte* p = into)
        {
            _readPixels(0, 0, width, height, Rgba, UnsignedByte, p);
        }
    }

    /// <summary>A shader's source headed for this context's language (GLSL ES 3.00 or GLSL 3.30).</summary>
    public string Shader(string body) =>
        (Es ? "#version 300 es\nprecision highp float;\nprecision highp int;\n" : "#version 330 core\n") + body;

    /// <summary>A program of these two shaders, its inputs bound in order to <paramref name="attributes"/>.</summary>
    /// <exception cref="InvalidOperationException">A shader does not compile or the program does not link.</exception>
    public int Program(string vertex, string fragment, params string[] attributes)
    {
        var gl = Api;
        var vs = gl.CreateShader(VertexShader);
        var fs = gl.CreateShader(FragmentShader);
        try
        {
            if (gl.CompileShaderAndGetError(vs, Shader(vertex)) is { } ve)
            {
                throw new InvalidOperationException($"vertex shader: {ve}");
            }
            if (gl.CompileShaderAndGetError(fs, Shader(fragment)) is { } fe)
            {
                throw new InvalidOperationException($"fragment shader: {fe}");
            }
            var program = gl.CreateProgram();
            gl.AttachShader(program, vs);
            gl.AttachShader(program, fs);
            for (var i = 0; i < attributes.Length; i++)
            {
                gl.BindAttribLocationString(program, i, attributes[i]);
            }
            if (gl.LinkProgramAndGetError(program) is { } le)
            {
                gl.DeleteProgram(program);
                throw new InvalidOperationException($"program: {le}");
            }
            return program;
        }
        finally
        {
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
        }
    }

    /// <summary>A texture of a picture (RGBA, top row first: texture coordinates' v runs down it as the game's do),
    /// repeating, with its smaller levels for drawing it small.</summary>
    public int Texture(Picture picture)
    {
        var gl = Api;
        var texture = gl.GenTexture();
        gl.BindTexture(Texture2D, texture);
        Upload(picture);
        gl.TexParameteri(Texture2D, TextureWrapS, Repeat);
        gl.TexParameteri(Texture2D, TextureWrapT, Repeat);
        gl.TexParameteri(Texture2D, TextureMagFilter, Linear);
        gl.TexParameteri(Texture2D, TextureMinFilter, LinearMipmapLinear);
        return texture;
    }

    /// <summary>New pixels for the bound texture (and its smaller levels).</summary>
    public void Upload(Picture picture)
    {
        var (w, h) = (Math.Max(1, picture.Width), Math.Max(1, picture.Height));
        var rgba = picture.Rgba.Length >= w * h * 4 ? picture.Rgba : Padded(picture.Rgba, w * h * 4);
        _pixelStorei(UnpackAlignment, 1);
        fixed (byte* p = rgba)
        {
            Api.TexImage2D(Texture2D, 0, Rgba8, w, h, 0, Rgba, UnsignedByte, (IntPtr)p);
        }
        _generateMipmap(Texture2D);
    }

    private static byte[] Padded(byte[] rgba, int length)
    {
        var padded = new byte[length];
        rgba.AsSpan(0, Math.Min(rgba.Length, length)).CopyTo(padded);
        return padded;
    }

    /// <summary>A buffer of these numbers.</summary>
    public int Buffer<T>(int target, ReadOnlySpan<T> data, int usage) where T : unmanaged
    {
        var buffer = Api.GenBuffer();
        Api.BindBuffer(target, buffer);
        Fill(target, data, usage);
        return buffer;
    }

    /// <summary>New numbers for the bound buffer.</summary>
    public void Fill<T>(int target, ReadOnlySpan<T> data, int usage) where T : unmanaged
    {
        fixed (T* p = data)
        {
            Api.BufferData(target, data.Length * sizeof(T), (IntPtr)p, usage);
        }
    }

    /// <summary>Vertex input <paramref name="index"/>: <paramref name="size"/> floats from float
    /// <paramref name="offset"/> of each vertex of <paramref name="stride"/> floats.</summary>
    public void Attribute(int index, int size, int stride, int offset)
    {
        Api.EnableVertexAttribArray(index);
        Api.VertexAttribPointer(index, size, Floats, 0, stride * sizeof(float), offset * sizeof(float));
    }

    /// <summary>Indexed triangles: <paramref name="count"/> indices from index <paramref name="first"/> of the bound
    /// index buffer.</summary>
    public void DrawTriangles(int first, int count) => Api.DrawElements(Triangles, count, UnsignedInt, first * sizeof(uint));

    /// <summary>A uniform's location by name (-1 when the program left it out).</summary>
    public int Location(int program, string name) => Api.GetUniformLocationString(program, name);
}
