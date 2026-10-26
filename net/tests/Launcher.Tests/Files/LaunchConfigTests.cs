using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The launch file (<see cref="LaunchConfig"/>, the launcher's <c>--config FILE</c>): the one place a
/// start's test settings are named. Without one every test setting is off; a file is read whole or refused with a
/// sentence, and a setting the reader does not know is never passed over.</summary>
public sealed class LaunchConfigTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (LaunchConfig? Config, string? Problem) Parsed(string text) => LaunchConfig.Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ALaunchFileNamesItsTestSettings()
    {
        var (config, problem) = Parsed("""
            {
              "about": "Quick Play test visit, launcher 1",
              "debug": {
                "title": "Quick-Play-test",
                "runnerArguments": ["--mute", "--automation-dir", "C:\\runs\\auto"],
                "windowAt": [40, -20],
                "windowPage": "garage",
                "quickPlay": {"server": "ws://127.0.0.1:47810/ws", "races": 2, "startWith": 3, "voteCourse": 16,
                              "watch": true, "log": "door.jsonl", "timeLimitSeconds": 450},
                "autopilot": true,
                "testLagMs": 50,
                "game": {"rollbackLog": "C:\\runs\\rb.csv", "jitterMs": 8},
                "startLimitSeconds": 60,
                "fault": "task",
                "faultWithoutWindow": true,
                "gpuLog": "gpu",
                "gpuTurn": true,
                "loadTrace": true
              }
            }
            """);

        Assert.Null(problem);
        Assert.Equal("Quick Play test visit, launcher 1", config!.About);
        var debug = config.Debug;
        Assert.Equal("Quick-Play-test", debug.Title);
        Assert.Equal(["--mute", "--automation-dir", @"C:\runs\auto"], debug.RunnerArguments);
        Assert.Equal((40, -20), debug.WindowAt);
        Assert.Equal("garage", debug.WindowPage);
        Assert.Equal(new QuickPlayVisit("ws://127.0.0.1:47810/ws", 2, 3, 16, true, "door.jsonl", 450), debug.QuickPlay);
        Assert.True(debug.Autopilot);
        Assert.Equal(50, debug.TestLagMs);
        Assert.Equal("""{"rollbackLog": "C:\\runs\\rb.csv", "jitterMs": 8}""", debug.Game);
        Assert.Equal(60, debug.StartLimitSeconds);
        Assert.Equal("task", debug.Fault);
        Assert.True(debug.FaultWithoutWindow);
        Assert.Equal("gpu", debug.GpuLog);
        Assert.True(debug.GpuTurn);
        Assert.True(debug.LoadTrace);
    }

    /// <summary>A file with no <c>"debug"</c> part, or an empty one, sets nothing: what a player's start has.</summary>
    [Theory]
    [InlineData("""{"about": "nothing set"}""")]
    [InlineData("""{"about": "nothing set", "debug": {}}""")]
    public void WithoutADebugPartEveryTestSettingIsOff(string text)
    {
        var (config, problem) = Parsed(text);

        Assert.Null(problem);
        var debug = config!.Debug;
        Assert.Null(debug.Title);
        Assert.Empty(debug.RunnerArguments);
        Assert.Null(debug.WindowAt);
        Assert.Null(debug.WindowPage);
        Assert.Null(debug.QuickPlay);
        Assert.False(debug.Autopilot);
        Assert.Equal(0, debug.TestLagMs);
        Assert.Null(debug.Game);
        Assert.Equal(0, debug.StartLimitSeconds);
        Assert.Null(debug.Fault);
        Assert.False(debug.FaultWithoutWindow);
        Assert.Null(debug.GpuLog);
        Assert.False(debug.GpuTurn);
        Assert.False(debug.LoadTrace);
        Assert.Equal(DebugSettings.Off, debug with { RunnerArguments = DebugSettings.Off.RunnerArguments });
    }

    /// <summary>What the launcher tells a server in its hello (<see cref="DebugSettings.ReachesARace"/>): a test
    /// setting is on when the file's <c>"debug"</c> part sets one that is handed on to the game or acts in a race.
    /// The settings of the launcher's own window and checks are none of those, and neither is a setting named with
    /// the value it has anyway.</summary>
    [Theory]
    [InlineData("""{"title": "Quick-Play-test"}""", true)]
    [InlineData("""{"runnerArguments": ["--mute"]}""", true)]
    [InlineData("""{"quickPlay": {"server": "ws://127.0.0.1:47810/ws"}}""", true)]
    [InlineData("""{"autopilot": true}""", true)]
    [InlineData("""{"testLagMs": 50}""", true)]
    [InlineData("""{"game": {"jitterMs": 8}}""", true)]
    [InlineData("""{"startLimitSeconds": 60}""", true)]
    [InlineData("""{"windowAt": [40, -20], "windowPage": "garage", "gpuLog": "gpu", "gpuTurn": true, "autopilot": true}""", true)]
    [InlineData("""{}""", false)]
    [InlineData("""{"windowAt": [40, -20], "windowPage": "garage"}""", false)]
    [InlineData("""{"fault": "task", "faultWithoutWindow": true}""", false)]
    [InlineData("""{"gpuLog": "gpu", "gpuTurn": true}""", false)]
    [InlineData("""{"loadTrace": true}""", false)]
    [InlineData("""{"runnerArguments": [], "autopilot": false, "testLagMs": 0, "game": {}, "startLimitSeconds": 0}""", false)]
    public void ADebugPartSaysWhetherATestSettingReachesARace(string debug, bool reaches)
    {
        var (config, problem) = Parsed("""{"about": "what the hello says", "debug": """ + debug + "}");

        Assert.Null(problem);
        Assert.Equal(reaches, config!.Debug.ReachesARace);
    }

    [Fact]
    public void WithoutALaunchFileNoTestSettingReachesARace()
    {
        Assert.False(DebugSettings.Off.ReachesARace);
        Assert.False(Parsed("""{"about": "nothing set"}""").Config!.Debug.ReachesARace);
    }

    /// <summary>A scripted visit that names only its server keeps the visit's own values for the rest.</summary>
    [Fact]
    public void AVisitThatNamesOnlyItsServerIsOneRace()
    {
        var (config, _) = Parsed("""{"about": "a visit", "debug": {"quickPlay": {"server": "ws://127.0.0.1:47810/ws"}}}""");
        Assert.Equal(new QuickPlayVisit("ws://127.0.0.1:47810/ws", 1, 2, null, false, null, 600), config!.Debug.QuickPlay);
    }

    /// <summary>A scripted visit names its server, or the file is refused: the settings' server is the official
    /// service's in a new data folder, and a test file (with the game's AI on a seat, perhaps) must never reach that
    /// one because a line was left out.</summary>
    [Theory]
    [InlineData("""{"about": "x", "debug": {"quickPlay": {}}}""")]
    [InlineData("""{"about": "x", "debug": {"quickPlay": {"races": 2}, "autopilot": true}}""")]
    [InlineData("""{"about": "x", "debug": {"quickPlay": {"server": ""}}}""")]
    public void AVisitThatDoesNotNameItsServerIsRefused(string text)
    {
        var (config, problem) = Parsed(text);
        Assert.Null(config);
        Assert.Equal("\"quickPlay\" does not name its \"server\": a scripted visit goes where its file says, never to the settings' server", problem);
    }

    [Theory]
    [InlineData("""{"debug": {}}""", "it has no \"about\" line that says what it is for")]
    [InlineData("""{"about": "", "debug": {}}""", "it has no \"about\" line that says what it is for")]
    [InlineData("""{"about": 3}""", "\"about\" is not text")]
    [InlineData("""{"about": "x", "match": {}}""", "\"match\" is not a setting of a launch file")]
    [InlineData("""{"about": "x", "debug": {"autoPilot": true}}""", "\"debug.autoPilot\" is not a setting of a launch file")]
    [InlineData("""{"about": "x", "debug": {"quickPlay": {"machine": 5}}}""", "\"debug.quickPlay.machine\" is not a setting of a launch file")]
    [InlineData("""{"about": "x", "debug": {"autopilot": "yes"}}""", "\"autopilot\" is not true or false")]
    [InlineData("""{"about": "x", "debug": {"testLagMs": -1}}""", "\"testLagMs\" is not a whole number from 0 to 10000")]
    [InlineData("""{"about": "x", "debug": {"testLagMs": 2.5}}""", "\"testLagMs\" is not a whole number from 0 to 10000")]
    [InlineData("""{"about": "x", "debug": {"game": "autopilot"}}""", "\"game\" is not an object")]
    [InlineData("""{"about": "x", "debug": {"game": {"jitterMs": 1, "jitterMs": 2}}}""", "\"game\" names a setting twice")]
    [InlineData("""{"about": "x", "debug": {"quickPlay": {"races": 0}}}""", "\"races\" is not a whole number from 1 to 1000")]
    [InlineData("""{"about": "x", "debug": {"runnerArguments": "--mute"}}""", "\"runnerArguments\" is not a list of texts")]
    [InlineData("""{"about": "x", "debug": {"runnerArguments": ["--mute", 3]}}""", "\"runnerArguments\" is not a list of texts")]
    [InlineData("""{"about": "x", "debug": {"windowAt": "40,20"}}""", "\"windowAt\" is not [X, Y]")]
    [InlineData("""{"about": "x", "debug": {"windowPage": "lobby"}}""", "\"windowPage\" is not one of play, garage, replays, settings")]
    [InlineData("""{"about": "x", "debug": {"fault": "everywhere"}}""", "\"fault\" is not one of window, task, thread, process, first")]
    [InlineData("""{"about": "x", "debug": {"autopilot": true, "autopilot": false}}""", "\"debug\" names a setting twice")]
    [InlineData("""{"about": "x", "debug": []}""", "\"debug\" is not an object")]
    [InlineData("""["about"]""", "the file is not an object")]
    public void AFileThatIsNotALaunchFileIsRefusedWithWhatIsWrong(string text, string wrong)
    {
        var (config, problem) = Parsed(text);
        Assert.Null(config);
        Assert.Equal(wrong, problem);
    }

    /// <summary>Plain JSON: no comment syntax, nothing after the last member; a file's own
    /// words are in its <c>"about"</c> line.</summary>
    [Theory]
    [InlineData("""{"about": "x", /* lag */ "debug": {}}""")]
    [InlineData("{\"about\": \"x\" // what it is for\n}")]
    [InlineData("""{"about": "x", "debug": {},}""")]
    [InlineData("""{"about": "x" """)]
    [InlineData("")]
    public void AFileThatIsNotPlainJsonIsRefused(string text)
    {
        var (config, problem) = Parsed(text);
        Assert.Null(config);
        Assert.StartsWith("it is not plain JSON (", problem, StringComparison.Ordinal);
    }

    /// <summary>A text or a name that is no text (half a surrogate pair, bytes that are not UTF-8) is refused like any
    /// other broken file. The JSON reader takes it and raises only when the text is read; the launcher reads the
    /// file before its handler of last resort is there, so an error here would end it with nothing written.</summary>
    [Theory]
    [InlineData("{\"about\": \"\\ud800\"}", false)]
    [InlineData("{\"about\": \"x\", \"\\udc00\": 1}", false)]
    [InlineData("{\"about\": \"x\", \"debug\": {\"runnerArguments\": [\"\\ud83d\"]}}", false)]
    [InlineData("{\"about\": \"x\", \"debug\": {\"game\": {\"rollbackLog\": \"\\ud800\"}}}", false)]
    [InlineData("{\"about\": \"x\", \"debug\": {\"title\": \"\u00ff\"}}", true)]
    [InlineData("{\"about\": \"x\", \"debug\": {\"tit\u00ffle\": \"x\"}}", true)]
    public void ATextThatIsNoTextIsRefused(string text, bool asLatin1)
    {
        // as Latin-1 the letter is the single byte FF, which no UTF-8 text holds
        var (config, problem) = LaunchConfig.Parse((asLatin1 ? Encoding.Latin1 : Encoding.UTF8).GetBytes(text));
        Assert.Null(config);
        Assert.Equal("a text or a name in it is no text (bytes that are not UTF-8, or half a character)", problem);
    }

    [Fact]
    public void AFileIsReadFromItsPathOrRefusedWithThePath()
    {
        var file = Path.Combine(_dir, "launch.json");
        Assert.Equal((null, $"The launch file is not there: {file}"), LaunchConfig.Read(file));

        File.WriteAllText(file, """{"about": "a lag test", "debug": {"testLagMs": 84}}""");
        var (config, problem) = LaunchConfig.Read(file);
        Assert.Null(problem);
        Assert.Equal(84, config!.Debug.TestLagMs);

        File.WriteAllText(file, """{"about": "a lag test", "debug": {"testLag": 84}}""");
        Assert.Equal((null, $"The launch file is refused: \"debug.testLag\" is not a setting of a launch file ({file})"), LaunchConfig.Read(file));

        // a launch file is a few lines: one that is not is not read at all
        File.WriteAllText(file, "{\"about\": \"" + new string('x', LaunchConfig.MaxBytes) + "\"}");
        Assert.Equal((null, $"The launch file is longer than 64 KiB and was not read: {file}"), LaunchConfig.Read(file));
    }
}
