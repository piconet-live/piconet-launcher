using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Launcher.App.Theme;
using Launcher.Core;

namespace Launcher.App.Gpu;

/// <summary>
/// The garage chooser's still pictures of the machines (stock and custom), drawn on the graphics card: a machine in a look drawn into a
/// small multisampled target and read back as a picture, kept per machine while its look stays the same. Thirty cards
/// are thirty pictures, not thirty live views (each with its own context, swap chain and frames); a card is drawn
/// again only when its look changes. Avalonia makes OpenGL contexts only for its OpenGL views, so the pictures are
/// drawn by whichever 3D view draws next (<see cref="Pump"/>, in its context, after its own frame; the garage's
/// machine is always one), each view keeping a <see cref="Kit"/> for it.
/// </summary>
internal static class StillPainter
{
    // seen from the front at three quarters, a little above, close
    private const float Yaw = 3.75f;
    private const float Pitch = 0.42f;
    private const float Zoom = 1.25f;

    // a view works on the pictures this long after a frame at most, so that its frame stays short
    private const double BudgetMs = 2;

    // a still begun in a view that has not drawn for this long is begun again in another
    private const double Abandoned = 500;

    // a still whose textures are going on the card, in its kit's context
    private static Request? _current;

    // by the machine drawn ("m6" a stock machine, "c2-25-50" a custom one's parts): its look's key and its picture
    private static readonly Dictionary<string, (string Key, Task<Bitmap?> Picture)> Stills = [];
    private static readonly Queue<Request> Queue = [];
    private static int _asked;

    /// <summary>How many pictures wait to be drawn (a view that has drawn some goes on with its next frame).</summary>
    public static int Waiting => Queue.Count + (_current is null ? 0 : 1);

    /// <summary>Raised when pictures wait to be drawn: the views ask for a frame.</summary>
    public static event Action? Wanted;

    /// <summary>A still picture of a machine in a look (stock colour, else <paramref name="paint"/>) of
    /// <paramref name="size"/> pixels; null without the disc or without graphics acceleration.</summary>
    public static Task<Bitmap?> Paint(int machine, int? stock, Online.Protocol.Rgb? paint, PixelSize size)
    {
        var slot = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"m{machine}");
        var key = $"{stock}:{paint}:{size}";
        if (Stills.TryGetValue(slot, out var still) && still.Key == key)
        {
            return still.Picture;
        }
        // numbered as asked, not as its model and textures come ready: of two looks of a machine the one asked
        // for last is the one wanted, whichever is ready first
        if (_first == 0)
        {
            _first = LoadTrace.Now;
        }
        var picture = PaintAsync(machine, stock, paint, size, ++_asked);
        Stills[slot] = (key, picture);
        return picture;
    }

    /// <summary>A still picture of a custom machine (its three parts; the pilot is not drawn) in its parts' colours
    /// (<paramref name="colours"/>: body, cockpit, booster; null a part's own colour), as the builder's 3D view
    /// colours it; null without the disc or without graphics acceleration. Kept as the stock machines' stills are, by
    /// its parts and colours (MachineCache.CustomStillFile).</summary>
    public static Task<Bitmap?> PaintCustom(CustomBuild build, IReadOnlyList<Online.Protocol.Rgb?>? colours, PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(build);
        var parts = (build.Body, build.Cockpit, build.Booster);
        var slot = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"c{parts.Body}-{parts.Cockpit}-{parts.Booster}");
        var tints = Enumerable.Range(0, 3).Select(k => colours is not null && k < colours.Count ? colours[k] : null).ToArray();
        var key = $"{string.Join(",", tints.Select(t => t?.ToString() ?? "-"))}:{size}";
        if (Stills.TryGetValue(slot, out var still) && still.Key == key)
        {
            return still.Picture;
        }
        if (_first == 0)
        {
            _first = LoadTrace.Now;
        }
        var picture = PaintCustomAsync(slot, parts, tints, size, ++_asked);
        Stills[slot] = (key, picture);
        return picture;
    }

    private static async Task<Bitmap?> PaintCustomAsync(string slot, (int Body, int Cockpit, int Booster) parts, Online.Protocol.Rgb?[] tints,
        PixelSize size, int asked)
    {
        var file = await GameSkin.MachinesFolderAsync() is { } folder
            ? MachineCache.CustomStillFile(folder, parts.Body, parts.Cockpit, parts.Booster, tints, size.Width, size.Height) : null;
        if (file is not null && await Task.Run(() => Load(file)) is { } kept)
        {
            return kept;
        }
        if (await GameSkin.Custom3D(parts.Body, parts.Cockpit, parts.Booster) is not { } loaded)
        {
            return null;
        }
        // (a custom machine is coloured by material as it is drawn: no look's textures)
        var packed = await Task.Run(() => GpuMesh.Pack(loaded.Model));
        var request = new Request(slot, asked, loaded, packed, [], size) { Tints = tints };
        Queue.Enqueue(request);
        Wanted?.Invoke();
        var picture = await request.Done.Task;
        if (picture is not null && file is not null)
        {
            _ = Task.Run(() => Keep(picture, file));
        }
        return picture;
    }

    /// <summary>Forgets the pictures (another disc).</summary>
    public static void Clear() => Stills.Clear();

    /// <summary>Whether a newer look of the request's machine was asked for since (a colour dragged round the
    /// wheel): its picture is no longer wanted.</summary>
    private static bool Stale(Request request) =>
        Queue.Any(r => r.Slot == request.Slot && r.Asked > request.Asked);

    private static async Task<Bitmap?> PaintAsync(int machine, int? stock, Online.Protocol.Rgb? paint, PixelSize size, int asked)
    {
        // drawn once per machine, look and size, and kept as a PNG beside the machines' cache (MachineCache): the
        // Garage's cells then show pictures only, and no model is loaded for them
        // (a card without its look yet, a moment before its bindings give it one, is drawn but not kept)
        var file = (stock is not null || paint is not null) && await GameSkin.MachinesFolderAsync() is { } folder
            ? MachineCache.StillFile(folder, machine, stock, paint, size.Width, size.Height) : null;
        if (file is not null && await Task.Run(() => Load(file)) is { } kept)
        {
            if (LoadTrace.On)
            {
                LoadTrace.Note($"still {machine}: read from the cache");
            }
            return kept;
        }
        var picture = await DrawnAsync(machine, stock, paint, size, asked);
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
        MachineCache.KeepStill(file, png.ToArray());
    }

    private static async Task<Bitmap?> DrawnAsync(int machine, int? stock, Online.Protocol.Rgb? paint, PixelSize size, int asked)
    {
        if (await GameSkin.Machine3D(machine) is not { } loaded)
        {
            return null;
        }
        // the look's textures and the packing off the window's thread; the drawing on it, in a view's context
        var (packed, pictures) = await Task.Run(() =>
        {
            using var job = LoadTrace.Begin($"still {machine} pack and look");
            using var timed = LoadTrace.Stage("pack+look");
            var file = stock is { } k ? loaded.Paint!.Stock(k) : paint is not null ? loaded.Paint!.Painted(paint) : null;
            return (GpuMesh.Pack(loaded.Model), file is null ? [] : loaded.Paint!.PaintedPictures(file));
        });
        var request = new Request(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"m{machine}"), asked, loaded, packed, pictures, size);
        Queue.Enqueue(request);
        Wanted?.Invoke();
        return await request.Done.Task;
    }

    /// <summary>Draws waiting pictures in the current context (a view's, after its frame), with that view's
    /// <paramref name="kit"/> (made here the first time), within <see cref="BudgetMs"/>: a still's textures go on
    /// the card a few a frame, and it is drawn and read back on a frame of its own once they all are. A still begun
    /// in one view's context is finished there (another context cannot use its textures), unless that view stops
    /// drawing (<see cref="Abandoned"/>).</summary>
    public static void Pump(Gl gl, ref Kit? kit)
    {
        var pumpStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var deadline = pumpStart + (long)(BudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000);
        var drawn = 0;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline)
        {
            if (_current is { } busy && !ReferenceEquals(busy.Kit, kit))
            {
                if (System.Diagnostics.Stopwatch.GetElapsedTime(busy.Touched).TotalMilliseconds < Abandoned)
                {
                    break;   // its own view goes on with it
                }
                _current = null;   // (its textures stay in a context that no longer draws)
                busy.Forget();
                Queue.Enqueue(busy);
            }
            var request = _current;
            if (request is null)
            {
                if (!Queue.TryDequeue(out request))
                {
                    break;
                }
                if (Stale(request))
                {
                    request.Done.TrySetResult(null);
                    continue;
                }
            }
            try
            {
                if (request.Kit is null)
                {
                    kit ??= new Kit(gl);
                    request.Begin(kit);
                    _current = request;
                }
                if (Stale(request))
                {
                    request.Delete();
                    _current = null;
                    request.Done.TrySetResult(null);
                    continue;
                }
                request.Touched = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!request.Textures!.Complete)
                {
                    var upload = System.Diagnostics.Stopwatch.GetTimestamp();
                    var done = request.Textures.UploadSome(deadline);
                    request.UploadMs += System.Diagnostics.Stopwatch.GetElapsedTime(upload).TotalMilliseconds;
                    request.Frames++;
                    if (!done || System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                    {
                        break;   // drawn on a frame of its own
                    }
                }
                _current = null;
                request.Done.TrySetResult(Draw(kit!, request));
            }
            catch (InvalidOperationException e)
            {
                GpuDebug.Note("StillPainter", e.Message);
                _current = null;
                request.Delete();
                request.Done.TrySetResult(null);
            }
            drawn++;
        }
        if (drawn > 0 && LoadTrace.On)
        {
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(pumpStart).TotalMilliseconds;
            _batch = (_batch.Start != 0 ? _batch.Start : _first != 0 ? _first : LoadTrace.Now - ms, _batch.Drawn + drawn, _batch.Thread + ms, Math.Max(_batch.Longest, ms));
            if (Waiting == 0)
            {
                LoadTrace.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"stills: {_batch.Drawn} drawn {LoadTrace.Now - _batch.Start:F0} ms from the first asked; {_batch.Thread:F1} ms on the window's thread, the longest frame's {_batch.Longest:F1} ms"));
                (_batch, _first) = ((0, 0, 0, 0), 0);
            }
        }
        else if (LoadTrace.On && _current is not null)
        {
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(pumpStart).TotalMilliseconds;
            _batch = (_batch.Start != 0 ? _batch.Start : _first != 0 ? _first : LoadTrace.Now - ms, _batch.Drawn, _batch.Thread + ms, Math.Max(_batch.Longest, ms));
        }
    }

    /// <summary>A view's kit going (its context ends): a still begun in it starts again in another.</summary>
    private static void Dropped(Kit kit)
    {
        if (_current is { } busy && ReferenceEquals(busy.Kit, kit))
        {
            busy.Delete();
            _current = null;
            Queue.Enqueue(busy);
        }
    }

    // a batch of stills (from the first asked to the queue empty), for the load trace
    private static (double Start, int Drawn, double Thread, double Longest) _batch;
    private static double _first;

    private static Bitmap Draw(Kit kit, Request request)
    {
        var (machine, size) = (request.Loaded, request.Size);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var (mesh, textures) = (request.Mesh!, request.Textures!);
        try
        {
            foreach (var (texture, picture) in request.Pictures)
            {
                textures.Set(texture, picture);
            }
            var uploaded = request.UploadMs + System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            kit.Target.Begin(size);
            var model = machine.Model;
            var view = StudioCamera.View(model.Center, model.Radius, Yaw, Pitch, Zoom);
            var distance = StudioCamera.Distance(model.Radius, Zoom);
            var projection = StudioCamera.Projection(StudioCamera.FieldOfView / 2, size.Width, size.Height,
                Math.Max(distance - (model.Radius * 1.2f), distance * 0.05f), distance + (model.Radius * 1.2f));
            // a custom machine's parts in their colours, as the builder's 3D view colours them (MachinePreview)
            var tints = request.Tints;
            kit.Studio.PartColour = tints is null ? null
                : part => part >= 0 && part < tints.Length && tints[part] is { } c ? new System.Numerics.Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : null;
            kit.Studio.Opaque(mesh, textures, view, projection);
            kit.Studio.Translucent(mesh, textures, view, projection);
            kit.Studio.PartColour = null;
            kit.Target.Resolve();
            var pixels = kit.Target.Read();
            if (LoadTrace.On)
            {
                LoadTrace.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"still {request.Slot}: upload {uploaded:F1} ms over {request.Frames} frames ({machine.Model.Textures.Count(t => t is not null)} textures + {request.Pictures.Count} look), " +
                    $"drawn and read back {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms"));
            }
            if (GpuDebug.On)
            {
                Large(kit, request, mesh, textures);
                kit.Target.Begin(size);   // the target back at the stills' size
            }
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Premul, pinned.AddrOfPinnedObject(), size, new Vector(96, 96), size.Width * 4);
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            request.Delete();
        }
    }

    /// <summary>For checking every machine as the card draws it (GpuDebug): the still again, large, saved.</summary>
    private static void Large(Kit kit, Request request, GpuMesh mesh, GpuTextures textures)
    {
        var size = new PixelSize(480, 284);
        var model = request.Loaded.Model;
        kit.Target.Begin(size);
        var view = StudioCamera.View(model.Center, model.Radius, Yaw, Pitch, Zoom);
        var distance = StudioCamera.Distance(model.Radius, Zoom);
        var projection = StudioCamera.Projection(StudioCamera.FieldOfView / 2, size.Width, size.Height,
            Math.Max(distance - (model.Radius * 1.2f), distance * 0.05f), distance + (model.Radius * 1.2f));
        kit.Studio.Opaque(mesh, textures, view, projection);
        kit.Studio.Translucent(mesh, textures, view, projection);
        kit.Target.Resolve();
        GpuDebug.Save(kit.Target.Read(), size, $"still-{request.Slot}");
    }

    /// <summary>What a view's context draws the pictures with.</summary>
    internal sealed class Kit(Gl gl)
    {
        public Gl Gl { get; } = gl;

        public Studio Studio { get; } = new(gl);

        public RenderTarget Target { get; } = new(gl);

        public void Delete()
        {
            Dropped(this);
            Studio.Delete();
            Target.Delete();
        }
    }

    private sealed record Request(string Slot, int Asked, GarageMachine Loaded, PackedModel Packed, IReadOnlyList<(int Texture, Picture Picture)> Pictures,
        PixelSize Size)
    {
        public TaskCompletionSource<Bitmap?> Done { get; } = new();

        /// <summary>A custom machine's colours by part (body, cockpit, booster; null a part's own); null a stock machine.</summary>
        public Online.Protocol.Rgb?[]? Tints { get; init; }

        /// <summary>The view's kit whose context its mesh and textures are in (null: not begun).</summary>
        public Kit? Kit { get; private set; }

        public GpuMesh? Mesh { get; private set; }

        public GpuTextures? Textures { get; private set; }

        /// <summary>When a frame last worked on it (Stopwatch ticks).</summary>
        public long Touched { get; set; }

        public double UploadMs { get; set; }

        public int Frames { get; set; }

        /// <summary>Begun in this kit's context: its mesh on the card, its textures to go on a few a frame.</summary>
        public void Begin(Kit kit)
        {
            Kit = kit;
            Mesh = new GpuMesh(kit.Gl, Packed);
            Textures = new GpuTextures(kit.Gl, Loaded.Model.Textures, later: true);
        }

        /// <summary>Its mesh and textures off the card (in its kit's context), to begin again.</summary>
        public void Delete()
        {
            Mesh?.Delete();
            Textures?.Delete();
            Forget();
        }

        /// <summary>Begun again from nothing, its card objects left where they are (a context that stopped drawing).</summary>
        public void Forget() => (Kit, Mesh, Textures, UploadMs, Frames) = (null, null, null, 0, 0);
    }
}
