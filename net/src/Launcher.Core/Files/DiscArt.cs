using System.Buffers.Binary;
using System.Numerics;
using Fzgx.GameCube.Compression;
using Fzgx.GameCube.Disc;
using Fzgx.GameCube.Models;
using Fzgx.GameCube.Textures;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A decoded picture: RGBA pixels (not premultiplied), row by row from the top.</summary>
public sealed record Picture(int Width, int Height, byte[] Rgba);

/// <summary>The chrome titles of the game's menus (bmp_e/bmp_sel, by index).</summary>
public enum MenuTitle
{
    SelectCourse = 17,
    Course = 18,
    SelectCup = 19,
    MachineSettings = 20,
    SelectMode = 21,
    SelectMachine = 22,
    Versus = 23,
}

/// <summary>
/// The game's pictures, read from the player's own disc for the launcher's look:
/// each venue's course-select picture, each machine's name plate, the blue cloud, the
/// chrome titles, the machine setting's labels and the F-Zero lettering. Nothing from
/// the game is stored in the launcher; every picture is decoded from the disc when asked for. Thread-safe.
/// <para>The image is the player's file and is read here before any preparation has checked its files (the theme
/// loads as soon as the page's first look says it is the game's disc), so it is read as the preparation reads it:
/// through <see cref="DiscImage"/>, whose file table costs a bounded amount whatever it says
/// (<see cref="DiscImage.TableOf"/>), and no file longer than <see cref="LargestFile"/> is taken. A file's own bytes
/// then go to readers that were written for the game's files (the library's decompressor, texture and model readers,
/// the launcher's archive, model and paint readers): what a file says of its own size is checked first
/// (<see cref="LargestUnpacked"/>, <see cref="LargestTexture"/>), and whatever those readers raise on bytes that are
/// not the game's is no picture or no model (<see cref="Unreadable"/>). **No member raises on a disc's bytes.**</para>
/// </summary>
public sealed partial class DiscArt : IDisposable
{
    private const string Select = "bmp_e/bmp_sel.tpl";
    private const string Common = "bmp_e/bmp_com.tpl";

    /// <summary>Each venue's file suffix by course number (bmp_e/stg/bmp_stg_*.tpl; "sto" a story chapter's own
    /// picture, bmp_e/sstg/bmp_sstg_*.tpl).</summary>
    private static readonly Dictionary<int, string> Venues = new()
    {
        [1] = "mut", [3] = "mut", [36] = "mut",
        [5] = "tow", [21] = "tow", [31] = "tow",
        [7] = "por", [13] = "por", [33] = "por",
        [8] = "lig", [9] = "lig", [34] = "lig",
        [10] = "for", [11] = "for", [35] = "for",
        [14] = "big", [27] = "big",
        [15] = "fir", [17] = "fir",
        [16] = "cas", [29] = "cas",
        [24] = "ele",   // Cosmo Terminal (its music is elev.adx)
        [25] = "san", [26] = "san",
        [28] = "rai",
        [32] = "met",
        // the story's chapters that race at a cup venue, by its picture: chapter 3 at Casino
        // Palace, 7 at Mute City, 9 at Phantom Road (CourseNames.FirstStoryStage is chapter 1); chapter 8 by the story
        // mode's own picture of its place, which no cup venue and no name of the game's has
        [39] = "cas", [43] = "mut", [44] = "sto08", [45] = "rai",
    };

    /// <summary>Whether the disc has a venue's picture this launcher knows for the course (<see cref="Venue"/>): the
    /// cup courses have, and the story chapters named in <see cref="Venues"/>.</summary>
    public static bool HasVenue(int course) => Venues.ContainsKey(course);

    /// <summary>A cup course at the same venue as this one (the least numbered), for the venue's name; null when
    /// the course has no venue here.</summary>
    public static int? SameVenue(int course) =>
        Venues.TryGetValue(course, out var venue)
            ? Venues.Where(v => v.Value == venue && v.Key < Online.Protocol.CourseNames.FirstStoryStage).Min(v => (int?)v.Key)
            : null;

    /// <summary>Each machine's short name on the disc (vehicle/*, bmp_e/carname, bmp_e/cardt), by the game's machine
    /// id (<see cref="Machines.Names"/>).</summary>
    public static IReadOnlyList<string> MachineFiles { get; } =
    [
        "rgaze", "wcat", "gfox", "itiger", "fsting", "wgoose", "bfalcon", "dclaw", "gstar", "lwy", "mwolf", "spira",
        "danc", "arobin", "bfang", "sphan", "gpan", "hspeed", "sang", "kmete", "qmete", "tnori", "nthun", "wboar",
        "hhawk", "wwasp", "mty", "mhurr", "cbear", "bbull", "darks", "fats", "scarp", "pinks", "mgull", "srat", "smoon",
        "prabbit", "gtaxi", "rrock", "rainp",
    ];

    /// <summary>The longest file read for the launcher's look: 16 MiB (the game's longest of these, a machine
    /// archive, is under 3 MiB). A table may give any file any length up to the image's.</summary>
    public const int LargestFile = 16 << 20;

    /// <summary>The most a compressed file (<c>NAME.lz</c>) may say it unpacks to: 24 MiB, the console's whole
    /// main memory (<see cref="DiscImage.ConsoleMemory"/>), into which the game unpacks every such file. The
    /// library's decompressor makes room for the size the file's first bytes name before it reads further: a file of
    /// eight bytes that names 2 GiB took 2 GiB, and one that names more raised an error nothing catches.</summary>
    public const int LargestUnpacked = DiscImage.ConsoleMemory;

    /// <summary>The widest and tallest texture decoded: 1024, the console's own limit.</summary>
    public const int LargestTexture = 1024;

    private readonly DiscImage _image;
    private readonly Dictionary<string, DiscFile> _table;
    private readonly Dictionary<string, AvTpl?> _files = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private DiscArt(DiscImage image, Dictionary<string, DiscFile> table) => (_image, _table) = (image, table);

    /// <summary>The player's disc's pictures, or null when it is not F-Zero GX (USA) or cannot be read.</summary>
    public static DiscArt? Open(string? discPath)
    {
        if (DiscCheck.Check(discPath) != DiscStatus.Ok)
        {
            return null;
        }
        DiscImage? image = null;
        try
        {
            image = DiscImage.Open(discPath!);
            // the bounded reader: a table without end, folders inside folders without end, a path named twice are
            // each an error of these kinds here, where the library's reader ran out of memory or threw another
            var table = image.Table().Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            return new DiscArt(image, table);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            image?.Dispose();
            return null;
        }
    }

    /// <summary>Whether the disc has this file (a path from its root, with '/').</summary>
    private bool Contains(string file) => _table.ContainsKey(file);

    /// <summary>What the readers of a disc file's bytes raise on bytes that are not the game's, by what they did
    /// when fed changed files (the second review: 16 million inputs): the kinds they name themselves (invalid data,
    /// an argument out of range, the end of the stream), and the ones that come from reading past a list, adding
    /// up sizes, a format the library does not decode, or a list that turned out empty. Not running out of memory:
    /// the sizes a file names are checked before anything is made for them.</summary>
    private static bool Unreadable(Exception e) =>
        e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException or NotSupportedException
            or ArithmeticException or InvalidOperationException or FormatException or KeyNotFoundException or InvalidCastException
            or NullReferenceException;

    /// <summary>A disc file's bytes, unpacked when the disc holds it compressed (<c>NAME.lz</c>); null when the disc
    /// has neither. Called with <see cref="_lock"/> held.</summary>
    /// <exception cref="InvalidDataException">The compressed file names a size no file of the game has
    /// (<see cref="LargestUnpacked"/>), or is cut off before it names one.</exception>
    private byte[]? Bytes(string file)
    {
        if (!Contains(file + ".lz"))
        {
            return Contains(file) ? ReadFile(file) : null;
        }
        var packed = ReadFile(file + ".lz");
        using var lz = LoadTrace.Stage("lz");
        // (two words, low byte first: the packed length and the unpacked one)
        if (packed.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4)) is < 0 or > LargestUnpacked)
        {
            throw new InvalidDataException($"{file}.lz names a size no file of the game unpacks to.");
        }
        return AvLz.Decompress(packed);
    }

    /// <summary>A file's bytes. Called with <see cref="_lock"/> held: the image is one stream.</summary>
    /// <exception cref="InvalidDataException">The file is longer than the launcher's look reads, or the image ends
    /// inside it.</exception>
    private byte[] ReadFile(string file)
    {
        var entry = _table[file];
        if (entry.Length > LargestFile)
        {
            throw new InvalidDataException($"{file} is longer than a file of the launcher's look.");
        }
        var bytes = new byte[entry.Length];
        using var read = LoadTrace.Stage(file.EndsWith("carcmntex.tpl", StringComparison.Ordinal) ? "read.shared" : "read");
        _image.Read(entry.Offset, bytes, file);
        return bytes;
    }

    /// <summary>The machine setting's two ends, "ACCELERATION" and "MAX SPEED" (bmp_sel 35, one above the other).</summary>
    public (Picture Acceleration, Picture MaxSpeed)? SettingLabels =>
        Texture(Select, 35) is { Height: 40 } both ? (Rows(both, 0, 19), Rows(both, 19, 40)) : null;   // (a picture's pixels are whole: Texture)

    /// <summary>The blue cloud behind the menus (512×512).</summary>
    public Picture? Cloud => Texture("init/sel.tpl", 1);

    public Picture? Title(MenuTitle title) => Texture(Select, (int)title);

    /// <summary>The course's venue picture from the course select (640×480 without its black bars), or null for a
    /// course without one.</summary>
    public Picture? Venue(int course) =>
        Venues.TryGetValue(course, out var venue) && Texture(VenueFile(venue), 0) is { } p ? WithoutBars(p) : null;

    private static string VenueFile(string venue) =>
        venue.StartsWith("sto", StringComparison.Ordinal) ? $"bmp_e/sstg/bmp_sstg_{venue}.tpl" : $"bmp_e/stg/bmp_stg_{venue}.tpl";

    /// <summary>The course select's picture that belongs to no venue (a city at night): for "Random course".</summary>
    public Picture? AnyVenue => Texture("bmp_e/stg/bmp_stg_com.tpl", 0) is { } p ? WithoutBars(p) : null;

    /// <summary><paramref name="text"/> in the game's F-Zero face (bmp_com 8: 18×18 cells, space to 's', capitals
    /// used), in chrome as the menus' titles; null without the font.</summary>
    public Picture? Lettering(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return Letters(text);
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;   // a font picture of another size than the game's
        }
    }

    private Picture? Letters(string text)
    {
        if (Texture(Common, 8) is not { } atlas)
        {
            return null;
        }
        const int cell = 18, columns = 12, first = 0x20, last = 0x73, gap = 2, space = 8;
        var upper = text.ToUpperInvariant();
        // each glyph by its inked columns, so narrow letters (I, 1) do not leave gaps as the fixed cells would
        var glyphs = new List<(int X, int From, int To, int Row)>();
        var width = 0;
        foreach (var code in upper)
        {
            if (code is ' ' || code < first || code > last)
            {
                width += space;
                continue;
            }
            var n = code - first;
            var (sx, sy) = (n % columns * cell, n / columns * cell);
            int from = cell, to = -1;
            for (var x = 0; x < cell; x++)
            {
                for (var y = 0; y < cell; y++)
                {
                    if (atlas.Rgba[((((sy + y) * atlas.Width) + sx + x) * 4) + 3] > 0)
                    {
                        (from, to) = (Math.Min(from, x), Math.Max(to, x));
                        break;
                    }
                }
            }
            if (to < 0)
            {
                width += space;
                continue;
            }
            glyphs.Add((width, sx + from, sx + to + 1, sy));
            width += to - from + 1 + gap;
        }
        width = Math.Max(1, width - gap);
        var rgba = new byte[width * cell * 4];
        foreach (var (at, from, to, sy) in glyphs)
        {
            for (var y = 0; y < cell; y++)
            {
                var tint = Chrome(y / (double)(cell - 1));
                for (var x = from; x < to; x++)
                {
                    var source = (((sy + y) * atlas.Width) + x) * 4;
                    var target = ((y * width) + at + x - from) * 4;
                    rgba[target] = tint;
                    rgba[target + 1] = (byte)Math.Min(255, tint + 6);
                    rgba[target + 2] = (byte)Math.Min(255, tint + 12);
                    rgba[target + 3] = atlas.Rgba[source + 3];
                }
            }
        }
        return new Picture(width, cell, rgba);
    }

    public void Dispose() => _image.Dispose();

    /// <summary>A machine's four stock colours (0 its default) as swatches (<see cref="MachinePaint.Swatches"/>), from
    /// its opponent set (<c>vehicle/NAME/NAME_svN.tpl</c>: the main set's paint at half the size, the same colours,
    /// quicker to read). Null when the files are missing or hold no paint.</summary>
    public IReadOnlyList<Rgb>? StockSwatches(int machine)
    {
        try
        {
            return Paint(machine, main: false)?.Swatches();
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>A machine's paint (<see cref="MachinePaint"/>): its main set (<c>NAME.tpl</c>, <c>NAME_cv1..3.tpl</c>),
    /// the player's own machine in a race, or with <paramref name="main"/> false the opponents' set (<c>NAME_sv0..3</c>).
    /// Null when the files are missing.</summary>
    public MachinePaint? Paint(int machine, bool main = true)
    {
        if (machine < 0 || machine >= MachineFiles.Count)
        {
            return null;
        }
        var name = MachineFiles[machine];
        var files = Enumerable.Range(0, 4)
            .Select(k => Read($"vehicle/{name}/{name}{(main ? k == 0 ? "" : $"_cv{k}" : $"_sv{k}")}.tpl")).ToList();
        try
        {
            using var paint = LoadTrace.Stage("paint");
            return files.Any(f => f is null) ? null : MachinePaint.Of(files!);
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>A machine's model as the player's own machine is drawn in a race (<see cref="MachineModel"/>), from its
    /// archive <c>vehicle/NAME_p.arc</c> (its objects placed by its <c>NAME.fmi</c>) with the shared machine textures
    /// (<c>vehicle/carcmntex.tpl</c>). Null when the
    /// files are missing or cannot be read.</summary>
    public MachineModel? MachineModel(int machine)
    {
        if (machine < 0 || machine >= MachineFiles.Count)
        {
            return null;
        }
        var name = MachineFiles[machine];
        try
        {
            var archive = Read($"vehicle/{name}_p.arc") is { } arc ? Traced("parse", () => U8Archive.Parse(arc)) : null;
            return archive?.File($"{name}.gma") is { } gma && archive.File($"{name}.tpl") is { } tpl
                ? Core.MachineModel.Read(gma, tpl, Read("vehicle/carcmntex.tpl"), archive.File($"{name}.fmi"))
                : null;
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;   // (a model without a mesh ended in "Sequence contains no elements", which was not caught)
        }
    }

    private static T Traced<T>(string stage, Func<T> work)
    {
        using var timed = LoadTrace.Stage(stage);
        return work();
    }

    /// <summary>A disc file's bytes (from its <c>.lz</c> when compressed), or null when it is missing or cannot be
    /// read.</summary>
    private byte[]? Read(string file)
    {
        var waiting = LoadTrace.Stage("lock");
        lock (_lock)
        {
            waiting.Dispose();
            try
            {
                return Bytes(file);
            }
            catch (Exception e) when (Unreadable(e))
            {
                return null;
            }
        }
    }

    /// <summary>Texture <paramref name="index"/> of a disc texture file (read from its <c>.lz</c> when compressed);
    /// null if the file or slot is missing or cannot be decoded.</summary>
    public Picture? Texture(string file, int index)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock)
        {
            try
            {
                // a texture's size is its file's word: none larger than the console's is decoded, and a picture
                // has all its pixels or is none (those who cut pictures up count on it)
                if (Load(file) is not { } tpl || index < 0 || index >= tpl.Textures.Count
                    || tpl.Textures[index] is not { IsEmpty: false, Width: > 0 and <= LargestTexture, Height: > 0 and <= LargestTexture } texture
                    || tpl.DecodeLevel0(texture) is not { } rgba || rgba.Length != texture.Width * texture.Height * 4)
                {
                    return null;
                }
                return new Picture(texture.Width, texture.Height, rgba);
            }
            catch (Exception e) when (Unreadable(e))
            {
                return null;   // (a format the library does not decode was raised here, outside the file's own catch)
            }
        }
    }

    private AvTpl? Load(string file)
    {
        if (!_files.TryGetValue(file, out var tpl))
        {
            try
            {
                tpl = Bytes(file) is { } bytes ? AvTpl.Parse(bytes) : null;
            }
            catch (Exception e) when (Unreadable(e))
            {
                tpl = null;
            }
            _files[file] = tpl;
        }
        return tpl;
    }

    /// <summary>The chrome of the menus' titles at height <paramref name="t"/> (0 top, 1 foot): white through steel
    /// to a darker band and light again.</summary>
    private static byte Chrome(double t) => (byte)(t switch
    {
        < 0.45 => 255 - (t / 0.45 * 43),
        < 0.55 => 212 - ((t - 0.45) / 0.10 * 88),
        _ => 124 + ((t - 0.55) / 0.45 * 108),
    });

    /// <summary>Rows <paramref name="from"/> up to <paramref name="to"/> of a picture.</summary>
    internal static Picture Rows(Picture p, int from, int to)
    {
        var rgba = new byte[p.Width * (to - from) * 4];
        p.Rgba.AsSpan(from * p.Width * 4, rgba.Length).CopyTo(rgba);
        return new Picture(p.Width, to - from, rgba);
    }

    /// <summary>The picture without the black rows above and below it (the course select's pictures are letterboxed).</summary>
    internal static Picture WithoutBars(Picture p)
    {
        bool Black(int y)
        {
            for (var x = 0; x < p.Width; x++)
            {
                var i = ((y * p.Width) + x) * 4;
                if (p.Rgba[i] > 12 || p.Rgba[i + 1] > 12 || p.Rgba[i + 2] > 12)
                {
                    return false;
                }
            }
            return true;
        }
        var top = 0;
        while (top < p.Height - 1 && Black(top))
        {
            top++;
        }
        var bottom = p.Height;
        while (bottom > top + 1 && Black(bottom - 1))
        {
            bottom--;
        }
        var rows = bottom - top;
        var rgba = new byte[p.Width * rows * 4];
        p.Rgba.AsSpan(top * p.Width * 4, rgba.Length).CopyTo(rgba);
        return new Picture(p.Width, rows, rgba);
    }
}
