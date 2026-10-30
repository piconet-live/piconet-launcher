using System.Buffers.Binary;
using System.Text;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// What the launcher reads of the custom parts from the player's disc, from bytes that may be anything (nothing
/// here raises on them): the naming tables from the main module and
/// each part's stats from the car data file, summed into a machine's as the game's <c>fn_1_14F6F8</c> does.
/// <para>The places in the main module are those of the pinned disc's file once decrypted and unpacked (its read
/// is gated as the letters' are: <see cref="DiscArt"/>): .rodata from 0x15F710, .data from 0x16D600.</para>
/// </summary>
public static class CustomPartFile
{
    // main.rel's sections in its file (the facts table, port/facts/game.tsv: REL_RODATA, REL_DATA)
    private const int Rodata = (int)GameFacts.Rel.RelRodata;
    private const int Data = (int)GameFacts.Rel.RelData;

    /// <summary>75 words, one a part: a booster's naming flags in the low five bits (.rodata 0x99D8).</summary>
    public const int FlagsAt = Rodata + 0x99D8;

    /// <summary>The named combinations' keys, 77 rising words (.rodata 0xCCDC).</summary>
    public const int ListedKeysAt = Rodata + 0xCCDC;

    /// <summary>The parts' strings, an English then a Japanese one per part, each from a word's start (.data 0x452A0).</summary>
    public const int PartsAt = Data + 0x452A0;

    /// <summary>The named combinations' names, an English then a Japanese one each, with one empty string after the
    /// first English one (.data 0x43E78).</summary>
    public const int ListedAt = Data + 0x43E78;

    public const int Listed = 77;

    /// <summary>The parts' rows (.rodata 0x9CDC; read in <c>fn_1_14E9E4</c> and <c>fn_1_1373B0</c>): 75
    /// rows of 16 bytes by part id, high byte first; +8 is the part's grade, 0 to 9, which the game turns into a
    /// letter as grade / 2 + 1 (1 A to 5 E; the disc's are all odd: 1, 3, 5, 7, 9).</summary>
    public const int GradesAt = Rodata + 0x9CDC;

    private const int GradeRow = 16;

    /// <summary>The weight factor of the boost's sum (.rodata 0xCCC4 to 0xCCD8): the names' difference's factor,
    /// the weight at which the factor is 1, its slopes above and below it.</summary>
    private static readonly float NameFactor = BitConverter.Int32BitsToSingle(0x3E000000);
    private static readonly float PivotWeight = BitConverter.Int32BitsToSingle(0x44960000);
    private static readonly float SlopeAbove = BitConverter.Int32BitsToSingle(0x39690453);
    private static readonly float SlopeBelow = BitConverter.Int32BitsToSingle(0x3960B1E3);

    /// <summary>The turn values' share of the names' difference (.rodata 0xCCD8).</summary>
    private static readonly float TurnShare = BitConverter.Int32BitsToSingle(0x3E99999A);

    /// <summary>Where each kind's 25 blocks start in the unpacked car data.</summary>
    private static readonly int[] KindAt =
    [
        (int)GameFacts.Files.CarPartsAt,
        (int)GameFacts.Files.CarPartsAt + (CustomPartTables.PerKind * MachineStatFile.BlockSize),
        (int)GameFacts.Files.CarPartsAt + (2 * CustomPartTables.PerKind * MachineStatFile.BlockSize),
    ];

    /// <summary>A part's name word points into the car data, which the game loads at this address.</summary>
    private const uint CarDataBase = 0x23800000;

    private const int LongestString = 64;

    /// <summary>The naming tables from the unpacked main module; null when the bytes are not them (too short, a
    /// string without its end, keys out of order, a body or cockpit without four words).</summary>
    public static CustomPartTables? Tables(ReadOnlySpan<byte> mainModule)
    {
        if (mainModule.Length < Math.Max(FlagsAt + (CustomMachine.Parts * 4), ListedKeysAt + (Listed * 4)))
        {
            return null;
        }
        var flags = new uint[CustomMachine.Parts];
        for (var i = 0; i < flags.Length; i++)
        {
            flags[i] = BinaryPrimitives.ReadUInt32BigEndian(mainModule[(FlagsAt + (i * 4))..]);
        }
        var keys = new int[Listed];
        for (var i = 0; i < Listed; i++)
        {
            keys[i] = BinaryPrimitives.ReadInt32BigEndian(mainModule[(ListedKeysAt + (i * 4))..]);
            if (i > 0 && keys[i] <= keys[i - 1])
            {
                return null;
            }
        }
        var parts = new string[CustomMachine.Parts];
        var at = PartsAt;
        for (var i = 0; i < parts.Length; i++)
        {
            if (Next(mainModule, ref at) is not { } english || Next(mainModule, ref at) is null
                || (i < CustomMachine.Boosters && english.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 4))
            {
                return null;
            }
            parts[i] = english;
        }
        var names = new string[Listed];
        at = ListedAt;
        for (var i = 0; i < Listed; i++)
        {
            if (Next(mainModule, ref at) is not { Length: > 0 } english || (i == 0 && Next(mainModule, ref at) is not { Length: 0 })
                || Next(mainModule, ref at) is null)
            {
                return null;
            }
            names[i] = english;
        }
        return new CustomPartTables(parts, flags, keys, names);
    }

    /// <summary>Each part's letter, by part id, 1 (A) to 5 (E), as the game shows a parts machine's letters: the body
    /// letter is its body's, the boost letter its booster's, the grip letter its cockpit's (<see cref="Rate"/>). Null
    /// when the bytes are not the table (too short, a row's id or kind not its own, a grade outside 0 to 9).</summary>
    public static int[]? Grades(ReadOnlySpan<byte> mainModule)
    {
        if (mainModule.Length < GradesAt + (CustomMachine.Parts * GradeRow))
        {
            return null;
        }
        var letters = new int[CustomMachine.Parts];
        for (var part = 0; part < letters.Length; part++)
        {
            var row = mainModule.Slice(GradesAt + (part * GradeRow), GradeRow);
            var grade = BinaryPrimitives.ReadInt16BigEndian(row[8..]);
            if (BinaryPrimitives.ReadInt16BigEndian(row) != part || BinaryPrimitives.ReadInt16BigEndian(row[2..]) != (int)CustomPartTables.KindOf(part)!
                || grade is < 0 or > 9)
            {
                return null;
            }
            letters[part] = (grade / 2) + 1;
        }
        return letters;
    }

    /// <summary>A parts machine's three letters put into its summed row (<see cref="Sum"/>), as the game's
    /// <c>fn_1_137288</c> gives them for a garage machine: body from the body, boost from the booster, grip from the
    /// cockpit. Nothing changes without the letters or with an id out of range.</summary>
    public static void Rate(float?[]? row, IReadOnlyList<int>? letters, int body, int cockpit, int booster)
    {
        if (row is null || letters is not { Count: CustomMachine.Parts }
            || new[] { body, cockpit, booster }.Any(p => p < 0 || p >= CustomMachine.Parts))
        {
            return;
        }
        row[(int)MachineStat.BodyRating] = letters[body];
        row[(int)MachineStat.BoostRating] = letters[booster];
        row[(int)MachineStat.GripRating] = letters[cockpit];
    }

    /// <summary>The car data with a stat pack laid over it, as the game lays one over its loaded table
    /// (the protocol's handbook/stat-packs.md): every value of the pack at its block (0 to 40 the machines, 41 to 115 the parts, which
    /// follow each other in the file) and offset. The file's words are low byte first, but for the four bytes at 0x48,
    /// which the game's loader leaves as they stand. A custom machine summed from this table is the one a race of the
    /// pack sums (the game's <c>CustomSumRefusal</c> reads the parts so). A value outside the file changes nothing.</summary>
    public static byte[] WithPack(ReadOnlySpan<byte> carData, StatPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var bytes = carData.ToArray();
        foreach (var entry in pack.Entries)
        {
            var at = (entry.Block < Machines.Names.Count ? entry.Block * MachineStatFile.BlockSize
                : KindAt[0] + ((entry.Block - Machines.Names.Count) * MachineStatFile.BlockSize)) + entry.At;
            if (entry.Block < 0 || entry.At < 0 || at < 0 || at + 4 > bytes.Length)
            {
                continue;
            }
            if (entry.At == 0x48)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), entry.Bits);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), entry.Bits);
            }
        }
        return bytes;
    }

    /// <summary>The string at <paramref name="at"/> (Latin-1; Japanese ones are only stepped over), moving past it to
    /// the next word's start; null without its end within <see cref="LongestString"/> bytes.</summary>
    private static string? Next(ReadOnlySpan<byte> bytes, ref int at)
    {
        if (at < 0 || at >= bytes.Length)
        {
            return null;
        }
        var end = bytes[at..Math.Min(bytes.Length, at + LongestString)].IndexOf((byte)0);
        if (end < 0)
        {
            return null;
        }
        var text = Encoding.Latin1.GetString(bytes.Slice(at, end));
        at = (at + end + 1 + 3) & ~3;
        return text;
    }

    /// <summary>The machine's block of these three parts as the game sums it, by <see cref="MachineStat"/> (the
    /// numbers only; the letters are left unknown); null when an id is not of its kind or the car data is not the
    /// game's table (too short, or a value that is no number).</summary>
    public static float?[]? Sum(ReadOnlySpan<byte> carData, int body, int cockpit, int booster)
    {
        if (CustomPartTables.KindOf(body) != PartKind.Body || CustomPartTables.KindOf(cockpit) != PartKind.Cockpit
            || CustomPartTables.KindOf(booster) != PartKind.Booster || carData.Length < KindAt[2] + (CustomPartTables.PerKind * MachineStatFile.BlockSize))
        {
            return null;
        }
        var b = Block(carData, body).ToArray();
        var c = Block(carData, cockpit).ToArray();
        var s = Block(carData, booster).ToArray();
        static float F(byte[] block, int at) => BinaryPrimitives.ReadSingleLittleEndian(block.AsSpan(at));
        float Sum3(int at) => F(b, at) + F(c, at) + F(s, at);   // (body + cockpit) + booster, as the game adds
        var d = NameDifference(carData, b, c) * NameFactor;
        var weight = Sum3(0x04);
        var factor = ((weight - PivotWeight) * (weight - PivotWeight > 0 ? SlopeAbove : SlopeBelow)) + 1f;
        var tension = Sum3(0x18);
        var movement = Sum3(0x20);
        var row = new float?[MachineStats.All.Count];
        void Set(MachineStat stat, float value) => row[(int)stat] = value;
        Set(MachineStat.Weight, weight);
        Set(MachineStat.Acceleration, Sum3(0x08));
        Set(MachineStat.MaxSpeed, Sum3(0x0C));
        Set(MachineStat.Grip1, F(b, 0x10) + (F(b, 0x10) * d));
        Set(MachineStat.Grip3, F(b, 0x14) + (F(b, 0x14) * d));
        Set(MachineStat.TurnTension, tension + (TurnShare * tension * d));
        Set(MachineStat.DriftAcceleration, Sum3(0x1C));
        Set(MachineStat.TurnMovement, movement - (TurnShare * movement * d));
        Set(MachineStat.StrafeTurn, Sum3(0x24));
        Set(MachineStat.Strafe, Sum3(0x28));
        Set(MachineStat.TurnReaction, Sum3(0x2C));
        Set(MachineStat.Grip2, Sum3(0x30));
        Set(MachineStat.BoostStrength, factor * Sum3(0x34));
        Set(MachineStat.BoostDuration, Sum3(0x38));
        Set(MachineStat.TurnDeceleration, Sum3(0x3C));
        Set(MachineStat.Drag, Sum3(0x40));
        Set(MachineStat.Body, F(b, 0x44) * F(c, 0x44));
        Set(MachineStat.CameraReorienting, F(c, 0x4C));
        Set(MachineStat.CameraRepositioning, F(c, 0x50));
        return row.All(v => v is null || float.IsFinite(v.Value)) ? row : null;
    }

    private static ReadOnlySpan<byte> Block(ReadOnlySpan<byte> carData, int part)
    {
        var kind = (int)CustomPartTables.KindOf(part)!;
        return carData.Slice(KindAt[kind] + ((part - CustomPartTables.First((PartKind)kind)) * MachineStatFile.BlockSize),
            MachineStatFile.BlockSize);
    }

    /// <summary>The fourth byte of the body's name less the cockpit's (each part's first word points at its name in
    /// the car data). The disc's parts all point at one string, so it is 0; a word that points nowhere in the file
    /// counts as 0 too.</summary>
    private static int NameDifference(ReadOnlySpan<byte> carData, ReadOnlySpan<byte> body, ReadOnlySpan<byte> cockpit)
    {
        return Byte(carData, body) - Byte(carData, cockpit);
    }

    private static int Byte(ReadOnlySpan<byte> carData, ReadOnlySpan<byte> block)
    {
        var at = (long)BinaryPrimitives.ReadUInt32LittleEndian(block) - CarDataBase + 3;
        return at >= 0 && at < carData.Length ? carData[(int)at] : 0;
    }
}
