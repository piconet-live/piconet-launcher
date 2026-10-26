using Launcher.App;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The launcher's command line (<see cref="CommandLine"/>): a mode and its files, the data folder, and a
/// launch file for everything that is for tests only. An option it does not list is refused: the test settings were
/// options once, and a script that still passes one must not get the window in its place.</summary>
public sealed class CommandLineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LaunchFile(string debug)
    {
        var file = Path.Combine(_dir, "launch.json");
        File.WriteAllText(file, """{"about": "a test of the command line", "debug": """ + debug + "}");
        return file;
    }

    [Fact]
    public void WithoutAnOptionItIsTheWindowWithEveryTestSettingOff()
    {
        var (line, refusal) = CommandLine.Parse([]);

        Assert.Null(refusal);
        Assert.Equal(StartMode.Window, line!.Mode);
        Assert.Null(line.DataFolder);
        Assert.Same(DebugSettings.Off, line.Debug);
    }

    [Fact]
    public void AModeTakesItsFilesAndTheDataFolder()
    {
        var (offline, _) = CommandLine.Parse(["--data", "d", "--play-offline"]);
        Assert.Equal((StartMode.PlayOffline, "d"), (offline!.Mode, offline.DataFolder));

        var (setup, _) = CommandLine.Parse(["--play-controller-setup", "--data", "d"]);
        Assert.Equal((StartMode.PlayControllerSetup, "d"), (setup!.Mode, setup.DataFolder));
        Assert.Equal((null, "One mode a start: --play-offline and --play-controller-setup were both asked for."),
            CommandLine.Parse(["--play-offline", "--play-controller-setup"]));

        var (match, _) = CommandLine.Parse(["--play-match", "match.json", "--replay", "race.fzrp"]);
        Assert.Equal((StartMode.PlayMatch, "match.json", "race.fzrp"), (match!.Mode, match.File, match.ReplayFile));

        var (replay, _) = CommandLine.Parse(["--play-replay", "race.fzrp"]);
        Assert.Equal((StartMode.PlayReplay, "race.fzrp"), (replay!.Mode, replay.File));
        // a replay plays as the race: the option that chose the retired replay mode has gone, and is refused by name
        Assert.StartsWith("--view is not an option of the launcher.",
            CommandLine.Parse(["--play-replay", "race.fzrp", "--view", "race"]).Refusal, StringComparison.Ordinal);

        Assert.Equal(StartMode.ListReplays, CommandLine.Parse(["--list-replays"]).Line!.Mode);

        var (disc, _) = CommandLine.Parse(["--prepare-disc", "game.iso", "--bundle", "fzgx"]);
        Assert.Equal((StartMode.PrepareDisc, "game.iso", "fzgx"), (disc!.Mode, disc.File, disc.Bundle));

        var (pack, _) = CommandLine.Parse(["--import-stat-pack", "cardata.lz", "--pack-name", "GX Made Up", "--disc", "game.iso", "--out", "p.fzstats"]);
        Assert.Equal((StartMode.ImportStatPack, "cardata.lz", "GX Made Up", "game.iso", "p.fzstats"),
            (pack!.Mode, pack.File, pack.Name, pack.Disc, pack.Out));
        Assert.Equal((false, false, false), (pack.AnyDisc, pack.Overwrite, pack.Report));
        var (knowingly, _) = CommandLine.Parse(["--import-stat-pack", "cardata.lz", "--any-disc", "--overwrite"]);
        Assert.Equal((true, true), (knowingly!.AnyDisc, knowingly.Overwrite));
        Assert.True(CommandLine.Parse(["--import-stat-pack", "cardata.lz", "--report"]).Line!.Report);
        Assert.Equal((null, "--pack-name needs a value."), CommandLine.Parse(["--import-stat-pack", "cardata.lz", "--pack-name"]));
        Assert.Equal((null, "One mode a start: --prepare-disc and --import-stat-pack were both asked for."),
            CommandLine.Parse(["--prepare-disc", "game.iso", "--import-stat-pack", "cardata.lz"]));
    }

    /// <summary>Every option that was the launcher's before the launch file: the test settings, and the four that
    /// nothing set (they are gone).</summary>
    [Theory]
    [InlineData("--quick-play"), InlineData("--races"), InlineData("--start-with"), InlineData("--vote-course")]
    [InlineData("--machine"), InlineData("--watch"), InlineData("--log"), InlineData("--time-limit")]
    [InlineData("--start-limit"), InlineData("--autopilot"), InlineData("--test-lag"), InlineData("--title")]
    [InlineData("--runner-arg"), InlineData("--at"), InlineData("--page"), InlineData("--test-fault")]
    [InlineData("--no-window"), InlineData("--again"), InlineData("--live"), InlineData("--no-replay"), InlineData("--view")]
    [InlineData("match.json")]
    public void AnOptionThatIsNotListedIsRefused(string option)
    {
        var (line, refusal) = CommandLine.Parse(["--data", "d", option, "1"]);
        Assert.Null(line);
        Assert.Equal($"{option} is not an option of the launcher. Test settings are named in a launch file (--config FILE).", refusal);
    }

    [Fact]
    public void WhatCannotBeMeantIsRefused()
    {
        Assert.Equal((null, "--data needs a value."), CommandLine.Parse(["--play-offline", "--data"]));
        Assert.Equal((null, "One mode a start: --play-offline and --list-replays were both asked for."),
            CommandLine.Parse(["--play-offline", "--list-replays"]));
        Assert.Equal((null, "One mode a start: --play-offline and the launch file's quickPlay were both asked for."),
            CommandLine.Parse(["--play-offline", "--config", LaunchFile("""{"quickPlay": {"server": "ws://127.0.0.1:47810/ws"}}""")]));
    }

    [Fact]
    public void ALaunchFileGivesTheStartItsTestSettings()
    {
        var (line, refusal) = CommandLine.Parse(["--config", LaunchFile("""{"title": "Port test", "runnerArguments": ["--mute"]}"""), "--play-offline"]);
        Assert.Null(refusal);
        Assert.Equal(StartMode.PlayOffline, line!.Mode);
        Assert.Equal("Port test", line.Debug.Title);
        Assert.Equal(["--mute"], line.Debug.RunnerArguments);

        // the two modes only a launch file asks for
        var (visit, _) = CommandLine.Parse(["--config", LaunchFile("""{"quickPlay": {"server": "ws://127.0.0.1:47810/ws", "races": 2}, "autopilot": true}""")]);
        Assert.Equal((StartMode.QuickPlay, 2, true), (visit!.Mode, visit.Debug.QuickPlay!.Races, visit.Debug.Autopilot));
        Assert.Equal(StartMode.Fault, CommandLine.Parse(["--config", LaunchFile("""{"fault": "task", "faultWithoutWindow": true}""")]).Line!.Mode);
        // (with the window, the error is raised after it opened)
        Assert.Equal(StartMode.Window, CommandLine.Parse(["--config", LaunchFile("""{"fault": "task"}""")]).Line!.Mode);
    }

    [Fact]
    public void ALaunchFileThatIsRefusedStartsNothing()
    {
        var missing = Path.Combine(_dir, "none.json");
        Assert.Equal((null, $"The launch file is not there: {missing}"), CommandLine.Parse(["--config", missing]));

        var file = LaunchFile("""{"testLag": 50}""");
        Assert.Equal((null, $"The launch file is refused: \"debug.testLag\" is not a setting of a launch file ({file})"),
            CommandLine.Parse(["--play-offline", "--config", file]));
    }

    /// <summary>The test settings reach the state every game start reads, and nothing else carries them: a start
    /// without them has the launcher's own limit for a game that is not racing.</summary>
    [Fact]
    public void TheSharedStateHasTheStartsTestSettingsOrNone()
    {
        var plain = AppState.Load(Path.Combine(_dir, "plain"));
        Assert.Same(DebugSettings.Off, plain.Debug);
        Assert.Equal(TimeSpan.FromSeconds(150), plain.StartLimit);

        var (line, _) = CommandLine.Parse(["--data", Path.Combine(_dir, "test"), "--config", LaunchFile("""{"startLimitSeconds": 60, "testLagMs": 84}""")]);
        var state = AppState.Load(line!.DataFolder, line.Debug);
        Assert.Equal(TimeSpan.FromSeconds(60), state.StartLimit);
        Assert.Equal(84, state.Debug.TestLagMs);
    }
}
