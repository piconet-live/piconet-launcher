using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Launcher.Core;

namespace Launcher.App.Theme;

/// <summary>A machine as the garage shows it: its model and its paint (a stock machine's; a custom machine has none:
/// its parts are coloured by material, <see cref="Gpu.Studio.PartColour"/>), and for a custom machine where its three
/// parts stand in the model.</summary>
public sealed record GarageMachine(MachineModel Model, MachinePaint? Paint, IReadOnlyList<System.Numerics.Vector3>? Anchors = null);

/// <summary>A course as the Practice page draws it: its course-select model and its venue's colours.</summary>
public sealed record CourseLook(CourseModel Model, VenueSky Sky);

/// <summary>
/// The game's own pictures in the launcher's look, read from the player's disc (<see cref="DiscArt"/>): a venue
/// picture behind the pages (BackdropImage), the blue cloud, the chrome titles, the machine setting's labels and the
/// F-Zero lettering go into the application's resources (keys below; <c>HasDiscArt</c> says whether they are there),
/// and the views ask for venue and machine pictures through the converters. Without a disc nothing is set and
/// the plain look stays. So it does when the disc's art cannot be read: <see cref="Load(string?)"/> runs when the
/// launcher starts, on an image of which only the header has been looked at, and whatever goes wrong in it must not
/// close the window (<see cref="Problem"/>).
/// </summary>
public static partial class GameSkin
{
    /// <summary>The courses whose venue can stand behind the pages: those a server races by default that the disc has
    /// a venue's picture for. (The pool also holds four of the story's stages, which have none: drawn
    /// as a backdrop, they would leave the pages without one.)</summary>
    public static IReadOnlyList<int> BackdropCourses { get; } =
        [.. Courses.Pool.Where(c => Launcher.Core.DiscArt.HasVenue(c) && Online.Protocol.CourseNames.StoryChapter(c) is null)];

    /// <summary>Guards the caches below and <see cref="GameSkin.Custom3D"/>'s: they are filled from worker threads
    /// too (the preload, the previews' loads, the still painters).</summary>
    internal static readonly object Gate = new();

    private static readonly Dictionary<string, Bitmap?> Cache = [];
    private static readonly Dictionary<int, Task<IReadOnlyList<Online.Protocol.Rgb>?>> Swatches = [];
    private static readonly Dictionary<int, CourseModel?> Courses3D = [];
    private static readonly Dictionary<int, Task<GarageMachine?>> Machines3D = [];
    private static readonly Dictionary<int, Task<CourseLook?>> CourseLooks = [];
    private static Task<string?>? _stillsKey;
    private static Task<string?>? _machinesKey;
    private static readonly Task<MachineStatTable?> NoStats = Task.FromResult<MachineStatTable?>(null);
    private static Task<MachineStatTable?> _stats = NoStats;
    private static DiscArt? _art;
    private static string? _disc;
    private static string? _failed;

    /// <summary>The resources <see cref="Load(string?)"/> sets from the disc.</summary>
    private static readonly string[] Pictures =
    [
        "CloudImage", "AccelerationLabel", "MaxSpeedLabel", "BrandLettering", "GarageLettering", "BackdropImage",
        .. Enum.GetValues<MenuTitle>().Select(title => $"Title.{title}"),
    ];

    /// <summary>Why the last <see cref="Load(string?)"/> left the plain look although the disc is the game's (its
    /// art could not be read), as written to the launcher's log; null: it did not.</summary>
    public static string? Problem { get; private set; }

    /// <summary>Uses the pictures of this disc (once per disc; none when it is not the game's). It does not raise:
    /// art that cannot be read is the plain look and one line in the launcher's log.</summary>
    public static void Load(string? discPath) => Load(discPath, DiscArt.Open);

    /// <param name="open">How a disc's art is opened (a test gives one that fails).</param>
    public static void Load(string? discPath, Func<string?, DiscArt?> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (_disc == discPath && (_art is not null || _failed == discPath))
        {
            return;   // (a disc whose art failed is not tried again at every change of the settings)
        }
        _failed = null;
        Problem = null;
        try
        {
            Read(discPath, open);
        }
#pragma warning disable CA1031 // whatever it is: the look is no reason to close the launcher
        catch (Exception e)
#pragma warning restore CA1031
        {
            _disc = discPath;
            _failed = discPath;
            Problem = $"the disc's art could not be read ({e.GetType().Name}: {e.Message}); the launcher shows its plain look";
            Program.Faults?.Note(Problem);
            Plain();
        }
    }

    /// <summary>The plain look: nothing from a disc.</summary>
    private static void Plain()
    {
        try
        {
            _art?.Dispose();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            // it was the art that failed
        }
        _art = null;
        _stats = NoStats;
        Sounds(null);
        lock (Gate)
        {
            Cache.Clear();
            Swatches.Clear();
            Courses3D.Clear();
            Machines3D.Clear();
            CourseLooks.Clear();
            ClearCustoms3D();
        }
        _stillsKey = null;
        _machinesKey = null;
        Gpu.CourseStillPainter.Clear();
        if (Application.Current?.Resources is { } resources)
        {
            resources["HasDiscArt"] = false;
            resources["NoDiscArt"] = true;
            foreach (var key in Pictures)
            {
                resources.Remove(key);
            }
        }
    }

    private static void Read(string? discPath, Func<string?, DiscArt?> open)
    {
        _art?.Dispose();
        lock (Gate)
        {
            Cache.Clear();
            Swatches.Clear();
            Courses3D.Clear();
            Machines3D.Clear();
            CourseLooks.Clear();
            ClearCustoms3D();
        }
        _stillsKey = null;
        _machinesKey = null;
        Gpu.StillPainter.Clear();
        Gpu.CourseStillPainter.Clear();
        _disc = discPath;
        _art = null;
        _stats = NoStats;
        _art = open(discPath);
        Sounds(_art);
        // the player's own machine and course first, before the swatches' many reads below take the disc
        PreloadOwn();
        var resources = Application.Current!.Resources;
        resources["HasDiscArt"] = _art is not null;
        resources["NoDiscArt"] = _art is null;
        Set(resources, "CloudImage", "cloud", a => a.Cloud);
        Set(resources, "AccelerationLabel", "acceleration", a => a.SettingLabels?.Acceleration);
        Set(resources, "MaxSpeedLabel", "maxspeed", a => a.SettingLabels?.MaxSpeed);
        Set(resources, "BrandLettering", "brand", a => a.Lettering(Branding.DisplayName));
        Set(resources, "GarageLettering", "garage", a => a.Lettering("Garage"));
        // behind every page: a venue of the races, a different one each time the launcher opens
        var backdrop = BackdropCourses[Random.Shared.Next(BackdropCourses.Count)];
        Set(resources, "BackdropImage", $"venue{backdrop}", a => a.Venue(backdrop));
        foreach (var title in Enum.GetValues<MenuTitle>())
        {
            Set(resources, $"Title.{title}", $"title{title}", a => a.Title(title));
        }
        // every machine's stock colours, read in the background (tens of milliseconds each): the garage and the race's
        // map ask for them
        if (_art is { } art)
        {
            for (var m = 0; m < Launcher.Core.Machines.Names.Count; m++)
            {
                var machine = m;
                var swatches = Task.Run(() => Quietly(() =>
                {
                    using var job = LoadTrace.Begin($"machine {machine} swatches");
                    return art.StockSwatches(machine);
                }));
                lock (Gate)
                {
                    Swatches[machine] = swatches;
                }
            }
            // and the machines' stats (two files, tens of milliseconds): the garage shows and sorts by them
            _stats = Task.Run(() => Quietly(() => art.MachineStats()));
        }
    }

    private static int _soundsLoad;

    /// <summary>The game's menu sounds for the launcher's (<see cref="GameSounds"/>), from this disc's bank: from the
    /// data folder's cache, or decoded and kept there, on a worker thread (a disc can be on a network share). Until
    /// they are there, and without a disc, the launcher's own tones sound.</summary>
    private static void Sounds(DiscArt? art)
    {
        var load = Interlocked.Increment(ref _soundsLoad);
        Attention.Game = null;
        if (art is null)
        {
            return;
        }
        var data = DataFolder;
        _ = Task.Run(() =>
        {
            var sounds = Quietly(() => GameSounds.Load(art.SoundBank(), data));
            if (Volatile.Read(ref _soundsLoad) == load)
            {
                Attention.Game = sounds;   // (a disc set since has its own load)
            }
        });
    }

    /// <summary>What the window shows first, loaded as soon as a disc's art is read, ahead of everything else read
    /// from it: the player's machine (a custom machine's parts when it is one) and the course the Practice page opens
    /// on. Set before <see cref="Load(string?)"/>.</summary>
    public static (RacedMachine? Machine, int? Course) Own { get; set; }

    private static void PreloadOwn()
    {
        var (raced, course) = Own;
        var machine = raced?.Custom is { } c ? Custom3D(c.Body, c.Cockpit, c.Booster)
            : raced is { Machine: >= 0 } r ? Machine3D(r.Machine) : null;
        // (packed too: the views share a model's packing)
        _ = machine?.ContinueWith(t => t.Result is { } m ? Gpu.GpuMesh.Pack(m.Model) : null, CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        if (course is { } k)
        {
            _ = CourseLookAsync(k);
        }
    }

    /// <summary>Every machine's stats from the disc (<see cref="DiscArt.MachineStats()"/>), once read; null without
    /// the disc or when its car data cannot be read.</summary>
    public static Task<MachineStatTable?> MachineStatsAsync() => _stats;

    /// <summary>A machine's model and paint for the garage's preview, read once on a worker thread (null without the
    /// disc or when they cannot be read).</summary>
    public static Task<GarageMachine?> Machine3D(int machine)
    {
        lock (Gate)
        {
        if (!Machines3D.TryGetValue(machine, out var task))
        {
            var art = _art;
            var asked = LoadTrace.Now;
            task = art is null ? Task.FromResult<GarageMachine?>(null) : Task.Run(async () =>
            {
                var file = await MachinesFolderAsync() is { } folder ? MachineCache.StockFile(folder, machine) : null;
                using var job = LoadTrace.Begin($"machine {machine} model (asked +{asked:F0}, queued {LoadTrace.Now - asked:F0} ms)");
                if (Kept(file) is { Paint: { } kept } cached)
                {
                    return new GarageMachine(cached.Model, kept);
                }
                var loaded = Quietly(() => art.MachineModel(machine) is { } model && art.Paint(machine) is { } paint ? new GarageMachine(model, paint) : null);
                Keep(file, loaded);
                return loaded;
            });
            Machines3D[machine] = task;
        }
        return task;
        }
    }

    /// <summary>The folder the machines' models of this disc are kept in (<see cref="MachineCache"/>), its key worked
    /// out once on a worker thread; null without the disc or a data folder.</summary>
    internal static Task<string?> MachinesFolderAsync()
    {
        if (_machinesKey is null)
        {
            var (art, data) = (_art, DataFolder);
            _machinesKey = art is null || data is null ? Task.FromResult<string?>(null)
                : Task.Run(() => Quietly(() => MachineCache.Folder(data, art.MachinesKey())));
        }
        return _machinesKey;
    }

    /// <summary>A machine kept in the cache's <paramref name="file"/>; null when there is none to read.</summary>
    private static CachedMachine? Kept(string? file)
    {
        using var timed = LoadTrace.Stage("cache read");
        return file is null ? null : MachineCache.Read(file);
    }

    /// <summary>Keeps a machine read from the disc in the cache's <paramref name="file"/>, in the background.</summary>
    private static void Keep(string? file, GarageMachine? loaded)
    {
        if (file is not null && loaded is not null)
        {
            _ = Task.Run(() => MachineCache.Keep(file, new CachedMachine(loaded.Model, loaded.Paint, loaded.Anchors)));
        }
    }

    /// <summary>A machine's four stock colours as swatches (0 its default), once read; null without the disc.</summary>
    public static Task<IReadOnlyList<Online.Protocol.Rgb>?> StockSwatchesAsync(int machine)
    {
        lock (Gate)
        {
            return Swatches.TryGetValue(machine, out var task) ? task : Task.FromResult<IReadOnlyList<Online.Protocol.Rgb>?>(null);
        }
    }

    // a disc changed under a reading (its files closed) gives nothing rather than an error
    private static T? Quietly<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    public static Bitmap? Venue(int course) =>
        Get($"venue{course}", a => course == Online.Protocol.CourseVote.RandomCourse ? a.AnyVenue : a.Venue(course));

    /// <summary>A machine's four stock colours as swatches (0 its default); null without the disc. Waits for them when
    /// they are still being read.</summary>
    public static IReadOnlyList<Online.Protocol.Rgb>? StockSwatches(int machine) => StockSwatchesAsync(machine).GetAwaiter().GetResult();

    /// <summary>A course's model from the course-select screen, the track map; null without the disc.</summary>
    public static CourseModel? Course(int course)
    {
        lock (Gate)
        {
            if (!Courses3D.TryGetValue(course, out var model))
            {
                model = _art?.Course(course);
                Courses3D[course] = model;
            }
            return model;
        }
    }

    /// <summary>Where the Practice page's course stills are kept (the data folder; <see cref="CourseStillCache"/>);
    /// null: they are drawn each time and kept nowhere (a test's window).</summary>
    public static string? DataFolder { get; set; }

    /// <summary>A course's model with its venue's colours, for the Practice page (the hero, the tiles), read once on a
    /// worker thread; null without the disc or when the disc has no model for the course.</summary>
    public static Task<CourseLook?> CourseLookAsync(int course)
    {
        lock (Gate)
        {
            if (!CourseLooks.TryGetValue(course, out var task))
            {
                var art = _art;
                task = art is null ? Task.FromResult<CourseLook?>(null) : Task.Run(() => Quietly(() =>
                    art.Course(course) is { Positions.Length: > 0, Indices.Length: > 0 } model
                        ? new CourseLook(model, VenueSky.From(art.Venue(course))) : null));
                CourseLooks[course] = task;
            }
            return task;
        }
    }

    /// <summary>The folder the course stills of this disc are kept in (<see cref="CourseStillCache"/>), its key worked
    /// out once on a worker thread from the disc's course files; null without the disc or a data folder.</summary>
    public static Task<string?> CourseStillsFolderAsync(PixelSize size)
    {
        if (_stillsKey is null)
        {
            var (art, data) = (_art, DataFolder);
            _stillsKey = art is null || data is null ? Task.FromResult<string?>(null) : Task.Run(() => Quietly(() =>
                CourseStillCache.Folder(data, CourseStillCache.DiscKey(art.CourseFiles(CourseCups.All.Select(c => c.Course)), size.Width, size.Height))));
        }
        return _stillsKey;
    }

    /// <summary>The launcher's name in the game's lettering, as the title bar shows it (BrandLettering), for the game
    /// window's strip; null without the disc's art (a start without the window has none).</summary>
    public static Picture? BrandPicture => _art is { } art ? Quietly(() => art.Lettering(Branding.DisplayName)) : null;

    private static void Set(IResourceDictionary resources, string key, string name, Func<DiscArt, Picture?> picture)
    {
        if (Get(name, picture) is { } bitmap)
        {
            resources[key] = bitmap;
        }
        else
        {
            resources.Remove(key);
        }
    }

    private static Bitmap? Get(string name, Func<DiscArt, Picture?> picture)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(name, out var bitmap))
            {
                bitmap = _art is not null && picture(_art) is { } p ? ToBitmap(p) : null;
                Cache[name] = bitmap;
            }
            return bitmap;
        }
    }

    private static Bitmap ToBitmap(Picture p)
    {
        var pinned = GCHandle.Alloc(p.Rgba, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, pinned.AddrOfPinnedObject(),
                new PixelSize(p.Width, p.Height), new Vector(96, 96), p.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }
}
