using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fzgx.GameCube.Compression;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// Which background file (<c>bg/bg_ele.gma.lz</c>, <c>.tpl.lz</c>) each course loads, as the game picks it: the main
/// module (<c>main.rel</c>) keeps one venue byte a course (.data 0x2B0D4 + the course number) and a table of the venues'
/// names (.data 0x2AF78, venue n at entry n - 1), each a pointer to its "bg_..." string that the module's own
/// relocations fill in at boot (the file holds 0 there). Read from the player's disc at a mod's import, so no name is
/// written here; bytes that are not the game's give no table, and nothing here raises on them.
/// </summary>
public static class CourseBackgrounds
{
    /// <summary>The venue bytes (.data 0x2B0D4) and the names' table (.data 0x2AF78) in the unpacked module.</summary>
    public const int VenuesAt = (int)GameFacts.Rel.RelData + 0x2B0D4, NamesAt = (int)GameFacts.Rel.RelData + 0x2AF78;

    /// <summary>Courses 0 to 55 have a venue byte; venues are 1 to 20 (0: none).</summary>
    public const int Courses = 56, Venues = 20;

    // the REL header's words: the section table and the imports (each module's relocations)
    private const int SectionsCount = 0x0C, SectionsAt = 0x10, ImportsAt = 0x28, ImportsSize = 0x2C;
    // relocation types: a word's address (R_PPC_ADDR32), the next section's start, the list's end (a no-op, 201, only
    // moves the place)
    private const byte Addr32 = 1, Section = 202, End = 203;

    /// <summary>The background name of each course that has a venue ("bg_ele"), from the disc's main module when it is
    /// the one the pins name (<see cref="DiscPins.Game"/>); null when it is not, or cannot be read.</summary>
    /// <param name="gameFiles">The disc's <c>files/</c> folder.</param>
    public static IReadOnlyDictionary<int, string>? OfDisc(string gameFiles)
    {
        try
        {
            var path = Path.Combine(gameFiles, MachineStatFile.MainModule);
            if (!DiscPins.Game.Named.TryGetValue("files/" + MachineStatFile.MainModule, out var pin) || !File.Exists(path) ||
                new FileInfo(path).Length != pin.Size)
            {
                return null;
            }
            var file = File.ReadAllBytes(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), pin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var packed = MachineStatFile.Decrypted(file);
            return packed.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4)) is < 0 or > DiscArt.LargestUnpacked
                ? null : Read(AvLz.Decompress(packed));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
                                      or IndexOutOfRangeException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The background name of each course that has a venue, from the unpacked main module; null when the
    /// bytes are not such a module (a name that is not "bg_" and letters, digits or '_' is none).</summary>
    public static IReadOnlyDictionary<int, string>? Read(byte[] module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.Length < Math.Max(VenuesAt + Courses, NamesAt + (Venues * 4)) || Names(module) is not { } names)
        {
            return null;
        }
        var courses = new Dictionary<int, string>();
        for (var course = 0; course < Courses; course++)
        {
            var venue = module[VenuesAt + course];
            if (venue is >= 1 and <= Venues && names[venue - 1] is { } name)
            {
                courses[course] = name;
            }
        }
        return courses.Count > 0 ? courses : null;
    }

    /// <summary>The venues' names, by the module's own relocations into the names' table; null when the module's
    /// relocations cannot be read.</summary>
    private static string?[]? Names(byte[] module)
    {
        var id = BinaryPrimitives.ReadUInt32BigEndian(module);
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(SectionsCount));
        var sections = (int)BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(SectionsAt));
        var imports = (int)BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(ImportsAt));
        var importsSize = (int)BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(ImportsSize));
        if (count is <= 0 or > 64 || sections < 0 || (long)sections + (count * 8L) > module.Length ||
            imports < 0 || importsSize < 0 || (long)imports + importsSize > module.Length)
        {
            return null;
        }
        long SectionOf(int s) => BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(sections + (s * 8))) & ~1u;
        var data = Enumerable.Range(0, count).FirstOrDefault(s => SectionOf(s) == GameFacts.Rel.RelData, -1);
        if (data < 0)
        {
            return null;
        }
        var names = new string?[Venues];
        for (var at = imports; at + 8 <= imports + importsSize; at += 8)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(at)) != id)
            {
                continue;
            }
            // the module's relocations of itself: an offset from the last place, a type, a section, an addend
            var r = (int)BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(at + 4));
            var (inSection, place) = (-1, 0L);
            for (; r >= 0 && r + 8 <= module.Length; r += 8)
            {
                var (step, type, section) = (BinaryPrimitives.ReadUInt16BigEndian(module.AsSpan(r)), module[r + 2], module[r + 3]);
                var addend = BinaryPrimitives.ReadUInt32BigEndian(module.AsSpan(r + 4));
                if (type == End)
                {
                    break;
                }
                if (type == Section)
                {
                    (inSection, place) = (section, 0);
                    continue;
                }
                place += step;
                var from = place + SectionOf(data) - NamesAt;
                if (type == Addr32 && inSection == data && from >= 0 && from < Venues * 4 && from % 4 == 0 && section < count)
                {
                    names[from / 4] = NameAt(module, SectionOf(section) + addend);
                }
            }
        }
        return names.Any(n => n is not null) ? names : null;
    }

    /// <summary>A background's name at a place of the module: "bg_" and then letters, digits or '_', ended by 0.</summary>
    private static string? NameAt(byte[] module, long at)
    {
        if (at < 0 || at >= module.Length)
        {
            return null;
        }
        var rest = module.AsSpan((int)at);
        var end = rest.IndexOf((byte)0);
        if (end is < 4 or > 32)
        {
            return null;
        }
        var name = Encoding.ASCII.GetString(rest[..end]);
        return name.StartsWith("bg_", StringComparison.Ordinal) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name : null;
    }
}
