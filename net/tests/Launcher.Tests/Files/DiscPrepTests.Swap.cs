using Launcher.Core;

namespace Launcher.Tests;

// The copy that works stays until the new one is checked; one preparation at a time; what the cheap check sees.
public sealed partial class DiscPrepTests
{
    private void AssertNothingAside()
    {
        Assert.False(Directory.Exists(Game + ".preparing"));
        Assert.False(Directory.Exists(Game + ".old"));
        Assert.False(File.Exists(Path.Combine(Data, DiscPrep.FolderName, DiscPrep.LockName)));
    }

    [Fact]
    public void PreparingAgainKeepsTheCopyThatWorksUntilTheNewOneIsChecked()
    {
        var bundle = Bundle("native");
        var options = Options;                                      // the pins of the unchanged game
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), bundle).Outcome);

        // again, from an image with one changed byte: it is refused after the whole copy, when the digest is known
        _disc.Files["movie/big.sfd"]![1_000_000] ^= 1;
        var changed = _disc.Write(_dir, "changed.iso");
        _disc.Files["movie/big.sfd"]![1_000_000] ^= 1;
        var refused = Run(changed, bundle, options with { Again = true });
        Assert.Equal(DiscPrepOutcome.ModifiedFile, refused.Outcome);
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, bundle, Pins).State);
        AssertGame(BundleWords());
        AssertNothingAside();

        // both copies are on the disk for that time: too little room for the second is said, and the first stays
        var full = Run(_disc.Write(_dir), bundle, options with { Again = true, FreeSpace = _ => 10L << 20 });
        Assert.Equal(DiscPrepOutcome.NotEnoughSpace, full.Outcome);
        Assert.EndsWith("Free 55 MB more and try again. The copy that is there stays until the new one is checked.", full.Message, StringComparison.Ordinal);
        Assert.Equal(Game, DiscPrep.ReadyFolder(Data, bundle, Pins));

        // a damaged copy is not kept: it makes room first, as before
        File.Delete(Path.Combine(Game, "files", "opening.bnr"));
        Assert.Equal(DiscPrepOutcome.ModifiedFile, Run(changed, bundle, options with { Again = true }).Outcome);
        AssertNothingKept();
    }

    [Fact]
    public void AFileHeldOpenLeavesTheCopyThatWorks()
    {
        var image = _disc.Write(_dir);
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null).Outcome);
        DiscPrepResult again;
        using (new FileStream(Path.Combine(Game, "files", "opening.bnr"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // a running game, a scanner: Windows moves no folder with an open file in it; Linux does
            again = Run(image, null, Options with { Again = true });
            Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, null, Pins).State);
        }
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DiscPrepOutcome.Failed, again.Outcome);
            Assert.Contains("That copy stays as it was. If the game is running, close it and try again.", again.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(DiscPrepOutcome.Prepared, again.Outcome);
        }
        AssertGame(PinWords);
        AssertNothingAside();
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null, Options with { Again = true }).Outcome);
        AssertGame(PinWords);
    }

    [Fact]
    public void WhatAStoppedPreparationLeftIsPutRightByTheNext()
    {
        var image = _disc.Write(_dir);
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null).Outcome);

        // stopped between the swap's two moves: the copy that worked is aside, and nothing has its name
        Directory.Move(Game, Game + ".old");
        Assert.Equal(PreparedState.Damaged, DiscPrep.Status(Data, null, Pins).State);
        Assert.Equal(DiscPrepOutcome.AlreadyReady, Run(null, null).Outcome);        // no image is needed to put it back
        AssertGame(PinWords);
        AssertNothingAside();

        // stopped while copying, and the next run is one that is refused: the unfinished copy goes all the same
        Directory.CreateDirectory(Path.Combine(Game + ".preparing", "files"));
        File.WriteAllText(Path.Combine(Game + ".preparing", "files", "opening.bnr"), "half");
        Directory.CreateDirectory(Game + ".old");
        Assert.Equal(DiscPrepOutcome.WrongGame, Run(new SyntheticDisc { Id = "GALE01" }.Write(_dir, "other.iso"), null, Options with { Again = true }).Outcome);
        AssertNothingAside();
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, null, Pins).State);
        AssertGame(PinWords);
    }

    [Fact]
    public void OfTwoPreparationsAtOnceTheSecondIsToldSo()
    {
        var image = _disc.Write(_dir);
        DiscPrepResult? second = null;
        var first = Run(image, null, Options, new Collect([], report =>
        {
            if (second is null && report.Stage == DiscPrepStage.Extracting)
            {
                second = Run(image, null, Options with { Again = true });   // while the first one copies
            }
        }));
        Assert.Equal((DiscPrepOutcome.Busy, DiscPrep.BusyMessage), (second!.Outcome, second.Message));
        Assert.False(second.Ready);
        Assert.Equal(DiscPrepOutcome.Prepared, first.Outcome);
        AssertGame(PinWords);
        AssertNothingAside();
        Assert.Equal(DiscPrepOutcome.AlreadyReady, Run(image, null).Outcome);   // the lock went with the first
    }

    [Fact]
    public void AnEmptiedFileIsSeenAtOnceAndAChangedByteByTheWholeCheck()
    {
        var image = _disc.Write(_dir);
        void Fresh() => Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null, Options with { Again = true }).Outcome);
        void Emptied(string file)
        {
            var status = DiscPrep.Status(Data, null, Pins);
            Assert.Equal((PreparedState.Damaged, file), (status.State, status.File));
            Assert.Equal($"Game files damaged · {file} was emptied or cut short · prepare again", status.Message);
            Assert.Null(DiscPrep.ReadyFolder(Data, null, Pins));
        }

        // zeros at the same size: what a power cut leaves of a file that had its room and not yet its bytes
        Fresh();
        File.WriteAllBytes(Path.Combine(Game, "files", "stage", "st01.gma"), new byte[70_000]);
        Emptied("files/stage/st01.gma");

        // cut short and filled up: only the end is zeros
        Fresh();
        var film = Path.Combine(Game, "files", "movie", "big.sfd");
        var bytes = File.ReadAllBytes(film);
        bytes.AsSpan(1_499_000).Clear();
        File.WriteAllBytes(film, bytes);
        Emptied("files/movie/big.sfd");

        // one bit in the middle of a file no pin names: the cheap check reads one byte of it and sees nothing; the
        // whole check reads everything
        Fresh();
        Assert.Equal(PreparedState.Ready, DiscPrep.Verify(Data, null, Pins, TestContext.Current.CancellationToken).State);
        bytes = File.ReadAllBytes(film);
        bytes[700_000] ^= 1;
        File.WriteAllBytes(film, bytes);
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, null, Pins).State);
        var whole = DiscPrep.Verify(Data, null, Pins, TestContext.Current.CancellationToken);
        Assert.Equal(PreparedState.Damaged, whole.State);
        Assert.Equal("Game files damaged · a file's bytes are not the ones that were prepared · prepare again", whole.Message);

        // a record from before the last bytes were kept: the rest of the check stands
        Fresh();
        var record = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(DiscPrep.RecordFile(Data)))!.AsObject();
        Assert.Equal(6, record["ends"]!.AsObject().Count);          // the ten files but the three a pin names and the empty one
        record.Remove("ends");
        File.WriteAllText(DiscPrep.RecordFile(Data), record.ToJsonString());
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, null, Pins).State);
    }

    [Fact]
    public void AFileOfAnotherSizeInThePreparedFolderIsNotRead()
    {
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), null).Outcome);
        using (var file = new FileStream(Path.Combine(Game, "sys", "main.dol"), FileMode.Open, FileAccess.Write))
        {
            file.SetLength(64 << 20);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var status = DiscPrep.Status(Data, null, Pins);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, Little);
        Assert.Equal((PreparedState.Damaged, "sys/main.dol"), (status.State, status.File));
        Assert.Equal("Game files damaged · sys/main.dol has another size · prepare again", status.Message);

        // and a record that is no record, whatever its size
        File.WriteAllText(DiscPrep.RecordFile(Data), new string(' ', 5 << 20));
        before = GC.GetAllocatedBytesForCurrentThread();
        status = DiscPrep.Status(Data, null, Pins);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, Little);
        Assert.Equal((PreparedState.Damaged, DiscPrep.RecordName), (status.State, status.File));
    }
}
