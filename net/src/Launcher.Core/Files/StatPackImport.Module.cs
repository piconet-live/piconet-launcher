using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Fzgx.GameCube.Compression;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A stat pack's code constants made from a modified main module (<c>enemy_line/line__.bin</c>; the
/// protocol's handbook/stat-packs.md, "Constants"): the file decrypted and unpacked as <see cref="MachineStatFile"/> does, compared with
/// the player's own disc's, and only the allowed words kept (<see cref="StatPackConstants"/>). One code change is
/// known and carried as data: the dash plate's formula rewritten as int(A + B x D), as GX Unleashed does, becomes
/// the constants DashPlateBase and DashPlateScale; so are the machine setting's numbers and the energy boost's length,
/// where a mod points their loads at other words (StatPackImport.Curve.cs). Every other difference is named by where
/// it is, and left out.
/// </summary>
public static partial class StatPackImport
{
    /// <summary>The largest main module file read: the game's is 2.2 MB unpacked.</summary>
    public const int LargestModule = 8 << 20;

    private const int EnergyAt = (int)GameFacts.Files.ModuleBoostEnergyUse;
    private const int SpeedCapAt = (int)GameFacts.Files.ModuleSpeedCap;
    private const int DashPlateAt = (int)GameFacts.Files.ModuleDashPlate;
    private const int DashPoolAt = (int)GameFacts.Files.ModuleDashPool;

    // the dash plate's three words (MODULE_DASH_PLATE +0, +8, +0x1C) as the game has them, and the shape GX
    // Unleashed gives them: lfs f1, B(r31); lfs f2, A(r31); fadd f0, f2, f0
    private const uint LoadF1 = 0xC03F0000, LoadF2 = 0xC05F0000, StockMultiply = 0xEC020032, Add = 0xFC02002A;
    private static readonly int[] DashWords = [0, 8, 0x1C];

    // the first word of main.rel's header: its module number (the facts' MAIN_REL_ID)
    private const uint MainModuleId = 1;

    /// <summary>The constants' words in the module's file: slot and offset (the race block's have none).</summary>
    private static readonly (int Slot, int At)[] InModule = [(0, EnergyAt), (1, SpeedCapAt), (2, SpeedCapAt + 4)];

    /// <summary>A main module file decrypted and unpacked; or why it is none: too large, does not unpack, or is not
    /// the game's main module by its header (module 1) and size.</summary>
    public static (byte[]? Module, string? Refusal) ModuleOf(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var packed = MachineStatFile.Decrypted(file);
        if (packed.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(packed) != (uint)(packed.Length - 8)
            || BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4)) is < 0 or > LargestModule)
        {
            return (null, "It is not the game's main module (enemy_line/line__.bin): it does not decrypt to a packed file.");
        }
        byte[] module;
        try
        {
            module = AvLz.Decompress(packed);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException
                                      or ArithmeticException or InvalidOperationException or NotSupportedException)
        {
            return (null, "It decrypts to a packed file that does not unpack.");
        }
        return module.Length < DashPoolAt + 0x1000 || BinaryPrimitives.ReadUInt32BigEndian(module) != MainModuleId
            ? (null, "It unpacks to something that is not the game's main module.")
            : (module, null);
    }

    /// <summary>The constants a modified module holds where it differs from the disc's (both unpacked), and a
    /// sentence for every other kind of difference, which no pack carries.</summary>
    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",
        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    public static (List<StatEntry> Constants, List<string> Ignored) ModuleDifferences(ReadOnlySpan<byte> disc, ReadOnlySpan<byte> changed)
    {
        var constants = new List<StatEntry>();
        var ignored = new List<string>();
        if (disc.Length != changed.Length)
        {
            ignored.Add(string.Create(CultureInfo.InvariantCulture,
                $"the module is {changed.Length:N0} bytes and the disc's {disc.Length:N0}: only the allowed words were compared"));
        }
        var length = Math.Min(disc.Length, changed.Length) & ~3;
        uint Word(ReadOnlySpan<byte> m, int at) => BinaryPrimitives.ReadUInt32BigEndian(m[at..]);
        var taken = new HashSet<int>();
        foreach (var (slot, at) in InModule)
        {
            taken.Add(at);
            if (StatPackConstants.All[slot].Kind == StatConstantKind.Number64Low)
            {
                continue;
            }
            var pair = StatPackConstants.All[slot].Kind == StatConstantKind.Number64High;
            if (Word(changed, at) != Word(disc, at) || (pair && Word(changed, at + 4) != Word(disc, at + 4)))
            {
                constants.Add(new StatEntry(StatPackConstants.Block, slot, Word(changed, at)));
                if (pair)
                {
                    constants.Add(new StatEntry(StatPackConstants.Block, slot + 1, Word(changed, at + 4)));
                }
            }
        }
        // the dash plate's formula: carried when it is the shape GX Unleashed gives it, and named otherwise
        var dash = new uint[DashWords.Length];
        var stock = new uint[DashWords.Length];
        for (var i = 0; i < DashWords.Length; i++)
        {
            (dash[i], stock[i]) = (Word(changed, DashPlateAt + DashWords[i]), Word(disc, DashPlateAt + DashWords[i]));
        }
        if (!stock.SequenceEqual(dash))
        {
            // (each load's constant inside the module: a file cut short names one past its end)
            if ((dash[0] & 0xFFFF0000) == LoadF1 && (dash[1] & 0xFFFF0000) == LoadF2 && dash[2] == Add
                && (dash[0] & 0xFFFF) < 0x8000 && (dash[1] & 0xFFFF) < 0x8000
                && DashPoolAt + (int)(dash[0] & 0xFFFF) + 4 <= changed.Length
                && DashPoolAt + (int)(dash[1] & 0xFFFF) + 4 <= changed.Length)
            {
                var scale = Word(changed, DashPoolAt + (int)(dash[0] & 0xFFFF));
                var bases = Word(changed, DashPoolAt + (int)(dash[1] & 0xFFFF));
                constants.Add(new StatEntry(StatPackConstants.Block, 3, bases));
                constants.Add(new StatEntry(StatPackConstants.Block, 4, scale));
                ignored.Add(string.Create(CultureInfo.InvariantCulture,
                    $"its dash plate's code is int(A + B x boost duration), A = {BitConverter.UInt32BitsToSingle(bases)}, B = {BitConverter.UInt32BitsToSingle(scale)}: carried as DashPlateBase and DashPlateScale"));
            }
            else
            {
                ignored.Add("its dash plate's code is changed in a way no constant carries: left out");
            }
            foreach (var k in DashWords)
            {
                taken.Add(DashPlateAt + k);
            }
        }
        // the machine setting's curve and the energy boost's length
        CurveDifferences(disc, changed, constants, ignored, taken);
        constants.Sort((a, b) => a.At.CompareTo(b.At));
        // every other word, by where it is
        var sections = Sections(disc);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var code = new List<int>();   // a changed word of code by its place in the code section (a .text+ offset)
        for (var at = 0; at < length; at += 4)
        {
            if (taken.Contains(at) || Word(changed, at) == Word(disc, at))
            {
                continue;
            }
            var section = sections.FirstOrDefault(s => at >= s.At && at < s.At + s.Size);
            var where = section.Name ?? "header and relocations";
            counts[where] = counts.GetValueOrDefault(where) + 1;
            if (where == "code")
            {
                code.Add(at - section.At);
            }
        }
        foreach (var (where, count) in counts)
        {
            // a change to the code is a change to how the race plays, which the pack does not carry: said where
            var places = where != "code" ? "" : string.Create(CultureInfo.InvariantCulture,
                $" (the race runs the disc's code there: {string.Join(", ", code.Take(8).Select(at => $".text+0x{at:X}"))}{(code.Count > 8 ? ", ..." : "")})");
            ignored.Add(string.Create(CultureInfo.InvariantCulture,
                $"{count:N0} other word{(count == 1 ? "" : "s")} of its {where}: not on the list of allowed words, left out{places}"));
        }
        return (constants, ignored);
    }

    /// <summary>The module's sections by its header: the code ones named ".text", the others by their number.</summary>
    private static List<(string Name, int At, int Size)> Sections(ReadOnlySpan<byte> module)
    {
        var list = new List<(string, int, int)>();
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(module[0x0C..]);
        var table = (int)BinaryPrimitives.ReadUInt32BigEndian(module[0x10..]);
        for (var i = 0; i < count && i < 64 && table + (i * 8) + 8 <= module.Length; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(module[(table + (i * 8))..]);
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(module[(table + (i * 8) + 4)..]);
            var at = (int)(offset & ~1u);
            if (at != 0 && size > 0)
            {
                list.Add(((offset & 1) != 0 ? "code" : i == 4 ? "constants (.rodata)" : i == 5 ? "data (.data)" : $"section {i}", at, size));
            }
        }
        return list;
    }

    /// <summary>The modified module at <paramref name="path"/> against the disc's: its constants and what was left
    /// out; or why it cannot be compared.</summary>
    private static (List<StatEntry>? Constants, List<string>? Ignored, string? Refusal) ReadModule(string? discPath, string path, DiscPins? pins)
    {
        byte[]? disc;
        using (var art = DiscArt.Open(discPath))
        {
            disc = art?.MainModuleOf(pins ?? DiscPins.Game);
        }
        if (disc is null)
        {
            return (null, null, $"{discPath ?? "no disc image"}: its main module is not the one of F-Zero GX (USA) the launcher knows.");
        }
        byte[] file;
        try
        {
            if (new FileInfo(path) is not { Exists: true, Length: <= LargestModule } info)
            {
                return (null, null, $"{path}: no such file, or longer than any main module.");
            }
            file = File.ReadAllBytes(info.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null, $"{path} cannot be read: {e.Message}");
        }
        var (module, refusal) = ModuleOf(file);
        if (module is null)
        {
            return (null, null, $"{path}: {refusal}");
        }
        var (constants, ignored) = ModuleDifferences(disc, module);
        return (constants, ignored, null);
    }
}
