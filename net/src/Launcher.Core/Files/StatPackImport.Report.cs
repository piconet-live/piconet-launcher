using System.Globalization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>The import's report (<c>--import-stat-pack FILE --report</c>; the protocol's handbook/stat-packs.md): what a
/// changed car data file holds, seen whole, before a pack is made of it. The import itself stops at the first value
/// it refuses; this lists every one.</summary>
public static partial class StatPackImport
{
    /// <summary>The report of the file at <paramref name="carDataPath"/> against the disc image's car data. Nothing
    /// is written. The result is the exit status: <see cref="Written"/> when a pack of the file would be taken,
    /// <see cref="Refused"/> when a file cannot be compared or a value is refused.</summary>
    public static int Report(string? discPath, string carDataPath, TextWriter output, bool anyDisc = false, DiscPins? pins = null)
    {
        ArgumentNullException.ThrowIfNull(carDataPath);
        ArgumentNullException.ThrowIfNull(output);
        var (disc, changed, unread) = Read(discPath, carDataPath, anyDisc, pins);
        if (unread is not null)
        {
            output.WriteLine("refused: " + unread);
            return Refused;
        }
        return Report(disc, changed, output);
    }

    /// <summary>Every word of <paramref name="changed"/> that differs from the disc's (both unpacked car data), a
    /// line each: the block (a machine by id and name, a part by kind and number), the stat, the disc's value, the
    /// file's, and what the game's rows say of it (<see cref="StatPackRules.Problem"/>): in range; in range and
    /// outside what has been tried (<see cref="StatPackRules.Caution"/>, a warning); or refused, and by which rule.
    /// Then the totals, and the refused and the untried counted by kind of block and stat.</summary>
    public static int Report(ReadOnlySpan<byte> disc, ReadOnlySpan<byte> changed, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (inRange, machines, parts) = (0, new HashSet<int>(), new HashSet<int>());
        var (untried, refused) = (new SortedDictionary<string, int>(StringComparer.Ordinal), new SortedDictionary<string, int>(StringComparer.Ordinal));
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            for (var at = 4; at < StatPackRules.BlockSize; at += 4)
            {
                var (was, now) = (Word(disc, block, at), Word(changed, block, at));
                if (was == now)
                {
                    continue;
                }
                (block < StatPackRules.Machines ? machines : parts).Add(block);
                var problem = StatPackRules.Problem(block, at, now);
                var caution = problem is null ? StatPackRules.Caution(block, at, now) : null;
                var stat = $"{StatPackRules.BlockName(block).Split(' ')[0]} {StatPackRules.RuleAt(at)!.Name}";
                if (problem is not null)
                {
                    refused[stat] = refused.GetValueOrDefault(stat) + 1;
                }
                else if (caution is not null)
                {
                    untried[stat] = untried.GetValueOrDefault(stat) + 1;
                }
                else
                {
                    inRange++;
                }
                var where = block < StatPackRules.Machines ? $"machine {block} ({GameMachines.Names[block]})" : StatPackRules.BlockName(block);
                var verdict = problem is not null ? "refused: " + problem : caution is not null ? "untried: " + caution : "in range";
                output.WriteLine($"{where} {StatPackRules.WordName(at)}: {Shown(at, was)} -> {Shown(at, now)}: {verdict}");
            }
        }
        var (untriedCount, refusedCount) = (untried.Values.Sum(), refused.Values.Sum());
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{inRange + untriedCount + refusedCount} values differ from the disc's, in " +
            $"{machines.Count} machines and {parts.Count} custom parts: {inRange} in range, {untriedCount} in range and outside what has been tried, {refusedCount} refused"));
        Counted(output, "outside what has been tried", untried);
        Counted(output, "refused", refused);
        if (!disc[Names..(int)GameFacts.Files.CarPartsAt].SequenceEqual(changed[Names..(int)GameFacts.Files.CarPartsAt]) || !disc[PartNames..].SequenceEqual(changed[PartNames..]))
        {
            output.WriteLine("the file also changes names: a pack carries no name");
        }
        output.WriteLine("a report: nothing was written");
        return refusedCount == 0 ? Written : Refused;
    }

    private static void Counted(TextWriter output, string what, SortedDictionary<string, int> counts)
    {
        if (counts.Count > 0)
        {
            output.WriteLine($"{what}, by block and stat: " + string.Join("; ", counts.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Key} {c.Value}"))));
        }
    }

    /// <summary>A word as a report line shows it: a number; the two bytes at 0x48 (and the rest of the word, when a
    /// file has put something there); the bits of a word that is no number at all.</summary>
    private static string Shown(int at, uint bits)
    {
        if (at == (int)GameFacts.Files.CarBytesAt)
        {
            return $"{bits >> 24} {(bits >> 16) & 0xFF}" + ((bits & 0xFFFF) != 0 ? $" (and {bits & 0xFFFF:X4})" : "");
        }
        return bits != 0 && ((bits >> 23) & 0xFF) is 0 or 0xFF ? $"the bits {bits:X8}" : StatPackRules.Number(BitConverter.UInt32BitsToSingle(bits));
    }
}
