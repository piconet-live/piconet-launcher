using Launcher.Core;

namespace Launcher.Tests;

// What the data folder holds: ready, stale, damaged, and what a game start takes.
public sealed partial class DiscPrepTests
{
    [Fact]
    public void AStaleFolderIsBroughtUpToDateWithoutTheDisc()
    {
        var first = Bundle("image-1");
        var second = Bundle("image-2", linked: 0x497FD900);
        var image = _disc.Write(_dir);
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, first).Outcome);
        File.Delete(image);                                 // the disc is not needed for what follows

        // the launcher was updated: another mod image, so another branch in the executable
        var stale = DiscPrep.Status(Data, second, Pins);
        Assert.Equal(PreparedState.Stale, stale.State);
        Assert.False(stale.NeedsDisc);
        AssertGame(BundleWords());                          // asking changes nothing

        var before = Recorded(Data, "folder_sha256");
        Assert.Equal(Game, DiscPrep.ReadyFolder(Data, second, Pins));   // what a game start asks: brought up to date
        AssertGame(BundleWords(0x497FD900));
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, second, Pins).State);
        // the record's digest follows the words, and is the folder's as it stands
        Assert.NotEqual(before, Recorded(Data, "folder_sha256"));
        Assert.Equal(Hashed(Game).Folder, Recorded(Data, "folder_sha256"));
        Assert.Equal(Hashed(Game).Files, Recorded(Data, "files_sha256"));
        Assert.Contains("\"image_sha1\": \"image-2\"", File.ReadAllText(DiscPrep.RecordFile(Data)), StringComparison.Ordinal);
        Assert.Equal(PreparedState.Stale, DiscPrep.Status(Data, first, Pins).State);

        // to a bundle without a list (the fork's): the list's words are taken back, the pins stay
        Assert.Equal(DiscPrepOutcome.Refreshed, DiscPrep.Refresh(Data, null, Pins).Outcome);
        AssertGame(PinWords);
        // and back through the whole preparation's door, still without the image
        var back = Run(null, first);
        Assert.Equal(DiscPrepOutcome.Refreshed, back.Outcome);
        Assert.Equal((before, before), (back.FolderSha256, Recorded(Data, "folder_sha256")));
        AssertGame(BundleWords());

        // other pins for the game's own file (another launcher): its word is rewritten too, and the digest is the folder's
        var repinned = Pins with { Words = [Pins.Words[0], Pins.Words[1] with { New = 0x38A00001 }] };
        Assert.Equal(DiscPrepOutcome.Refreshed, DiscPrep.Refresh(Data, first, repinned).Outcome);
        Assert.Equal(0x38A00001u, SyntheticDisc.Word(File.ReadAllBytes(Path.Combine(Game, "files", "fze.sample.rel")), SyntheticDisc.SamplePin));
        Assert.NotEqual(before, Recorded(Data, "folder_sha256"));
        Assert.Equal((Hashed(Game).Folder, Hashed(Game).Files), (Recorded(Data, "folder_sha256"), Recorded(Data, "files_sha256")));
        Assert.Equal(DiscPrepOutcome.Refreshed, DiscPrep.Refresh(Data, first, Pins).Outcome);
        Assert.Equal(before, Recorded(Data, "folder_sha256"));
        Assert.Equal(DiscPrepOutcome.AlreadyReady, DiscPrep.Refresh(Data, first, Pins).Outcome);
    }

    [Fact]
    public void ARecordOfAnotherDiscOrFormatNeedsTheDiscAgain()
    {
        var image = _disc.Write(_dir);
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null).Outcome);

        // pins of another disc (a later launcher that accepts another one)
        var other = Pins with { ContentSha256 = new string('0', 64) };
        var status = DiscPrep.Status(Data, null, other);
        Assert.Equal(PreparedState.Stale, status.State);
        Assert.True(status.NeedsDisc);
        Assert.Null(DiscPrep.ReadyFolder(Data, null, other));
        Assert.Equal(DiscPrepOutcome.Failed, DiscPrep.Refresh(Data, null, other).Outcome);

        // a record of another format (1: before the folder was written in one form for every image)
        var record = DiscPrep.RecordFile(Data);
        Assert.Contains("\"format\": 2", File.ReadAllText(record), StringComparison.Ordinal);
        File.WriteAllLines(record, File.ReadAllLines(record).Where(l => !l.Contains("\"folder_sha256\"", StringComparison.Ordinal) &&
            !l.Contains("\"files_sha256\"", StringComparison.Ordinal) && !l.Contains("\"files_bytes\"", StringComparison.Ordinal))
            .Select(l => l.Replace("\"format\": 2", "\"format\": 1", StringComparison.Ordinal)));
        Assert.DoesNotContain("folder_sha256", File.ReadAllText(record), StringComparison.Ordinal);
        Assert.Equal("Game files from another launcher version · prepare again", DiscPrep.Status(Data, null, Pins).Message);
        Assert.Equal((PreparedState.Stale, true), (DiscPrep.Status(Data, null, Pins).State, DiscPrep.Status(Data, null, Pins).NeedsDisc));
        // a record that is no record
        File.WriteAllText(record, "{ not json");
        Assert.Equal(PreparedState.Damaged, DiscPrep.Status(Data, null, Pins).State);

        // each is mended by preparing again
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, null).Outcome);
        Assert.Equal(PreparedState.Ready, DiscPrep.Status(Data, null, Pins).State);
    }

    [Fact]
    public void ADamagedFolderIsToldApartAndNamed()
    {
        var image = _disc.Write(_dir);
        var bundle = Bundle("native");
        void Fresh() => Assert.Equal(DiscPrepOutcome.Prepared, Run(image, bundle, Options with { Again = true }).Outcome);
        void Damaged(string file, string message)
        {
            var status = DiscPrep.Status(Data, bundle, Pins);
            Assert.Equal(PreparedState.Damaged, status.State);
            Assert.True(status.NeedsDisc);
            Assert.Equal(file, status.File);
            Assert.Equal($"Game files damaged · {message} · prepare again", status.Message);
            Assert.Null(DiscPrep.ReadyFolder(Data, bundle, Pins));
        }

        Fresh();
        File.Delete(Path.Combine(Game, "files", "stage", "st01.gma"));
        Damaged("files/stage/st01.gma", "files/stage/st01.gma is missing");

        Fresh();
        File.AppendAllText(Path.Combine(Game, "files", "movie", "big.sfd"), "x");
        Damaged("files/movie/big.sfd", "files/movie/big.sfd has another size");

        Fresh();
        File.WriteAllText(Path.Combine(Game, "files", "stage", "mine.txt"), "x");   // it would move the game's files on the disc the runner builds
        Damaged("files/stage/mine.txt", "files/stage/mine.txt does not belong to the game");

        Fresh();
        var dol = Path.Combine(Game, "sys", "main.dol");
        var bytes = File.ReadAllBytes(dol);
        bytes[0x3F0] ^= 1;
        File.WriteAllBytes(dol, bytes);
        Damaged("sys/main.dol", "sys/main.dol is changed");

        Fresh();
        File.Delete(Path.Combine(Game, "files", "fze.sample.rel"));
        Damaged("files/fze.sample.rel", "files/fze.sample.rel is missing");

        Fresh();
        Directory.Delete(Path.Combine(Game, "files", "test_model"));
        Damaged("files/test_model", "the folder files/test_model is missing");

        // the whole preparation mends it
        Assert.Equal(DiscPrepOutcome.Prepared, Run(image, bundle).Outcome);
        AssertGame(BundleWords());

        // a game folder without its record (a preparation that was interrupted) is nothing
        File.Delete(DiscPrep.RecordFile(Data));
        Assert.Equal(PreparedState.NotPrepared, DiscPrep.Status(Data, bundle, Pins).State);
        Assert.Equal(DiscPrep.NotPreparedMessage, DiscPrep.Status(Data, bundle, Pins).Message);
    }

    [Fact]
    public void AGameStartUsesThePreparedFolderElseTheOneBesideTheLauncher()
    {
        // nothing prepared: the hand-laid folder beside the launcher, as before (staged test copies)
        var launcher = Path.Combine(_dir, "launcher");
        var paths = RecompLaunch.Resolve(null, launcher, Data);
        Assert.Equal(Path.Combine(launcher, "recomp", "game"), paths.GameFolder);
        // and a start with nothing there is a result the interface can show, not an exception
        Directory.CreateDirectory(Path.Combine(launcher, "recomp"));
        File.WriteAllText(paths.Runner, "");
        File.WriteAllText(paths.Module, "");
        Assert.Equal(DiscPrep.NotPreparedMessage, RecompLaunch.Problem(paths));
        Assert.Equal(DiscPrep.NotPreparedMessage, DiscPrep.Status(Data, paths.Bundle).Message);

        // a folder prepared from a made-up disc is not the game's: the launcher's own pins do not take it
        Assert.Equal(DiscPrepOutcome.Prepared, Run(_disc.Write(_dir), null).Outcome);
        Assert.Equal(Game, DiscPrep.ReadyFolder(Data, paths.Bundle, Pins));
        Assert.Equal(PreparedState.Stale, DiscPrep.Status(Data, paths.Bundle).State);
        Assert.Equal(Path.Combine(launcher, "recomp", "game"), RecompLaunch.Resolve(null, launcher, Data).GameFolder);
        // the settings' own folder (development) still wins
        Assert.Equal("D:/gx", RecompLaunch.Resolve(new RecompSettings(GameFolder: "D:/gx"), launcher, Data).GameFolder);
    }

    /// <summary>What a script that prepared a disc without the window is told. "Another launcher is preparing" was
    /// status 2, the image refused: nothing is wrong with the image, and a script that gives up on a refused image
    /// would give up on a sound one.</summary>
    [Fact]
    public void EveryOutcomeHasItsExitStatusAndBusyIsNotARefusedImage()
    {
        static int Status(DiscPrepOutcome outcome) => new DiscPrepResult(outcome, "").ExitStatus;
        Assert.All(new[] { DiscPrepOutcome.Prepared, DiscPrepOutcome.AlreadyReady, DiscPrepOutcome.Refreshed }, o => Assert.Equal(0, Status(o)));
        Assert.All(new[]
        {
            DiscPrepOutcome.ImageMissing, DiscPrepOutcome.NotADisc, DiscPrepOutcome.UnsupportedFormat, DiscPrepOutcome.WrongGame,
            DiscPrepOutcome.WrongVersion, DiscPrepOutcome.ModifiedFile, DiscPrepOutcome.DamagedImage,
        }, o => Assert.Equal(2, Status(o)));
        Assert.Equal(3, Status(DiscPrepOutcome.NotEnoughSpace));
        Assert.Equal(4, Status(DiscPrepOutcome.Cancelled));
        Assert.Equal(5, Status(DiscPrepOutcome.Failed));
        Assert.Equal(6, Status(DiscPrepOutcome.Busy));
        // every outcome there is has been named above: a new one gets its status here
        Assert.Equal(14, Enum.GetValues<DiscPrepOutcome>().Length);
    }
}
