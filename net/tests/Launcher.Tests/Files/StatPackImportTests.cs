using Fzgx.GameCube.Compression;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A stat pack imported from a changed car data file (<see cref="StatPackImport"/>, the launcher's
/// <c>--import-stat-pack</c>; handbook/stat-packs.md), on a made-up disc and made-up files
/// (<see cref="MadeUpCarData"/>): the pack holds every word that differs and nothing else, never a block's first
/// word, and a file that is not car data, changes nothing or holds a value outside its range gives none.
/// </summary>
public sealed class StatPackImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stat-pack-tests").FullName;
    private readonly string _disc;
    private readonly DiscPins _pins;

    public StatPackImportTests()
    {
        var disc = MadeUpStats.Disc(MadeUpCarData.File(), null);
        (_disc, _pins) = (disc.Write(_dir, "game.iso"), disc.Pins());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static uint Bits(float value) => MadeUpCarData.Bits(value);

    /// <summary>The Blue Falcon's weight doubled, its two bytes, a hover point of the last machine, a body's weight
    /// and a booster's boost strength.</summary>
    private static readonly (int Block, int At, uint Bits)[] Changes =
    [
        (6, 0x04, Bits(2 * BitConverter.UInt32BitsToSingle(MadeUpCarData.Word(6, 0x04)))), (6, 0x48, 0x03010000), (40, 0x5C, Bits(-1.5f)),
        (41, 0x04, Bits(500)), (115, 0x34, Bits(14)),
    ];

    private (int Status, string Said, string Out) Import(byte[] file, string? name = "Made up", string fileName = "cardata.bin", string? disc = null,
        bool anyDisc = false)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, file);
        var output = new StringWriter();
        var written = Path.Combine(_dir, "out.fzstats");
        File.Delete(written);
        var status = StatPackImport.Run(disc ?? _disc, path, name, written, output, anyDisc, pins: _pins);
        return (status, output.ToString().ReplaceLineEndings("\n"), written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePackIsEveryWordThatDiffersFromTheDiscsAndNothingElse(bool packed)
    {
        var changed = MadeUpCarData.With(Changes);

        var (status, said, written) = Import(packed ? AvLz.Compress(changed) : changed, fileName: packed ? "cardata.lz" : "cardata.bin");

        Assert.Equal(StatPackImport.Written, status);
        var (pack, refusal) = StatPackText.Read(File.ReadAllText(written));
        Assert.Null(refusal);
        Assert.Equal("Made up", pack!.Name);
        Assert.Equal(Changes.Select(c => new StatEntry(c.Block, c.At, c.Bits)), pack.Entries);
        Assert.Equal($"stat pack Made up: 5 values differ from the disc's, in 2 machines and 2 custom parts\nfingerprint: {pack.Fingerprint}\nwritten: {written}\n", said);
        Assert.StartsWith("fzstats 1\nname Made up\n# Blue Falcon\nmachine 6 Weight ", File.ReadAllText(written), StringComparison.Ordinal);
        Assert.Contains("\nmachine 6 Bytes48 3 1\n# Rainbow Phoenix\nmachine 40 Hover0Z -1.5\nbody 0 Weight 500\nbooster 24 BoostStrength 14\n",
            File.ReadAllText(written), StringComparison.Ordinal);
    }

    /// <summary>A whole replacement file is a large pack: every word of every block changed gives all 5,104, and no
    /// block's first word is among them.</summary>
    [Fact]
    public void AWholeReplacementFileIsALargePack()
    {
        var all = MadeUpCarData.File((block, at) => at == 0x48 ? MadeUpCarData.Word(block, at) ^ 0x01000000
            : Bits(BitConverter.UInt32BitsToSingle(MadeUpCarData.Word(block, at)) + 0.03125f));

        var (pack, refusal, names) = StatPackImport.Differences(MadeUpCarData.File(), all, "Whole");

        Assert.Null(refusal);
        Assert.False(names);
        Assert.Equal(StatPackRules.MostEntries, pack!.Entries.Count);
        Assert.DoesNotContain(pack.Entries, e => e.At == 0);
        // without --out the pack is written beside the car data file, named after the pack
        var path = Path.Combine(_dir, "whole.bin");
        File.WriteAllBytes(path, all);
        Assert.Equal(StatPackImport.Written, StatPackImport.Run(_disc, path, "GX Whole 2.0 (test)", null, new StringWriter(), pins: _pins));
        Assert.Equal(pack.Fingerprint, StatPackText.Read(File.ReadAllText(Path.Combine(_dir, "GX-Whole-2.0--test-.fzstats"))).Pack!.Fingerprint);
    }

    [Fact]
    public void ChangedNamesAreLeftOutAndSaid()
    {
        var renamed = MadeUpCarData.File((block, at) => (block, at) == (6, 4) ? Bits(1000) : null, names: "RAINBOW\0RENAMED\0");

        var (status, said, written) = Import(renamed);

        Assert.Equal(StatPackImport.Written, status);
        Assert.Contains("\nthe file also changes names: a pack carries no name, and they are left out\n", said, StringComparison.Ordinal);
        Assert.Equal([StatEntry.Of(6, 4, 1000)], StatPackText.Read(File.ReadAllText(written)).Pack!.Entries);
        Assert.DoesNotContain("RENAMED", File.ReadAllText(written), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotCarDataIsRefused()
    {
        var good = MadeUpCarData.With(Changes);
        var noise = new byte[MadeUpCarData.Length];
        new Random(7).NextBytes(noise);
        var name = MadeUpCarData.File();
        name[StatPackRules.BlockAt(70)] = 1;
        var gap = MadeUpCarData.File();
        gap[0x1CD8] = 1;
        var huge = AvLz.Compress(good);
        BitConverter.GetBytes(0x6001).CopyTo(huge, 4);
        var broken = AvLz.Compress(good)[..^40];
        BitConverter.GetBytes(broken.Length - 8).CopyTo(broken, 0);
        foreach (var (file, why) in new (byte[], string)[]
                 {
                     ([], "It is 0 bytes unpacked, and car data is 21,440 to 24,576."),
                     (good[..0x53BF], "It is 21,439 bytes unpacked, and car data is 21,440 to 24,576."),
                     ([.. good, .. new byte[0x6001 - good.Length]], "no such file, or longer than any car data."),
                     (noise, "The first word of its block 0 is not 0, as every block's of a car data file is."),
                     (name, "The first word of its block 70 is not 0, as every block's of a car data file is."),
                     (gap, "The bytes between its machines and their names are not 0, as a car data file's are."),
                     (huge, "It is a packed file that unpacks to more than any car data."),
                     (broken, "It is a packed file that does not unpack."),
                 })
        {
            var (status, said, written) = Import(file);
            Assert.Equal(StatPackImport.Refused, status);
            Assert.StartsWith("refused: ", said, StringComparison.Ordinal);
            Assert.EndsWith(why + "\n", said, StringComparison.Ordinal);
            Assert.False(File.Exists(written));
        }
        Assert.NotNull(StatPackImport.CarDataOf([.. good, .. new byte[0x6000 - good.Length]]).Data);   // the loader's room, to the byte
    }

    [Fact]
    public void NoDifferenceAValueOutOfRangeNoNameAndNoDiscAreRefused()
    {
        var good = MadeUpCarData.With(Changes);
        Assert.Equal((StatPackImport.Refused, "refused: Every value of the file is the disc's own: there is nothing for a pack to hold.\n"),
            Take(Import(MadeUpCarData.File())));
        Assert.Equal((StatPackImport.Refused, "refused: machine 6 Weight is 0, and must be at least 1.\n"), Take(Import(MadeUpCarData.With((6, 4, 0)))));
        Assert.Equal((StatPackImport.Refused, "refused: booster 3 BoostDuration is 5, and a part's value is less than 4.26 from 0.\n"),
            Take(Import(MadeUpCarData.With((6, 4, Bits(900)), (94, 0x38, Bits(5))))));
        foreach (var name in new[] { null, "", " x", new string('n', 41) })
        {
            Assert.Equal((StatPackImport.Refused, "refused: give the pack a name with --pack-name: 1 to 40 characters of plain ASCII.\n"),
                Take(Import(good, name)));
        }
        // a disc image that is not the game's, none at all, and a car data file that is not there
        File.WriteAllBytes(Path.Combine(_dir, "other.iso"), new byte[4096]);
        foreach (var disc in new[] { Path.Combine(_dir, "other.iso"), Path.Combine(_dir, "none.iso") })
        {
            var (status, said, _) = Import(good, disc: disc);
            Assert.Equal(StatPackImport.Refused, status);
            Assert.Equal($"refused: {disc}: not the disc of F-Zero GX (USA), or its car data cannot be read. Name the image with --disc.\n", said);
        }
        var output = new StringWriter();
        Assert.Equal(StatPackImport.Refused, StatPackImport.Run(null, Path.Combine(_dir, "x"), "p", null, output));
        Assert.StartsWith("refused: no disc image: not the disc", output.ToString(), StringComparison.Ordinal);
        output = new StringWriter();
        Assert.Equal(StatPackImport.Refused, StatPackImport.Run(_disc, Path.Combine(_dir, "not-there.lz"), "p", null, output, pins: _pins));
        Assert.EndsWith("not-there.lz: no such file, or longer than any car data.\n", output.ToString().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        // a pack that cannot be written
        File.WriteAllBytes(Path.Combine(_dir, "c.bin"), good);
        output = new StringWriter();
        Assert.Equal(StatPackImport.NotWritten, StatPackImport.Run(_disc, Path.Combine(_dir, "c.bin"), "p", Path.Combine(_dir, "no-folder", "p.fzstats"), output, pins: _pins));
        Assert.StartsWith("not written: ", output.ToString(), StringComparison.Ordinal);

        static (int, string) Take((int Status, string Said, string Out) result) => (result.Status, result.Said);
    }

    /// <summary>A value the game takes and nobody has raced (beyond the "Tried" ranges of the protocol's handbook/stat-packs.md table) is
    /// written into the pack all the same, and said as a warning to whoever makes the pack: the launcher of a player
    /// who is handed the pack takes it too.</summary>
    [Fact]
    public void AValueOutsideWhatHasBeenTriedIsWrittenAndSaid()
    {
        var (status, said, written) = Import(MadeUpCarData.With((6, 4, Bits(900)), (6, 0x10, Bits(9.5f)), (7, 0x20, Bits(2000))));

        Assert.Equal(StatPackImport.Written, status);
        var lines = said.Split('\n');
        Assert.Equal(("stat pack Made up: 3 values differ from the disc's, in 2 machines and 0 custom parts",
            "warning: 2 of the pack's values are outside what has been tried. The game takes them; nobody has raced them:",
            "  machine 6 Grip1 is 9.5, outside what has been tried (0 to 9).",
            "  machine 7 TurnMovement is 2000, outside what has been tried (0 to 400)."), (lines[0], lines[1], lines[2], lines[3]));
        Assert.StartsWith("fingerprint: ", lines[4], StringComparison.Ordinal);
        var pack = StatPackText.Read(File.ReadAllText(written)).Pack!;
        Assert.Equal([Bits(900), Bits(9.5f), Bits(2000)], pack.Entries.Select(e => e.Bits));
        // a pack with nothing beyond them says no such line
        Assert.DoesNotContain("warning", Import(MadeUpCarData.With(Changes)).Said, StringComparison.Ordinal);
    }

    /// <summary>The report (<c>--report</c>) sees the whole file where the import stops at the first value it
    /// refuses: every difference a line, with the disc's value, the file's, and what the ranges say of it; then the
    /// totals. It writes nothing, and its status says whether a pack of the file would be taken.</summary>
    [Fact]
    public void TheReportListsEveryDifferenceWithWhatTheRangesSayAndWritesNothing()
    {
        static string Disc(int block, int at) => StatPackRules.Number(BitConverter.UInt32BitsToSingle(MadeUpCarData.Word(block, at)));
        var path = Path.Combine(_dir, "report.bin");
        File.WriteAllBytes(path, MadeUpCarData.With((6, 4, Bits(900)), (6, 0x10, Bits(9.5f)), (6, 0x30, Bits(-10.5f)), (6, 0x48, 0x03010000),
            (41, 0x30, Bits(-0.05f)), (94, 0x38, Bits(5))));
        var output = new StringWriter();

        var status = StatPackImport.Report(_disc, path, output, pins: _pins);

        var falcon = $"machine 6 ({GameMachines.Names[6]})";
        Assert.Equal(
        [
            $"{falcon} Weight: {Disc(6, 4)} -> 900: in range",
            $"{falcon} Grip1: {Disc(6, 0x10)} -> 9.5: untried: is 9.5, outside what has been tried (0 to 9)",
            $"{falcon} Grip2: {Disc(6, 0x30)} -> -10.5: refused: is -10.5, and must be at least -10",
            $"{falcon} Bytes48: 6 2 -> 3 1: in range",
            $"body 0 Grip2: {Disc(41, 0x30)} -> -0.05: in range",
            $"booster 3 BoostDuration: {Disc(94, 0x38)} -> 5: refused: is 5, and a part's value is less than 4.26 from 0",
            "6 values differ from the disc's, in 1 machines and 2 custom parts: 3 in range, 1 in range and outside what has been tried, 2 refused",
            "outside what has been tried, by block and stat: machine Grip1 1",
            "refused, by block and stat: booster BoostDuration 1; machine Grip2 1",
            "a report: nothing was written",
            "",
        ], output.ToString().ReplaceLineEndings("\n").Split('\n'));
        Assert.Equal(StatPackImport.Refused, status);
        Assert.Empty(Directory.GetFiles(_dir, "*.fzstats"));
        // a file a pack would be made of: status 0; one that is no car data: the import's own sentence
        File.WriteAllBytes(path, MadeUpCarData.With(Changes));
        output = new StringWriter();
        Assert.Equal(StatPackImport.Written, StatPackImport.Report(_disc, path, output, pins: _pins));
        Assert.Contains("5 values differ from the disc's, in 2 machines and 2 custom parts: 5 in range, 0 in range and outside what has been tried, 0 refused",
            output.ToString(), StringComparison.Ordinal);
        File.WriteAllBytes(path, new byte[100]);
        output = new StringWriter();
        Assert.Equal(StatPackImport.Refused, StatPackImport.Report(_disc, path, output, pins: _pins));
        Assert.StartsWith($"refused: {path} is not a car data file.", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The pack is the file's differences from the disc's car data: against an image whose car data (or
    /// any file) is not the pinned disc's, the same file gives another pack. So the image is held to the pins the
    /// preparation uses, every file of it, unless --any-disc says to take it as it is. And a pack file that is there
    /// already is not written over unless --overwrite says so.</summary>
    [Fact]
    public void TheDiscMustBeTheUnchangedOneAndAFileThatIsThereIsNotWrittenOver()
    {
        var good = MadeUpCarData.With(Changes);
        // an image with one stat of its own car data changed: by its shape it is car data still
        var modified = MadeUpStats.Disc(MadeUpCarData.With((12, 0x0C, Bits(0.05f))), null).Write(_dir, "modified.iso");

        var (status, said, written) = Import(good, disc: modified);
        Assert.Equal(StatPackImport.Refused, status);
        Assert.Equal($"refused: {modified}: not an unchanged disc of F-Zero GX (USA): one of its files is modified or damaged, and a pack made " +
                     "against it would not be the pack players' games need. --any-disc takes the image as it is.\n", said);
        Assert.False(File.Exists(written));
        // taken knowingly, the pack is another: it also holds the value the image had changed
        (status, _, written) = Import(good, disc: modified, anyDisc: true);
        Assert.Equal(StatPackImport.Written, status);
        Assert.Equal(Changes.Length + 1, StatPackText.Read(File.ReadAllText(written)).Pack!.Entries.Count);
        using (var art = DiscArt.Open(_disc))
        using (var other = DiscArt.Open(modified))
        {
            Assert.True(art!.IsUnchanged(_pins));
            Assert.False(other!.IsUnchanged(_pins));
        }

        // a file at the output's place: kept, unless told otherwise
        var path = Path.Combine(_dir, "c.bin");
        var there = Path.Combine(_dir, "there.fzstats");
        File.WriteAllBytes(path, good);
        File.WriteAllText(there, "somebody's pack");
        var output = new StringWriter();
        Assert.Equal(StatPackImport.Refused, StatPackImport.Run(_disc, path, "p", there, output, pins: _pins));
        Assert.Equal($"refused: {there} is there already: name another file with --out, or give --overwrite.\n", output.ToString().ReplaceLineEndings("\n"));
        Assert.Equal("somebody's pack", File.ReadAllText(there));
        Assert.Equal(StatPackImport.Written, StatPackImport.Run(_disc, path, "p", there, new StringWriter(), overwrite: true, pins: _pins));
        Assert.StartsWith("fzstats 1\nname p\n", File.ReadAllText(there), StringComparison.Ordinal);
    }

    /// <summary>The Garage's table with a pack over it (<see cref="MachineStatTable.With(StatPack)"/>): a machine's
    /// stats change, and what the table has no place for changes nothing. The stats' names in a pack are the
    /// launcher's own (<see cref="MachineStat"/>), offset for offset.</summary>
    [Fact]
    public void APackIsLaidOverTheStatTable()
    {
        using var art = DiscArt.Open(_disc);
        var table = art!.MachineStats();
        var pack = StatPack.Of("p", [.. Changes.Select(c => new StatEntry(c.Block, c.At, c.Bits)), StatEntry.Of(12, 0x34, 21.5f)]).Pack!;

        var laid = table!.With(pack);

        Assert.Equal(2 * table.Value(6, MachineStat.Weight), laid.Value(6, MachineStat.Weight));
        Assert.Equal(21.5f, laid.Value(12, MachineStat.BoostStrength));
        Assert.Equal("21.5", laid.Text(12, MachineStat.BoostStrength));
        foreach (var machine in Enumerable.Range(0, 41))
        {
            foreach (var stat in MachineStats.All.Where(s => (machine, s.Stat) is not ((6, MachineStat.Weight) or (12, MachineStat.BoostStrength))))
            {
                Assert.Equal(table.Value(machine, stat.Stat), laid.Value(machine, stat.Stat));
            }
        }
        Assert.Equal(table.Value(6, MachineStat.Weight), art.MachineStats()!.Value(6, MachineStat.Weight));   // the disc's table is as it was
        Assert.All(MachineStats.All.Where(s => s.At >= 0), s => Assert.Equal(s.Stat.ToString(), StatPackRules.WordName(s.At)));
    }
}
