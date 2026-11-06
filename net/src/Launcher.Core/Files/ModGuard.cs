using System.Buffers.Binary;
using System.Collections.Immutable;
using Fzgx.Coli;
using Fzgx.GameCube.Compression;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>What a mod's file is to the game, by its path on the disc (<see cref="ModGuard.Classify"/>).</summary>
public enum ModFileClass
{
    /// <summary>Anything that is neither of the others: textures, models, backgrounds, music, menus, movies. Free,
    /// but for a course's own models, which keep the disc's shape for the race (<see cref="ModModels"/>), and the
    /// looks of a course that is single player only (<see cref="ModManifest.Files"/>).</summary>
    Look,

    /// <summary>The game's code (<c>sys/*</c>, <c>main.dol</c>, <c>*.rel</c>, <c>enemy_line/*</c>, the pinned files):
    /// never used, online or offline. Piconet runs its own compiled game.</summary>
    Code,

    /// <summary><c>game/cardata.lz</c>: the machines' numbers, compared field by field with the disc's.</summary>
    CarData,

    /// <summary><c>stage/COLI_COURSE*.lz</c>: a course, compared field by field with the disc's.</summary>
    Course,

    /// <summary><c>staff_ghost/*</c>, <c>story_ghost/*</c>: the game's ghosts, offline only.</summary>
    Ghost,
}

/// <summary>What the guard says of one file of a mod: whether a start other than offline play may use it, and why
/// not, in a few words (null: it may).</summary>
public sealed record ModVerdict(bool Online, string? Why)
{
    public static ModVerdict Anywhere { get; } = new(true, null);
}

/// <summary>
/// The guard a mod's files pass at import: which files are code (never used), which may be used in a race and which
/// in offline play only. A race is the same in every game of it only while what the race reads is the disc's: so the
/// car data and a course file are compared with the disc's own, field by field, and pass for the race only when every
/// field the race reads is the disc's. Where the class of a field is not settled, it counts as one the race reads.
/// </summary>
public static class ModGuard
{
    /// <summary>The words of a car data block the race does not read: the per-machine camera (CameraReorienting at
    /// 0x4C, CameraRepositioning at 0x50; protocol handbook stat-packs.md). Every other word of every block, the
    /// points of the machine's hover and wall collision included, is the race's.</summary>
    public static IReadOnlySet<int> CarLookWords { get; } = new HashSet<int> { 0x4C, 0x50 };

    /// <summary>Why a code file is never used.</summary>
    public const string CodeWhy = "Code changes are not used: Piconet runs its own compiled game";

    /// <summary>The class of a file by its path under the disc's <c>files/</c> (forward slashes, any case); a path
    /// that starts with <c>sys/</c> is the system area's.</summary>
    public static ModFileClass Classify(string discPath)
    {
        ArgumentNullException.ThrowIfNull(discPath);
        var path = discPath.Replace('\\', '/').TrimStart('/');
        var lower = path.ToLowerInvariant();
        if (lower.StartsWith("sys/", StringComparison.Ordinal) || lower is "main.dol" || lower.EndsWith(".dol", StringComparison.Ordinal) ||
            lower.EndsWith(".rel", StringComparison.Ordinal) || lower.StartsWith("enemy_line/", StringComparison.Ordinal) ||
            DiscPins.Game.Named.Keys.Any(k => string.Equals(k, "files/" + path, StringComparison.OrdinalIgnoreCase)))
        {
            return ModFileClass.Code;
        }
        if (lower is "game/cardata.lz")
        {
            return ModFileClass.CarData;
        }
        if (lower.StartsWith("stage/coli_course", StringComparison.Ordinal) && lower.EndsWith(".lz", StringComparison.Ordinal))
        {
            return ModFileClass.Course;
        }
        return lower.StartsWith("staff_ghost/", StringComparison.Ordinal) || lower.StartsWith("story_ghost/", StringComparison.Ordinal)
            ? ModFileClass.Ghost
            : ModFileClass.Look;
    }

    /// <summary>The guard's word on one file: the mod's bytes against the disc's (null: the disc has no such file).</summary>
    public static ModVerdict Check(ModFileClass kind, byte[] mod, byte[]? disc) => kind switch
    {
        ModFileClass.Code => new ModVerdict(false, CodeWhy),
        ModFileClass.Ghost => new ModVerdict(false, "Ghosts: single player only"),
        ModFileClass.CarData => CarData(mod, disc),
        ModFileClass.Course => Course(mod, disc),
        _ => ModVerdict.Anywhere,
    };

    /// <summary>The car data: every word of the 116 blocks but <see cref="CarLookWords"/> must be the disc's. Names
    /// (the text after the blocks) may differ: the race reads none.</summary>
    public static ModVerdict CarData(byte[] mod, byte[]? disc)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (disc is null || StatPackImport.CarDataOf(disc).Data is not { } d)
        {
            return new ModVerdict(false, "Car data: the disc's cannot be read");
        }
        if (StatPackImport.CarDataOf(mod).Data is not { } m)
        {
            return new ModVerdict(false, "Car data: not readable");
        }
        var machines = new SortedSet<int>();
        var parts = 0;
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            var at0 = StatPackRules.BlockAt(block);
            for (var at = 4; at < StatPackRules.BlockSize; at += 4)
            {
                if (CarLookWords.Contains(at) || m.AsSpan(at0 + at, 4).SequenceEqual(d.AsSpan(at0 + at, 4)))
                {
                    continue;
                }
                if (block < StatPackRules.Machines)
                {
                    machines.Add(block);
                }
                else
                {
                    parts++;
                }
            }
        }
        if (machines.Count == 0 && parts == 0)
        {
            return ModVerdict.Anywhere;
        }
        return new ModVerdict(false, machines.Count > 0
            ? $"Machine stats changed ({machines.Count} machines): single player only"
            : "Custom part stats changed: single player only");
    }

    /// <summary>A course file: every byte but the fog's must be the disc's, the file the same length. The fog is the
    /// header's 0x84 (type, near, far, colour) and the keys its curves at 0x80 point at (the curves' table itself,
    /// their counts and places, must be the disc's). The readers then say which part differs, for the reason.</summary>
    public static ModVerdict Course(byte[] mod, byte[]? disc)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (disc is null || Unpacked(disc) is not { } d)
        {
            return new ModVerdict(false, "Course file: the disc's cannot be read");
        }
        if (Unpacked(mod) is not { } m)
        {
            return new ModVerdict(false, "Course file: not readable");
        }
        try
        {
            ColiSceneReader.Read(d);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return new ModVerdict(false, "Course file: the disc's cannot be read");
        }
        if (m.Length == d.Length && OnlyFogDiffers(m, d))
        {
            return ModVerdict.Anywhere;
        }
        return new ModVerdict(false, "Course changed: " + WhatDiffers(m, d) + ": single player only");
    }

    /// <summary>Every differing byte lies in the disc's fog (see <see cref="Course"/>).</summary>
    internal static bool OnlyFogDiffers(byte[] mod, byte[] disc)
    {
        var free = FogRanges(disc);
        for (var i = 0; i < disc.Length; i++)
        {
            if (mod[i] != disc[i] && !free.Exists(r => i >= r.Start && i < r.End))
            {
                return false;
            }
        }
        return true;
    }

    private const int FogCurvesAt = 0x80, FogAt = 0x84, FogSize = 0x24, FogCurves = 6, FogKeySize = 0x14, MostFogKeys = 4096;

    /// <summary>Where the fog's values are in a course file: the fog itself, and each fog curve's keys.</summary>
    internal static List<(int Start, int End)> FogRanges(byte[] data)
    {
        var ranges = new List<(int, int)>();
        if (data.Length < 0xE8)
        {
            return ranges;
        }
        var fog = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(FogAt));
        if (fog > 0 && (long)fog + FogSize <= data.Length)
        {
            ranges.Add((fog, fog + FogSize));
        }
        var curves = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(FogCurvesAt));
        if (curves > 0 && (long)curves + (FogCurves * 8) <= data.Length)
        {
            for (var c = 0; c < FogCurves; c++)
            {
                var count = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(curves + (c * 8)));
                var keys = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(curves + (c * 8) + 4));
                if (count is > 0 and <= MostFogKeys && keys > 0 && (long)keys + ((long)count * FogKeySize) <= data.Length)
                {
                    ranges.Add((keys, keys + (count * FogKeySize)));
                }
            }
        }
        return ranges;
    }

    /// <summary>Which part of a course differs, by the stage editor's readers: the track (nodes, checkpoints,
    /// segments), the placed objects, else the rest of the file (collision, the computer drivers' areas, triggers:
    /// parts the readers do not take apart, which count as the race's).</summary>
    internal static string WhatDiffers(byte[] mod, byte[] disc)
    {
        try
        {
            var (ms, ds) = (ColiSceneReader.Read(mod), ColiSceneReader.Read(disc));
            var (mt, dt) = (ColiTrackReader.Read(mod), ColiTrackReader.Read(disc));
            if (!SameTrack(mt, dt) || ms.TrackLength != ds.TrackLength || ms.Circuit != ds.Circuit)
            {
                return "track shape";
            }
            if (!SameObjects(ms.Objects, ds.Objects) || ms.TrackMinHeight != ds.TrackMinHeight)
            {
                return "objects";
            }
            return "collision or other course data";
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return "not readable";
        }
    }

    private static bool SameObjects(ImmutableArray<ColiSceneObject> a, ImmutableArray<ColiSceneObject> b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First == p.Second);

    private static bool SameTrack(ColiTrack a, ColiTrack b) =>
        a.Roots.Length == b.Roots.Length && a.Nodes.Length == b.Nodes.Length &&
        a.Roots.Zip(b.Roots).All(p => SameSegment(p.First, p.Second)) &&
        a.Nodes.Zip(b.Nodes).All(p => p.First.Root == p.Second.Root && p.First.Checkpoints.SequenceEqual(p.Second.Checkpoints));

    private static bool SameSegment(ColiTrackSegment a, ColiTrackSegment b) =>
        a.Kind == b.Kind && a.Property == b.Property && a.Perimeter == b.Perimeter && a.Pipe == b.Pipe &&
        a.FallbackScale == b.FallbackScale && a.FallbackRotation == b.FallbackRotation && a.FallbackPosition == b.FallbackPosition &&
        a.Unknown38 == b.Unknown38 && a.Unknown3A == b.Unknown3A && a.RailLeft.Equals(b.RailLeft) && a.RailRight.Equals(b.RailRight) &&
        a.BranchIndex == b.BranchIndex && a.HasCorner == b.HasCorner &&
        a.Curves.Zip(b.Curves).All(c => c.First.Keys.SequenceEqual(c.Second.Keys)) &&
        a.Children.Length == b.Children.Length && a.Children.Zip(b.Children).All(c => SameSegment(c.First, c.Second));

    /// <summary>The largest file a mod's packed file may unpack to here (a course file is under 2 MiB).</summary>
    public const int MostUnpacked = 16 << 20;

    /// <summary>A packed file's bytes unpacked (the disc's <c>.lz</c>: two words, low byte first, the packed length
    /// and the unpacked one); the bytes themselves when they are not packed so; null when they do not unpack.</summary>
    public static byte[]? Unpacked(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(file) != (uint)(file.Length - 8))
        {
            return file;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)) > MostUnpacked)
        {
            return null;
        }
        try
        {
            return AvLz.Decompress(file);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException
                                      or ArithmeticException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Two files are the same game file: the same bytes, or the same once unpacked.</summary>
    public static bool Same(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.AsSpan().SequenceEqual(b))
        {
            return true;
        }
        return Unpacked(a) is { } ua && !ReferenceEquals(ua, a) && Unpacked(b) is { } ub && !ReferenceEquals(ub, b) && ua.AsSpan().SequenceEqual(ub);
    }
}
