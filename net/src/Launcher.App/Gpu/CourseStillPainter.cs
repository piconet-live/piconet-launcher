using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Launcher.App.Theme;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>
/// The Practice page's tiles: a still of each course's model (<see cref="CourseLook"/>) on its venue's sky, from one
/// angle (<see cref="CourseShot.Still"/>), drawn on the graphics card into a small target and read back as a picture,
/// once per course and disc: kept as a PNG in the data folder (<see cref="CourseStillCache"/>) and read from there
/// after. The pictures are drawn as the machine stills are (<see cref="StillPainter"/>: by whichever 3D view draws
/// next, in its context, after its own frame), one after a frame at most, so that the window's thread is never held
/// for more than one; the tiles show each picture as it comes. The first time, a line in the launcher's log says what
/// the drawing cost.
/// </summary>
internal static class CourseStillPainter
{
    /// <summary>A still's pixels: twice a tile's usual size (about 140 by 64), for sharp edges on dense screens.</summary>
    public static readonly PixelSize Size = new(320, 144);

    private static readonly Dictionary<int, Task<Bitmap?>> Stills = [];
    private static readonly Queue<Request> Queue = [];

    // the cost of a batch of stills drawn (from the first asked until the queue is empty)
    private static long _batchStart;
    private static int _batchDrawn;
    private static double _batchThread;
    private static double _batchLongest;

    /// <summary>How many stills wait to be drawn (a view that has drawn one goes on with its next frame).</summary>
    public static int Waiting => Queue.Count;

    /// <summary>Raised when stills wait to be drawn: the views ask for a frame.</summary>
    public static event Action? Wanted;

    /// <summary>A course's still; null without the disc, without its model or without graphics acceleration.</summary>
    public static Task<Bitmap?> Paint(int course)
    {
        if (!Stills.TryGetValue(course, out var still))
        {
            if (_batchStart == 0)
            {
                _batchStart = Stopwatch.GetTimestamp();
            }
            still = PaintAsync(course);
            Stills[course] = still;
        }
        return still;
    }

    /// <summary>Forgets the stills (another disc).</summary>
    public static void Clear()
    {
        Stills.Clear();
        (_batchStart, _batchDrawn, _batchThread, _batchLongest) = (0, 0, 0, 0);
        while (Queue.TryDequeue(out var request))
        {
            request.Done.TrySetResult(null);
        }
    }

    private static async Task<Bitmap?> PaintAsync(int course)
    {
        if (await GameSkin.CourseLookAsync(course) is not { } look)
        {
            return null;
        }
        var folder = await GameSkin.CourseStillsFolderAsync(Size);
        var file = folder is null ? null : CourseStillCache.File(folder, course);
        if (file is not null && await Task.Run(() => Load(file)) is { } kept)
        {
            return kept;
        }
        var request = new Request(course, look);
        Queue.Enqueue(request);
        Wanted?.Invoke();
        var picture = await request.Done.Task;
        if (picture is not null && file is not null)
        {
            _ = Task.Run(() => Keep(picture, file));
        }
        return picture;
    }

    private static Bitmap? Load(string file)
    {
        if (CourseStillCache.Read(file) is not { } png)
        {
            return null;
        }
        try
        {
            using var stream = new MemoryStream(png);
            return new Bitmap(stream);
        }
#pragma warning disable CA1031 // a kept picture that cannot be decoded is drawn again
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static void Keep(Bitmap picture, string file)
    {
        using var png = new MemoryStream();
        picture.Save(png, PngBitmapEncoderOptions.Default);
        CourseStillCache.Keep(file, png.ToArray());
    }

    /// <summary>Draws one waiting still in the current context (a view's, after its frame), with that view's
    /// <paramref name="kit"/> (made here the first time).</summary>
    public static void Pump(Gl gl, ref Kit? kit)
    {
        if (!Queue.TryDequeue(out var request))
        {
            return;
        }
        var start = Stopwatch.GetTimestamp();
        try
        {
            kit ??= new Kit(gl);
            request.Done.TrySetResult(Draw(kit, request.Look));
        }
        catch (InvalidOperationException e)
        {
            GpuDebug.Note("CourseStillPainter", e.Message);
            request.Done.TrySetResult(null);
        }
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        GpuDebug.Note("CourseStillPainter", $"course {request.Course}: {ms:F1} ms");
        _batchDrawn++;
        _batchThread += ms;
        _batchLongest = Math.Max(_batchLongest, ms);
        if (Queue.Count == 0)
        {
            Program.Faults?.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"course stills: {_batchDrawn} drawn in {Stopwatch.GetElapsedTime(_batchStart).TotalMilliseconds:F0} ms from the first asked; " +
                $"{_batchThread:F1} ms on the window's thread in all, the longest {_batchLongest:F1} ms"));
            (_batchStart, _batchDrawn, _batchThread, _batchLongest) = (0, 0, 0, 0);
        }
    }

    private static Bitmap Draw(Kit kit, CourseLook look)
    {
        var (model, sky) = (look.Model, look.Sky);
        kit.Target.Begin(Size);
        var frame = CourseShot.Still(model, Size.Width, Size.Height);
        kit.Painter.Sky(sky);
        kit.Painter.Track(model, CourseShot.StillViewProjection(model, frame, Size.Width, Size.Height), sky.Dark, sky.Lit);
        kit.Target.Resolve();
        var pixels = kit.Target.Read();
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Premul, pinned.AddrOfPinnedObject(), Size, new Vector(96, 96), Size.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>What a view's context draws the stills with.</summary>
    internal sealed class Kit(Gl gl)
    {
        public MapPainter Painter { get; } = new(gl);

        public RenderTarget Target { get; } = new(gl);

        public void Delete()
        {
            Painter.Delete();
            Target.Delete();
        }
    }

    private sealed record Request(int Course, CourseLook Look)
    {
        public TaskCompletionSource<Bitmap?> Done { get; } = new();
    }
}
