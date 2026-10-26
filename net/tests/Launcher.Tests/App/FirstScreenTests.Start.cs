using Avalonia.Controls;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.Tests;

// The launcher's start, by what the data folder holds of the game.
public sealed partial class FirstScreenTests
{
    [Fact]
    public async Task WithFilesThatAreReadyTheLauncherOpensAtOnce()
    {
        var image = _disc.Write(_dir);
        PrepareWithoutTheLauncher(image, Bundle("bundle"));
        var state = State(stored: image);
        await OnTheLauncher(state, screen =>
        {
            Assert.True(screen.Opened);                                        // the first screen is never shown
            Assert.Null(state.GameProblem);
            Assert.Equal(Game, state.GamePaths.GameFolder);
        });
        Assert.False(_asked.IsSet);                                            // nothing was prepared

        // the disc image itself is still wanted (the launcher's look is read from it): gone, the first screen asks
        File.Delete(image);
        await OnTheLauncher(State(stored: image), screen => Assert.False(screen.Continue.IsEffectivelyEnabled));
    }

    [Fact]
    public async Task FilesOfAnotherOnlinePartAreBroughtUpToDateWithoutAWord()
    {
        var image = _disc.Write(_dir);
        PrepareWithoutTheLauncher(image, Bundle("image-1"));
        var updated = Bundle("image-2", linked: 0x497FD900);                   // the launcher was updated
        var stale = DiscPrep.Status(Data, updated, Pins);
        Assert.Equal((PreparedState.Stale, false), (stale.State, stale.NeedsDisc));

        var state = State(stored: image, bundle: updated);
        await OnTheLauncher(state, screen =>
        {
            Assert.True(screen.Opened);                                        // no first screen, no preparation
            Assert.Equal(PreparedState.Ready, Prepared(state));
            Assert.Equal(0x497FD900u, SyntheticDisc.Word(File.ReadAllBytes(Path.Combine(Game, "sys", "main.dol")), 0x180));
            Assert.Null(state.GameProblem);
        });
        Assert.False(_asked.IsSet);                                            // the disc was not read for it
    }

    [Fact]
    public async Task DamagedFilesBringTheFirstScreenWithTheStoredDiscAndWhatIsWrong()
    {
        var image = _disc.Write(_dir);
        PrepareWithoutTheLauncher(image, Bundle("bundle"));
        File.Delete(Path.Combine(Game, "files", "stage", "st01.gma"));
        var damaged = DiscPrep.Status(Data, Bundle("bundle"), Pins);
        Assert.Equal(PreparedState.Damaged, damaged.State);

        var state = State(stored: image);
        await OnTheLauncher(state, screen =>
        {
            Assert.NotNull(screen.View);
            Assert.False(screen.Main.Navigation);
            Assert.Equal(image, screen.PathBox.Text);                          // the stored path, already there
            Assert.Equal(damaged.Message, screen.Said);
            Assert.Contains("files/stage/st01.gma", screen.Said, StringComparison.Ordinal);
            Assert.Equal("Game files damaged · files/stage/st01.gma", screen.SaidLabel);

            screen.Press(screen.Continue);                                     // one press repairs it
            Until(() => screen.Opened, "the launcher opens on the repaired files");
            Assert.Equal(PreparedState.Ready, Prepared(state));
            Assert.True(File.Exists(Path.Combine(Game, "files", "stage", "st01.gma")));
        });
    }

    [Fact]
    public async Task MissingFilesBringTheFirstScreenWithTheStoredDisc()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: image);                                      // a player of the build before: a disc, no files
        await OnTheLauncher(state, screen =>
        {
            Assert.NotNull(screen.View);
            Assert.Equal(image, screen.PathBox.Text);
            Assert.Null(screen.Said);                                          // nothing is wrong: it was never done
            Assert.True(screen.Continue.IsEffectivelyEnabled);
            Assert.False(screen.Preparing);

            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens");
            Assert.Equal(PreparedState.Ready, Prepared(state));
        });

        // a stored disc that is no longer there: the first screen, with the path and what is wrong with it
        Directory.Delete(Path.Combine(Data, DiscPrep.FolderName), recursive: true);
        File.Delete(image);
        await OnTheLauncher(State(stored: image), screen =>
        {
            Assert.NotNull(screen.View);
            Assert.Equal(image, screen.PathBox.Text);
            Assert.False(screen.Continue.IsEffectivelyEnabled);
        });
    }

    /// <summary>A made-up player copy as the package script lays it out (the libraries' folder with the marker, the
    /// game program and its online part in <c>recomp</c>), and the launcher's state in it: its data in the copy.</summary>
    private (AppState State, string Copy) InAPlayerCopy(string? stored)
    {
        var copy = Path.Combine(_dir, "Some Copy 0.2.0");
        Directory.CreateDirectory(Path.Combine(copy, Install.ProgramFolder));
        File.WriteAllText(Path.Combine(copy, Install.ProgramFolder, Install.MarkerName), "a player copy");
        Directory.CreateDirectory(Path.Combine(copy, "recomp"));
        File.WriteAllText(Path.Combine(copy, "recomp", RecompLaunch.RunnerFileName), "");
        File.WriteAllText(Path.Combine(copy, "recomp", RecompLaunch.ModuleFileName), "");
        Directory.Move(Bundle("bundle"), Path.Combine(copy, "recomp", "fzgx"));
        var install = Install.Of(Path.Combine(copy, Install.ProgramFolder));
        Directory.CreateDirectory(install.DataFolder!);
        File.WriteAllText(Path.Combine(install.DataFolder!, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new { discPath = stored, noServer = true }));
        var state = AppState.Load(null, null, install);
        state.Preparation = Options;
        return (state, copy);
    }

    [Fact]
    public async Task APlayerCopyUnzippedOverAnOlderOneSaysSoAndPreparesItsOwnGame()
    {
        var image = _disc.Write(_dir);
        var (state, copy) = InAPlayerCopy(stored: image);
        // the older copy under it: its launcher, a start file, and its game folder with another online part's words
        File.WriteAllText(Path.Combine(copy, "Launcher.App.exe"), "");
        File.WriteAllText(Path.Combine(copy, "2 Play.cmd"), "");
        Directory.CreateDirectory(Path.Combine(copy, "recomp", "game", "sys"));
        File.WriteAllText(Path.Combine(copy, "recomp", "game", "sys", "main.dol"), "an older build's");
        state = AppState.Load(null, null, state.Where);                        // the start that finds them
        state.Preparation = Options;
        var data = Path.Combine(copy, "data");
        Assert.Equal(data, Path.GetDirectoryName(state.GamePaths.UserFolder));

        await OnTheLauncher(state, screen =>
        {
            Assert.NotNull(screen.View);                                       // not opened on the older game
            var page = (DiscViewModel)screen.View!.DataContext!;
            Assert.Equal(Install.OlderCopyNotice, page.Notice);                // said, where the first screen says notices
            Assert.Contains(screen.View.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == Install.OlderCopyNotice);
            Assert.Equal(NotPrepared, state.GameProblem);
            Assert.Equal(image, screen.PathBox.Text);

            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens on its own game");
            var game = DiscPrep.GameFolder(data, Pins);
            Assert.Equal(game, state.GamePaths.GameFolder);                    // the prepared folder, in the copy's data
            Assert.Equal(PreparedState.Ready, DiscPrep.Status(data, state.GamePaths.Bundle, Pins).State);
            Assert.Null(state.GameProblem);
            Assert.Equal("an older build's", File.ReadAllText(Path.Combine(copy, "recomp", "game", "sys", "main.dol")));   // left alone
        });
    }

    [Fact]
    public async Task ACleanPlayerCopyOpensOnItsPreparedGameAndSaysNothing()
    {
        var image = _disc.Write(_dir);
        var (state, copy) = InAPlayerCopy(stored: image);
        Assert.Equal(DiscPrepOutcome.Prepared, DiscPrep.Prepare(image, Path.Combine(copy, "data"), state.GamePaths.Bundle, null,
            new DiscPrepOptions { Pins = Pins }, TestContext.Current.CancellationToken).Outcome);
        state = AppState.Load(null, null, state.Where);
        state.Preparation = Options;
        Assert.Null(state.Problem);
        await OnTheLauncher(state, screen => Assert.True(screen.Opened));
        Assert.False(_asked.IsSet);
    }
}
