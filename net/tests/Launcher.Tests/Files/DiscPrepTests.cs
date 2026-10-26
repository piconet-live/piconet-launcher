using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The game's files prepared from a disc image (<see cref="DiscPrep"/>), on made-up images
/// (<see cref="SyntheticDisc"/>): no game file is read or written by these tests.</summary>
public sealed partial class DiscPrepTests : IDisposable
{
    private const uint Linked = 0x497FD784;
    private const uint Sound = 0x4979B5F0;

    private readonly string _dir = Directory.CreateTempSubdirectory("disc-prep-tests").FullName;
    private readonly SyntheticDisc _disc = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private DiscPins Pins => _disc.Pins();

    private DiscPrepOptions Options => new() { Pins = Pins };

    private string Game => DiscPrep.GameFolder(Data, Pins);

    private DiscPrepResult Run(string? image, string? bundle, DiscPrepOptions? options = null, IProgress<DiscPrepProgress>? progress = null,
        CancellationToken? cancel = null) =>
        DiscPrep.Prepare(image, Data, bundle, progress, options ?? Options, cancel ?? TestContext.Current.CancellationToken);

    /// <summary>A native mod bundle's patch list: the executable's pin (as the real list has it too), a word in the
    /// text section, a word in the data section, and a word of main.rel, which is not the launcher's to write.</summary>
    private string Bundle(string name, uint linked = Linked, bool complete = true, uint? pinOriginal = null, uint? pinNew = null)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        string Entry(uint address, uint original, uint replacement, string what) => string.Create(CultureInfo.InvariantCulture,
            $"{{\"address\": \"{address:X8}\", \"original\": \"{original:X8}\", \"new\": \"{replacement:X8}\", \"what\": \"{what}\"}}");
        var entries = string.Join(",\n", [
            Entry(SyntheticDisc.TextAddress + 0x40, pinOriginal ?? SyntheticDisc.Word(_disc.Dol, 0x140), pinNew ?? 0x38600000, "pin"),
            Entry(SyntheticDisc.TextAddress + 0x80, SyntheticDisc.Word(_disc.Dol, 0x180), linked, "linked"),
            Entry(0x80004010, SyntheticDisc.Word(_disc.Dol, 0x310), Sound, "sound_queue"),
            Entry(0x801BB000, 0x11111111, 0x22222222, "a word of main.rel"),
        ]);
        File.WriteAllText(Path.Combine(folder, DiscPrep.PatchListName),
            $"{{\"version\": 1, \"complete\": {(complete ? "true" : "false")}, \"image_sha1\": \"{name}\", \"main_rel\": \"801BA660\", \"patches\": [\n{entries}\n]}}");
        return folder;
    }

    /// <summary>The prepared folder holds exactly the disc's files, with these words (file, offset, word) in.</summary>
    private void AssertGame(params (string File, int Offset, uint Word)[] words)
    {
        var expected = _disc.SystemFiles();
        foreach (var (path, data) in _disc.Files.Where(f => f.Value is not null))
        {
            expected["files/" + path] = data!;
        }
        foreach (var (file, offset, word) in words)
        {
            expected[file] = (byte[])expected[file].Clone();
            BinaryPrimitives.WriteUInt32BigEndian(expected[file].AsSpan(offset), word);
        }
        var found = Directory.EnumerateFiles(Game, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Game, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), found);
        foreach (var (name, data) in expected)
        {
            Assert.True(data.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Game, name))), name);
        }
        Assert.True(Directory.Exists(Path.Combine(Game, "files", "test_model")));   // the game's empty folder
    }

    private static (string, int, uint)[] PinWords => [("sys/main.dol", SyntheticDisc.DolPin, 0x38600000), ("files/fze.sample.rel", SyntheticDisc.SamplePin, 0x38A00000)];

    private static (string, int, uint)[] BundleWords(uint linked = Linked) => [.. PinWords, ("sys/main.dol", 0x180, linked), ("sys/main.dol", 0x310, Sound)];

    private void AssertNothingKept()
    {
        Assert.False(Directory.Exists(Game));
        Assert.False(Directory.Exists(Game + ".preparing"));
        Assert.False(File.Exists(DiscPrep.RecordFile(Data)));
        Assert.Equal(PreparedState.NotPrepared, DiscPrep.Status(Data, null, Pins).State);
        Assert.Null(DiscPrep.ReadyFolder(Data, null, Pins));
    }

    [Fact]
    public void AGoodDiscIsExtractedCheckedAndPatched()
    {
        var image = _disc.Write(_dir);
        var before = File.ReadAllBytes(image);
        var bundle = Bundle("native");
        var reports = new List<DiscPrepProgress>();

        var result = Run(image, bundle, Options, new Collect(reports));

        Assert.Equal(DiscPrepOutcome.Prepared, result.Outcome);
        Assert.True(result.Ready);
        Assert.Equal(Game, result.GameFolder);
        Assert.Equal(Path.Combine(Data, "recomp-game", "GFZE01"), Game);
        AssertGame(BundleWords());
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(image)));       // the image is only read
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, bundle, Pins).State);
        Assert.Equal(Game, DiscPrep.ReadyFolder(Data, bundle, Pins));

        // progress: the three stages in order, bytes never going back, the end at the whole
        Assert.Equal([DiscPrepStage.Checking, DiscPrepStage.Extracting, DiscPrepStage.Finishing], reports.Select(r => r.Stage).Distinct());
        Assert.All(reports.Zip(reports.Skip(1)), p => Assert.True(p.Second.BytesDone >= p.First.BytesDone));
        var last = reports[^1];
        Assert.Equal(last.BytesTotal, last.BytesDone);
        Assert.Equal(1.0, last.Fraction);
        Assert.Equal(10, last.FilesDone);
        Assert.Equal(result.BytesWritten, last.BytesTotal);
        Assert.Contains(reports, r => r.File == "movie/big.sfd");

        // the record: which disc, which words (with their stock words), which image
        var record = File.ReadAllText(DiscPrep.RecordFile(Data));
        Assert.Contains(Pins.ContentSha256, record, StringComparison.Ordinal);
        Assert.Contains("\"image_sha1\": \"native\"", record, StringComparison.Ordinal);
        Assert.Contains(SyntheticDisc.Word(_disc.Dol, 0x180).ToString("X8", CultureInfo.InvariantCulture), record, StringComparison.Ordinal);
        Assert.Contains("497FD784", record, StringComparison.Ordinal);
        Assert.DoesNotContain("22222222", record, StringComparison.Ordinal);       // main.rel's words are the game program's
    }

    [Fact]
    public void WithoutAPatchListOnlyThePinsAreWritten()
    {
        // no bundle, and the fork's kind of bundle (no list): the folder the reference configuration boots
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), null).Outcome);
        AssertGame(PinWords);
        var forks = Path.Combine(_dir, "fork-bundle");
        Directory.CreateDirectory(forks);
        File.WriteAllText(Path.Combine(forks, "codes.txt"), "");
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, forks, Pins).State);
    }

    [Fact]
    public void ASecondRunFindsEverythingReadyWithoutTheImage()
    {
        var bundle = Bundle("native");
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), bundle).Outcome);
        var written = Directory.GetLastWriteTimeUtc(Game);

        var again = Run(Path.Combine(_dir, "gone.iso"), bundle);
        Assert.Equal(DiscPrepOutcome.AlreadyReady, again.Outcome);
        Assert.True(again.Ready);
        Assert.Equal(Game, again.GameFolder);
        Assert.Equal(0, again.BytesWritten);
        Assert.Equal(written, Directory.GetLastWriteTimeUtc(Game));

        // asked to do it again from the image, it does
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), bundle, Options with { Again = true }).Outcome);
        AssertGame(BundleWords());
    }

    /// <summary>Everything under a folder: each file's path and bytes, and each folder's path.</summary>
    private static (SortedDictionary<string, byte[]> Files, List<string> Folders) Tree(string root) =>
        (new SortedDictionary<string, byte[]>(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes), StringComparer.Ordinal),
            [.. Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(root, d).Replace('\\', '/')).Order(StringComparer.Ordinal)]);

    /// <summary>The digests of a game folder from what is on the disk.</summary>
    private static (string Folder, string Files, long FilesBytes) Hashed(string game) => DiscPrep.HashFolder(game, TestContext.Current.CancellationToken);

    private static string Recorded(string data, string key) =>
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(DiscPrep.RecordFile(data)))![key]!.GetValue<string>();

    [Fact]
    public void ACancelledRunKeepsNothing()
    {
        using var stop = new CancellationTokenSource();
        var reports = new List<DiscPrepProgress>();
        var progress = new Collect(reports, r =>
        {
            if (r is { Stage: DiscPrepStage.Extracting, FilesDone: >= 3 })
            {
                stop.Cancel();
            }
        });
        var result = Run(_disc.Write(_dir), Bundle("native"), Options, progress, stop.Token);
        Assert.Equal(DiscPrepOutcome.Cancelled, result.Outcome);
        Assert.False(result.Ready);
        Assert.DoesNotContain(reports, r => r.Stage == DiscPrepStage.Finishing);
        AssertNothingKept();

        // and the same folder takes a whole preparation afterwards
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), Bundle("native")).Outcome);
        AssertGame(BundleWords());
    }

    [Fact]
    public async Task TheScreensCallReportsProgressAndCanBeCancelled()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var cancelled = await DiscPrep.PrepareAsync(_disc.Write(_dir), Data, null, options: Options, cancel: stop.Token);
        Assert.Equal(DiscPrepOutcome.Cancelled, cancelled.Outcome);
        var reports = new List<DiscPrepProgress>();
        var done = await DiscPrep.PrepareAsync(_disc.Write(_dir), Data, null, new Collect(reports), Options, TestContext.Current.CancellationToken);
        Assert.Equal(DiscPrepOutcome.Prepared, done.Outcome);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public void TooLittleFreeSpaceIsRefusedWithTheAmounts()
    {
        var options = Options with { FreeSpace = _ => 100L << 20 };
        // the made-up game is 1.5 MB; the launcher asks for 64 MB on top
        var result = Run(_disc.Write(_dir), null, Options with { FreeSpace = _ => 10L << 20 });
        Assert.Equal(DiscPrepOutcome.NotEnoughSpace, result.Outcome);
        Assert.Equal(10L << 20, result.BytesFree);
        Assert.InRange(result.BytesNeeded, (64L << 20) + 1_500_000, (64L << 20) + 1_700_000);
        Assert.Equal($"Not enough free space for the game's files in {Data}: they need 65 MB and 10 MB is free. Free 55 MB more and try again.", result.Message);
        AssertNothingKept();
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), null, options).Outcome);
        Assert.Equal("1.34 GB", DiscPrep.Size(1_433_628_143));
        Assert.NotNull(DiscPrep.FreeSpace(Path.Combine(_dir, "not", "there", "yet")));
    }

    /// <summary>Progress taken as it comes, on the preparing thread.</summary>
    private sealed class Collect(List<DiscPrepProgress> reports, Action<DiscPrepProgress>? then = null) : IProgress<DiscPrepProgress>
    {
        public void Report(DiscPrepProgress value)
        {
            reports.Add(value);
            then?.Invoke(value);
        }
    }
}
