using System.Buffers.Binary;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

// Doctored images: each is refused with a sentence before anything it says is allocated, followed or written.
public sealed partial class DiscPrepTests
{
    /// <summary>What a refusal may allocate at the most. An image's own numbers (a size, a count, a name's length)
    /// are held against the limits first, so a refusal costs the same whatever they say.</summary>
    private const long Little = 4 << 20;

    private const string Changed = "This disc image was changed or is damaged. ";
    private const string DumpAgain = " Dump the disc again, or check the image in Dolphin (Properties, Verify).";

    private static uint Word(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);

    private static void Put(Span<byte> bytes, int at, uint word) => BinaryPrimitives.WriteUInt32BigEndian(bytes[at..], word);

    /// <summary>The made-up image's own file table.</summary>
    private byte[] Table()
    {
        var image = _disc.Build();
        return image[(int)Word(image, 0x424)..(int)(Word(image, 0x424) + Word(image, 0x428))];
    }

    /// <summary>The made-up image with another file table, which lies after everything else.</summary>
    private string WithTable(byte[] table)
    {
        var good = _disc.Build();
        var at = (good.Length + 0xFF) & ~0xFF;
        var image = new byte[at + table.Length + 0x40];
        good.CopyTo(image, 0);
        table.CopyTo(image, at);
        Put(image, 0x424, (uint)at);
        Put(image, 0x428, (uint)table.Length);
        var path = Path.Combine(_dir, "doctored.iso");
        File.WriteAllBytes(path, image);
        return path;
    }

    /// <summary>A table with more entries, as the root's last children. A null name: the name of the entry before.</summary>
    private static byte[] Added(byte[] table, params (byte Flag, string? Name, uint First, uint Second)[] extra)
    {
        var count = (int)Word(table, 8);
        var names = table.AsSpan(count * 12);
        using var entries = new MemoryStream();
        using var more = new MemoryStream();
        entries.Write(table, 0, count * 12);
        var nameAt = 0;
        Span<byte> entry = stackalloc byte[12];
        foreach (var (flag, name, first, second) in extra)
        {
            if (name is not null)
            {
                nameAt = names.Length + (int)more.Length;
                more.Write(Encoding.Latin1.GetBytes(name));
                more.WriteByte(0);
            }
            Put(entry, 0, ((uint)flag << 24) | (uint)nameAt);
            Put(entry, 4, first);
            Put(entry, 8, second);
            entries.Write(entry);
        }
        entries.Write(names);
        entries.Write(more.ToArray());
        var result = entries.ToArray();
        Put(result, 8, (uint)(count + extra.Length));
        return result;
    }

    /// <summary>A preparation that must be refused with nothing kept: its result and what it allocated.</summary>
    private (DiscPrepResult Result, long Allocated) Refused(string image)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = Run(image, null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(result.Ready);
        AssertNothingKept();
        return (result, allocated);
    }

    private void AssertChanged(string sentence, string image)
    {
        var (result, allocated) = Refused(image);
        Assert.Equal((DiscPrepOutcome.DamagedImage, Changed + sentence + DumpAgain), (result.Outcome, result.Message));
        Assert.InRange(allocated, 0, Little);
    }

    [Fact]
    public void ANameLongerThanAnyFolderHoldsIsRefusedUnread()
    {
        // 2,000 entries that share one name of a million characters: read as text, 4 GB
        var extra = new (byte, string?, uint, uint)[2000];
        extra[0] = (0, new string('n', 1_000_000), 0x440, 0);
        AssertChanged("Its file table has a name longer than 255 characters.", WithTable(Added(Table(), extra)));
        // one name of 20,000 parts with '/' between them
        AssertChanged("Its file table has a name longer than 255 characters.",
            WithTable(Added(Table(), (0, string.Join('/', Enumerable.Repeat("a", 20_000)), 0x440, 0))));
        // and the longest name that is taken is no more than another file of the game
        var most = Run(WithTable(Added(Table(), (0, new string('n', 255), 0x440, 0))), null);
        Assert.Equal((DiscPrepOutcome.ModifiedFile, "sys/fst.bin"), (most.Outcome, most.File));
    }

    [Fact]
    public void ANameWithASlashIsNoWayAroundTheDepthLimit()
    {
        // 100 folders deep in one name, short enough for a name
        AssertChanged("Its file table has a name with a '/' in it.",
            WithTable(Added(Table(), (0, string.Join('/', Enumerable.Repeat("a", 100)), 0x440, 0))));
        AssertChanged("Its file table has a name with a '/' in it.", WithTable(Added(Table(), (0, "stage/../../../../escape.bin", 0x440, 16))));
        AssertChanged("Its file table has a name with a '/' in it.", WithTable(Added(Table(), (0, "/abs.bin", 0x440, 16))));
    }

    [Fact]
    public void FoldersDeeperThanAnyDiscsAreRefused()
    {
        // 30,000 folders, each inside the one before: their paths as text, 1.8 GB
        var table = Table();
        var extra = new (byte, string?, uint, uint)[30_000];
        Array.Fill(extra, ((byte)1, (string?)null, 0u, Word(table, 8) + 30_000));
        extra[0].Item2 = "d";
        AssertChanged("Its file table has folders more than 32 deep, under " + string.Join('/', Enumerable.Repeat("d", 32)) + ".", WithTable(Added(table, extra)));
    }

    [Fact]
    public void APathLongerThanWindowsOpensIsRefused()
    {
        // 20 folders deep, well inside the depth limit, each name 20 characters: 419 characters at the bottom
        var table = Table();
        var extra = new (byte, string?, uint, uint)[20];
        Array.Fill(extra, ((byte)1, (string?)null, 0u, Word(table, 8) + 20));
        extra[0].Item2 = new string('p', 20);
        AssertChanged("Its file table has a path longer than 255 characters, under " + string.Join('/', Enumerable.Repeat(new string('p', 20), 12)) + ".",
            WithTable(Added(table, extra)));
    }

    [Fact]
    public void MoreEntriesThanAnyDiscHasAreRefused()
    {
        var extra = new (byte, string?, uint, uint)[70_000];
        extra[0] = (0, "x", 0x440, 0);
        AssertChanged("Its file table names 70,017 files and folders, and no GameCube disc is read with more than 65,536.", WithTable(Added(Table(), extra)));
    }

    [Fact]
    public void AFileTableLargerThanAnyDiscsIsRefusedUnread()
    {
        // the game's own table with filler after it: nothing in it is wrong but its size
        var table = new byte[DiscImage.LargestTable + 1];
        Table().CopyTo(table, 0);
        AssertChanged("Its file table is 4,194,305 bytes, and no GameCube disc's is read beyond 4,194,304.", WithTable(table));
        Assert.Throws<DiscLimitException>(() => DiscImage.TableOf(table));
        // at the limit it is the game's table still
        Assert.Equal(DiscPrepOutcome.Prepared, Run(WithTable(table[..DiscImage.LargestTable]), null).Outcome);
    }

    /// <summary>The made-up image with a loader that says it is this long, in a file long enough to hold it.</summary>
    private string WithLoader(uint size)
    {
        var image = _disc.Build();
        Put(image, 0x2440 + 0x14, size - 0x20);
        Put(image, 0x2440 + 0x18, 0);
        var path = Path.Combine(_dir, "doctored.iso");
        File.WriteAllBytes(path, image);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
        file.SetLength(0x2440L + size + 0x1000);
        return path;
    }

    [Fact]
    public void ASystemFileIsNotReadBeforeItsSizeIsThePinned()
    {
        // 20 MiB where the pin says 160 bytes: refused by its size, and no 20 MiB were allocated to find out
        var (result, allocated) = Refused(WithLoader(20 << 20));
        Assert.Equal((DiscPrepOutcome.ModifiedFile, "sys/apploader.img"), (result.Outcome, result.File));
        Assert.Equal("This disc image is not an unchanged the made-up game: its file sys/apploader.img is modified or damaged.", result.Message);
        Assert.InRange(allocated, 0, Little);
    }

    [Fact]
    public void ASystemFileLargerThanTheConsolesMemoryIsRefused()
    {
        var image = WithLoader(32 << 20);
        AssertChanged("Its sys/apploader.img is 33,554,432 bytes, and the console loads that file whole into 25,165,824 bytes of memory.", image);
        Assert.Throws<DiscLimitException>(() => DiscImage.Open(image));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("C:evil.bin")]
    [InlineData("..\\evil.bin")]
    [InlineData("café.bin")]
    [InlineData("bell\u0007.bin")]
    [InlineData("what?.bin")]
    public void AFileNameTheLauncherWillNotWriteIsRefused(string name)
    {
        var (result, _) = Refused(WithTable(Added(Table(), (0, name, 0x440, 16))));
        Assert.Equal(DiscPrepOutcome.ModifiedFile, result.Outcome);
        Assert.StartsWith("This disc image has a file name the launcher will not write: ", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir, "*evil*", SearchOption.AllDirectories));
    }

    [Fact]
    public void AFileNamedTwiceIsRefused()
    {
        // the second entry of a name points at other bytes; and a name twice but for its case, one file on Windows
        var table = Table();
        foreach (var name in new[] { "opening.bnr", "OPENING.BNR" })
        {
            var (result, _) = Refused(WithTable(Added(table, (0, name, 0x440, 6496))));
            Assert.Equal(DiscPrepOutcome.DamagedImage, result.Outcome);
            Assert.StartsWith($"This disc image is incomplete: The file table names {name} twice.", result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFileTableIsWrittenWithoutACallInsideACall()
    {
        // 100,000 folders deep: a writer that called itself for each folder would end the launcher here
        var table = DiscImage.CanonicalTable([(string.Join('/', Enumerable.Repeat("a", 100_000)) + "/f", 1)], [], 0x1000);
        Assert.Equal(100_002u, Word(table, 8));
        Assert.Equal((100_002 * 12) + 200_002, table.Length);
        Assert.Equal(1u, Word(table, (100_001 * 12) + 8));                     // the file, last
        Assert.Equal(0x01000000u | 2, Word(table, 2 * 12));                    // the second folder: its name after "a\0"
        Assert.Equal((1u, 100_002u), (Word(table, (2 * 12) + 4), Word(table, (2 * 12) + 8)));   // inside the first, to the end
    }
}
