using Launcher.Core;

namespace Launcher.Tests;

/// <summary>Whether the first screen has to prepare the game's files (<see cref="RecompLaunch.PreparationWanted"/>),
/// by what the launcher's folder and the data folder hold. No disc image and no game file: the "game program" is
/// two empty files.</summary>
public sealed class PreparationWantedTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Launcher => Path.Combine(_dir, "launcher");

    private string Data => Path.Combine(_dir, "data");

    private void Program()
    {
        Directory.CreateDirectory(Path.Combine(Launcher, "recomp"));
        File.WriteAllText(Path.Combine(Launcher, "recomp", RecompLaunch.RunnerFileName), "");
        File.WriteAllText(Path.Combine(Launcher, "recomp", RecompLaunch.ModuleFileName), "");
    }

    [Fact]
    public void AGameProgramWithoutTheGamesFilesWantsThem()
    {
        Program();
        var wanted = RecompLaunch.PreparationWanted(null, Launcher, Data);
        Assert.NotNull(wanted);
        Assert.Equal((PreparedState.NotPrepared, DiscPrep.NotPreparedMessage, true), (wanted.State, wanted.Message, wanted.NeedsDisc));
        Assert.False(Directory.Exists(Data));                                  // asking writes nothing
    }

    [Fact]
    public void ACopyThatCarriesAGameFolderWantsNothing()
    {
        // a development copy and the testers' copies of the first build: recomp\game beside the game program
        Program();
        Directory.CreateDirectory(Path.Combine(Launcher, "recomp", "game", "sys"));
        File.WriteAllText(Path.Combine(Launcher, "recomp", "game", "sys", "main.dol"), "");
        Assert.Null(RecompLaunch.PreparationWanted(null, Launcher, Data));
        Assert.Equal(Path.Combine(Launcher, "recomp", "game"), RecompLaunch.Resolve(null, Launcher, Data).GameFolder);
        Assert.Null(RecompLaunch.Problem(RecompLaunch.Resolve(null, Launcher, Data)));
    }

    [Fact]
    public void APlayerCopyDoesNotTakeAGameFolderItDidNotMake()
    {
        // an older copy's recomp\game, unzipped over or carried across: no record, another online part's words
        Program();
        Directory.CreateDirectory(Path.Combine(Launcher, "recomp", "game", "sys"));
        File.WriteAllText(Path.Combine(Launcher, "recomp", "game", "sys", "main.dol"), "");
        var wanted = RecompLaunch.PreparationWanted(null, Launcher, Data, playerCopy: true);
        Assert.Equal(PreparedState.NotPrepared, wanted?.State);
        var paths = RecompLaunch.Resolve(null, Launcher, Data, playerCopy: true);
        Assert.Equal(DiscPrep.GameFolder(Data), paths.GameFolder);             // where a preparation will put the game
        Assert.Equal(DiscPrep.NotPreparedMessage, RecompLaunch.Problem(paths));
        Assert.Equal(DiscPrep.NotPreparedMessage, RecompLaunch.RaceProblem(paths));
        // the same folders in a launcher that is no player copy: the game, as before (the test above)
        Assert.Null(RecompLaunch.PreparationWanted(null, Launcher, Data));
        // by the copy's marker, as a launcher finds out what it is
        var copy = Path.Combine(_dir, "copy");
        Directory.CreateDirectory(Path.Combine(copy, Install.ProgramFolder));
        File.WriteAllText(Path.Combine(copy, Install.ProgramFolder, Install.MarkerName), "");
        var install = Install.Of(Path.Combine(copy, Install.ProgramFolder));
        Assert.True(RecompLaunch.Resolve(null, install, Data).PlayerCopy);
        Assert.False(RecompLaunch.Resolve(null, Install.Of(Launcher), Data).PlayerCopy);
    }

    [Fact]
    public void APlayerCopyWhosePreparedFolderIsNotReadyHasNoGame()
    {
        // the prepared folder's own place is where a copy's game is: files there that are not ready (damaged, or
        // with another online part's words that cannot be changed) are no game a start may take
        Program();
        var game = DiscPrep.GameFolder(Data);
        Directory.CreateDirectory(Path.Combine(game, "sys"));
        File.WriteAllText(Path.Combine(game, "sys", "main.dol"), "not the game's");
        File.WriteAllText(DiscPrep.RecordFile(Data), "{}");
        Assert.NotEqual(PreparedState.Ready, DiscPrep.Status(Data, Path.Combine(Launcher, "recomp", "fzgx")).State);

        var paths = RecompLaunch.Resolve(null, Launcher, Data, playerCopy: true);
        Assert.True(paths.NoGame);
        Assert.Equal(DiscPrep.NotPreparedMessage, RecompLaunch.Problem(paths));
        Assert.Equal(DiscPrep.NotPreparedMessage, RecompLaunch.PlaybackProblem(paths, Path.Combine(_dir, "x.fzrp")));
        var look = RecompLaunch.Look(null, Launcher, Data, playerCopy: true);
        Assert.NotNull(look.Problem);
        Assert.NotNull(look.RaceProblem);
        Assert.NotNull(look.Wanted);
        Assert.False(RecompLaunch.Resolve(null, Launcher, Data).NoGame);       // no player copy: recomp\game, as before
    }

    [Fact]
    public void WhatIsMissingIsSaidAsItIsTrueForTheKindOfCopy()
    {
        // nothing is installed into a player copy: one without its game program is not whole
        var copy = RecompLaunch.Resolve(null, Launcher, Data, playerCopy: true);
        Assert.Equal("Game program missing · unzip the copy again", RecompLaunch.Problem(copy));
        Assert.Equal("Game program not installed", RecompLaunch.Problem(RecompLaunch.Resolve(null, Launcher, Data)));
        Program();
        Directory.CreateDirectory(Path.Combine(Launcher, "recomp", "game", "sys"));
        File.WriteAllText(Path.Combine(Launcher, "recomp", "game", "sys", "main.dol"), "");
        var settings = new RecompSettings(GameFolder: Path.Combine(Launcher, "recomp", "game"));
        Assert.Equal("Online part missing · unzip the copy again",
            RecompLaunch.RaceProblem(RecompLaunch.Resolve(settings, Launcher, Data, playerCopy: true)));
        Assert.Equal("Online part not installed",
            RecompLaunch.RaceProblem(RecompLaunch.Resolve(settings, Launcher, Data)));
    }

    [Fact]
    public void WithoutAGameProgramNothingIsWanted()
    {
        Assert.Null(RecompLaunch.PreparationWanted(null, Launcher, Data));
        Assert.Equal("Game program not installed", RecompLaunch.Problem(RecompLaunch.Resolve(null, Launcher, Data)));
        Program();
        File.Delete(Path.Combine(Launcher, "recomp", RecompLaunch.ModuleFileName));   // half a game program is none
        Assert.Null(RecompLaunch.PreparationWanted(null, Launcher, Data));
    }

    [Fact]
    public void AGameFolderNamedInTheSettingsWantsNothing()
    {
        Program();
        var settings = new RecompSettings(GameFolder: Path.Combine(_dir, "elsewhere"));   // a prepared folder would not be used
        Assert.Null(RecompLaunch.PreparationWanted(settings, Launcher, Data));
    }
}
