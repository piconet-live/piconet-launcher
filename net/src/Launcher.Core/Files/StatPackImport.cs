using System.Buffers.Binary;
using System.Globalization;
using Fzgx.GameCube.Compression;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A stat pack made from a changed car data file (the protocol's handbook/stat-packs.md): every word of the file's 116
/// blocks that differs from the player's own disc's, as a pack file. The launcher's <c>--import-stat-pack</c>. The
/// pack holds the differences only, so nothing of the disc's is in it but the values the file's author changed; a
/// whole replacement file is a pack that happens to be large.
/// <para>The changed file is somebody's, and can be anything: it is read only when it is small enough to be car
/// data, unpacked only when it names a size car data has, and taken for car data only by its shape. Never word 0 of
/// a block (the place of the name's address), and no name: a pack carries none.</para>
/// </summary>
public static partial class StatPackImport
{
    /// <summary>The exit status of an import that wrote its pack; that refused its files or its values; whose pack
    /// could not be written.</summary>
    public const int Written = 0, Refused = 2, NotWritten = 5;

    /// <summary>The largest car data: the game's loader has this much room for the unpacked file.</summary>
    public const int LargestCarData = 0x6000;

    private const int Names = (int)GameFacts.Files.CarNamesAt, PartNames = (int)GameFacts.Files.CarPartNamesAt;

    /// <summary>A file's bytes as unpacked car data: the file itself, or what it unpacks to when it is packed as the
    /// disc's <c>.lz</c> files are (two words, low byte first: the packed length, the unpacked one). Null and the
    /// sentence that says why when it is not car data by its shape: its size, a block whose first word is not 0 (in
    /// the file every block's is; the game puts the name's address there), the zero bytes before the names.</summary>
    public static (byte[]? Data, string? Refusal) CarDataOf(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var data = file;
        if (file.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(file) == (uint)(file.Length - 8))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)) > LargestCarData)
            {
                return (null, "It is a packed file that unpacks to more than any car data.");
            }
            try
            {
                data = AvLz.Decompress(file);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException
                                          or ArithmeticException or InvalidOperationException or NotSupportedException)
            {
                return (null, "It is a packed file that does not unpack.");
            }
        }
        if (data.Length is < PartNames or > LargestCarData)
        {
            return (null, string.Create(CultureInfo.InvariantCulture,
                $"It is {data.Length:N0} bytes unpacked, and car data is {PartNames:N0} to {LargestCarData:N0}."));
        }
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(StatPackRules.BlockAt(block))) != 0)
            {
                return (null, $"The first word of its block {block} is not 0, as every block's of a car data file is.");
            }
        }
        return data.AsSpan(StatPackRules.Machines * StatPackRules.BlockSize, Names - (StatPackRules.Machines * StatPackRules.BlockSize)).ContainsAnyExcept((byte)0)
            ? (null, "The bytes between its machines and their names are not 0, as a car data file's are.")
            : (data, null);
    }

    /// <summary>A word of a block as a pack holds it: the file's words are low byte first, the game's high byte
    /// first, and a pack's bits are the game's; the four bytes at 0x48 stand as they are in both.</summary>
    private static uint Word(ReadOnlySpan<byte> data, int block, int at)
    {
        var word = data[(StatPackRules.BlockAt(block) + at)..];
        return at == (int)GameFacts.Files.CarBytesAt ? BinaryPrimitives.ReadUInt32BigEndian(word) : BinaryPrimitives.ReadUInt32LittleEndian(word);
    }

    /// <summary>The pack of every word of <paramref name="changed"/> that differs from the disc's, both unpacked car
    /// data (<see cref="CarDataOf"/>); or the sentence that says why there is none: no difference, a value outside
    /// its range (named), a name that is none. NamesDiffer: the changed file's names are not the disc's, which no
    /// pack carries.</summary>
    public static (StatPack? Pack, string? Refusal, bool NamesDiffer) Differences(ReadOnlySpan<byte> disc, ReadOnlySpan<byte> changed, string name,
        string? author = null, IReadOnlyList<StatEntry>? constants = null)
    {
        var entries = new List<StatEntry>();
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            for (var at = 4; at < StatPackRules.BlockSize; at += 4)
            {
                if (Word(changed, block, at) is var word && word != Word(disc, block, at))
                {
                    entries.Add(new StatEntry(block, at, word));
                }
            }
        }
        var names = !disc[Names..(int)GameFacts.Files.CarPartsAt].SequenceEqual(changed[Names..(int)GameFacts.Files.CarPartsAt])
            || !disc[PartNames..].SequenceEqual(changed[PartNames..]);
        if (entries.Count == 0 && constants is not { Count: > 0 })
        {
            return (null, "Every value of the file is the disc's own: there is nothing for a pack to hold.", names);
        }
        entries.AddRange(constants ?? []);
        var (pack, refusal) = StatPack.Of(name, entries, author);
        return (pack, refusal, names);
    }

    /// <summary>The import: the disc image's car data against the file at <paramref name="carDataPath"/>, the pack
    /// written to <paramref name="outPath"/> (without one: beside the car data file, named after the pack). What it
    /// did, or why it did nothing, goes to <paramref name="output"/> a line each; the result is the exit status.</summary>
    /// <param name="anyDisc">The image is taken without being held to the pinned disc (a pack diffed against another
    /// image than every player's is another pack: only for a test's made-up image, or knowingly).</param>
    /// <param name="overwrite">A file that is already at the output's place is replaced.</param>
    /// <param name="pins">Which disc is the game (the launcher's own: <see cref="DiscPins.Game"/>).</param>
    /// <param name="author">Who made the pack, for its file's <c>author</c> line (1 to 40 plain characters); null: no
    /// such line.</param>
    /// <param name="modulePath">A modified main module (<c>enemy_line/line__.bin</c>): its allowed code constants go
    /// into the pack too, and every other difference is named and left out (<see cref="ModuleDifferences"/>).</param>
    public static int Run(string? discPath, string carDataPath, string? name, string? outPath, TextWriter output,
        bool anyDisc = false, bool overwrite = false, DiscPins? pins = null, string? author = null, string? modulePath = null)
    {
        ArgumentNullException.ThrowIfNull(carDataPath);
        ArgumentNullException.ThrowIfNull(output);
        int Refuse(string why)
        {
            output.WriteLine("refused: " + why);
            return Refused;
        }
        if (!StatPack.IsName(name))
        {
            return Refuse($"give the pack a name with --pack-name: 1 to {StatPack.MostName} characters of plain ASCII.");
        }
        var path = outPath ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(carDataPath)) ?? ".", FileNameOf(name!));
        if (!overwrite && (File.Exists(path) || Directory.Exists(path)))
        {
            return Refuse($"{path} is there already: name another file with --out, or give --overwrite.");
        }
        var (disc, changed, unread) = Read(discPath, carDataPath, anyDisc, pins);
        if (unread is not null)
        {
            return Refuse(unread);
        }
        List<StatEntry>? constants = null;
        List<string>? ignored = null;
        if (modulePath is not null)
        {
            (constants, ignored, var unreadModule) = ReadModule(discPath, modulePath, pins);
            if (unreadModule is not null)
            {
                return Refuse(unreadModule);
            }
        }
        var (pack, refusal, names) = Differences(disc!, changed!, name!, author, constants);
        if (pack is null)
        {
            return Refuse(refusal!);
        }
        if (StatPackConstants.BoostLengthProblem(pack.Constants, LongestBoost(changed)) is { } boost)
        {
            return Refuse(boost);
        }
        try
        {
            File.WriteAllText(path, StatPackText.Write(pack));
            // the file is the pack: what was written reads back to the same values
            if (StatPackText.Read(File.ReadAllText(path)).Pack?.Fingerprint != pack.Fingerprint)
            {
                output.WriteLine($"not written: {path} does not read back as the pack.");
                return NotWritten;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            output.WriteLine($"not written: {path}: {e.Message}");
            return NotWritten;
        }
        var machines = pack.Entries.Where(e => e.Block < StatPackRules.Machines).Select(e => e.Block).Distinct().Count();
        var parts = pack.Entries.Where(e => e.Block >= StatPackRules.Machines).Select(e => e.Block).Distinct().Count();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"stat pack {pack.Name}: {pack.Entries.Count} values differ from the disc's, in {machines} machines and {parts} custom parts"));
        if (modulePath is not null)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"the main module: {StatPackConstants.Described(pack.Constants).Count} of the game's code constants differ from the disc's"));
            foreach (var line in ignored!)
            {
                output.WriteLine("  " + line);
            }
        }
        if (names)
        {
            output.WriteLine("the file also changes names: a pack carries no name, and they are left out");
        }
        foreach (var line in pack.UntriedWarning())
        {
            output.WriteLine(line);
        }
        output.WriteLine("fingerprint: " + pack.Fingerprint);
        output.WriteLine("written: " + path);
        return Written;
    }

    /// <summary>The two files an import compares, both as unpacked car data: the disc image's and the changed
    /// file's; or the sentence that says why there is nothing to compare.</summary>
    private static (byte[]? Disc, byte[]? Changed, string? Refusal) Read(string? discPath, string carDataPath, bool anyDisc, DiscPins? pins)
    {
        byte[]? disc;
        var unchanged = true;
        using (var art = DiscArt.Open(discPath))
        {
            disc = art?.CarData();
            // The pack is the file's differences from this image: against an image with a changed car data file, or
            // another region's, the same file gives another pack, which every player's game would lay over the real
            // one. So the image is the pinned disc, every file of it (what the preparation holds a disc to).
            unchanged = anyDisc || art is null || art.IsUnchanged(pins ?? DiscPins.Game);
        }
        if (disc is null || CarDataOf(disc).Data is null)
        {
            return (null, null, $"{discPath ?? "no disc image"}: not the disc of F-Zero GX (USA), or its car data cannot be read. Name the image with --disc.");
        }
        if (!unchanged)
        {
            return (null, null, $"{discPath}: not an unchanged disc of F-Zero GX (USA): one of its files is modified or damaged, and a pack made " +
                                "against it would not be the pack players' games need. --any-disc takes the image as it is.");
        }
        byte[] file;
        try
        {
            if (new FileInfo(carDataPath) is not { Exists: true, Length: <= LargestCarData } info)
            {
                return (null, null, $"{carDataPath}: no such file, or longer than any car data.");
            }
            file = File.ReadAllBytes(info.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null, $"{carDataPath} cannot be read: {e.Message}");
        }
        var (changed, notCarData) = CarDataOf(file);
        return changed is null ? (null, null, $"{carDataPath} is not a car data file. {notCarData}") : (disc, changed, null);
    }

    /// <summary>The file a pack of this name is written to when none is named: the name's letters and digits, the
    /// rest as '-'.</summary>
    public static string FileNameOf(string name) =>
        new string([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' ? c : '-')]).Trim('.') is { Length: > 0 } plain
            ? plain + StatPackText.Extension
            : "pack" + StatPackText.Extension;
}
