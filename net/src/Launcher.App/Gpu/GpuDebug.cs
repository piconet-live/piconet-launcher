using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Launcher.App.Gpu;

/// <summary>
/// For checking the 3D views, off unless a launch file's <c>"gpuLog"</c> names a folder
/// (<see cref="Launcher.Core.DebugSettings"/>): each view writes a line a second to
/// <c>gpu.log</c> there (frames shown, the time between them, the processor's time to issue a frame and the time until
/// the card has finished it), and saves its picture as it drew it (<c>NAME-N.png</c>: its latest frame among its first
/// eight, and its 180th), as window captures may leave out what the card drew; the garage chooser's stills are saved
/// too, each also drawn large (<c>still-MACHINE.png</c>). With <c>"gpuTurn"</c> as well, the machine views
/// turn steadily as if dragged, to measure what a turning machine costs without moving the pointer.
/// </summary>
internal static class GpuDebug
{
    private const int CaptureAfter = 180;
    private const int CaptureFirst = 8;

    private static string? Folder { get; set; }
    private static readonly ConditionalWeakTable<GpuView, Stats> All = [];
    private static readonly Lock Gate = new();
    private static int _views;

    public static bool On => Folder is not null;

    /// <summary>The machine views turn steadily, for a measurement (see the class).</summary>
    public static bool Turning { get; private set; }

    /// <summary>Switches the checking on for this run, before any view is made (the program's start).</summary>
    public static void Use(string? folder, bool turning)
    {
        Folder = folder is { Length: > 0 } ? folder : null;
        Turning = Folder is not null && turning;
    }

    public static void Note(GpuView view, string text)
    {
        if (Folder is not null)
        {
            Write($"{Of(view).Name}: {text}");
        }
    }

    public static void Note(string who, string text)
    {
        if (Folder is not null)
        {
            Write($"{who}: {text}");
        }
    }

    /// <summary>A frame drawn (issued from <paramref name="start"/>): counted, and when on, waited for.</summary>
    public static void Frame(GpuView view, Gl gl, RenderTarget target, long start)
    {
        if (Folder is null)
        {
            return;
        }
        var issued = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        gl.Api.Finish();
        var done = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var stats = Of(view);
        var now = Stopwatch.GetTimestamp();
        if (stats.Last != 0)
        {
            var gap = Stopwatch.GetElapsedTime(stats.Last, now).TotalMilliseconds;
            stats.Gaps += gap;
            stats.MaxGap = Math.Max(stats.MaxGap, gap);
        }
        stats.Last = now;
        stats.Frames++;
        stats.Issued += issued;
        stats.Done += done;
        stats.MaxDone = Math.Max(stats.MaxDone, done);
        if (++stats.Total == CaptureAfter || stats.Total <= CaptureFirst)
        {
            Save(target, Path.Combine(Folder, $"{stats.Name}.png"));
        }
        if (Stopwatch.GetElapsedTime(stats.Since, now).TotalSeconds >= 1)
        {
            var n = Math.Max(1, stats.Frames);
            Write(string.Create(CultureInfo.InvariantCulture,
                $"{stats.Name}: {target.Size.Width}x{target.Size.Height} {stats.Frames} frames, every {stats.Gaps / Math.Max(1, n - 1):F1} ms (longest {stats.MaxGap:F1}), issued in {stats.Issued / n:F2} ms, finished in {stats.Done / n:F2} ms (longest {stats.MaxDone:F2})"));
            (stats.Frames, stats.Gaps, stats.MaxGap, stats.Issued, stats.Done, stats.MaxDone, stats.Since) = (0, 0, 0, 0, 0, 0, now);
        }
    }

    /// <summary>Saves premultiplied RGBA pixels (top row first) as a PNG, when on.</summary>
    public static void Save(byte[] rgba, PixelSize size, string name)
    {
        if (Folder is null)
        {
            return;
        }
        var pinned = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Premul, pinned.AddrOfPinnedObject(), size,
                new Vector(96, 96), size.Width * 4);
            Directory.CreateDirectory(Folder);
            bitmap.Save(Path.Combine(Folder, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note(name, $"not saved: {e.Message}");
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void Save(RenderTarget target, string path) =>
        Save(target.Read(), target.Size, Path.GetFileNameWithoutExtension(path));

    private static Stats Of(GpuView view) =>
        All.GetValue(view, v => new Stats($"{v.GetType().Name}-{Interlocked.Increment(ref _views)}") { Since = Stopwatch.GetTimestamp() });

    private static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder!);
                Launcher.Core.LogFiles.Append(Path.Combine(Folder!, "gpu.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a check's log is not worth a view
            }
        }
    }

    private sealed class Stats(string name)
    {
        public string Name { get; } = name;

        public long Since { get; set; }

        public long Last { get; set; }

        public int Total { get; set; }

        public int Frames { get; set; }

        public double Gaps { get; set; }

        public double MaxGap { get; set; }

        public double Issued { get; set; }

        public double Done { get; set; }

        public double MaxDone { get; set; }
    }
}
