using Launcher.Core;
using TestSupport;

namespace Launcher.Tests.Files;

/// <summary>
/// The launch files the launcher writes, held to the shared samples in protocol/tests/vectors/launch (the
/// description: protocol/launch.toml): each case's text is the sample's, byte for byte. The game's reader takes
/// every sample in its tests (port/core/tests/test_launch.cpp, TestTheSharedLaunchFiles), so a text that changes
/// here changes there. A new case comes with its sample: the text the failing
/// assertion shows, with a line end after it.
/// </summary>
public sealed class LaunchVectorsTests
{
    private const string Match = """
        {"match": {"matchId": "m7", "seat": 1, "token": "1f2e3d4c5b6a7988", "relay": {"host": "", "port": 47811},
                   "setup": {"course": 8, "laps": 3, "seats": [{"seat": 0, "machine": 5, "playerId": "p1"}]}},
         "relayHost": "127.0.0.1", "delay": 2, "overclock": 6, "otherSounds": "quieter"}
        """;

    private static readonly TaskbarGroup Taskbar = new("Piconet.Launcher", @"C:\Piconet\Launcher.App.exe", "Piconet Launcher");

    /// <summary>Every case: the sample's name and the launcher's text.</summary>
    public static IEnumerable<(string Name, string Text)> Cases()
    {
        var debug = GameDebug.From(new DebugSettings { Autopilot = true, TestLagMs = 50, Game = """{"rollbackLog": "rb.csv", "jitterMs": 8}""" });
        var picture = new Picture(2, 1, [0xFF, 0, 0, 0xFF, 1, 2, 3, 0x80]);
        yield return ("offline", GameLaunchFile.Offline(@"C:\game\fzgx"));
        yield return ("offline-without-bundle", GameLaunchFile.Offline(null, GameDebug.Off));
        yield return ("controller-setup", GameLaunchFile.Offline("b", controllerSetup: true));
        yield return ("controller-setup-without-bundle", GameLaunchFile.Offline(null, controllerSetup: true));
        yield return ("offline-debug", GameLaunchFile.Offline("b", new GameDebug(false, 0, """{"alterStep": 1500}""")));
        yield return ("playback-full", GameLaunchFile.Playback("b", "r.fzrp", OtherSounds.Full));
        yield return ("playback-true-widescreen", GameLaunchFile.Playback("b", "C:/data/replays/r.fzrp", OtherSounds.Quieter, WidescreenMode.TrueWide));
        yield return ("playback-narrow-off", GameLaunchFile.Playback("b", "r.fzrp", OtherSounds.Off, WidescreenMode.Off, debug));
        yield return ("race", GameLaunchFile.Race("b", Match, record: "race.fzrp"));
        yield return ("race-narrow-rejoin", GameLaunchFile.Race("b", Match, WidescreenMode.Off, rejoin: true));
        yield return ("race-spectator", GameLaunchFile.Race("b", Match.Replace("\"relayHost\"", "\"spectate\": true, \"relayHost\"", StringComparison.Ordinal),
            WidescreenMode.TrueWide));
        yield return ("race-debug", GameLaunchFile.Race("b", Match, record: "race.fzrp", debug: debug));
        yield return ("practice", GameLaunchFile.Race("b", PracticeMatch.Build(PracticeSetup.Default, "P", null, DateTimeOffset.UnixEpoch),
            record: "practice.fzrp"));
        yield return ("practice-camera", GameLaunchFile.Race("b", PracticeMatch.Build(PracticeSetup.Default, "P", null, DateTimeOffset.UnixEpoch),
            record: "practice.fzrp", window: new GameWindow("F-Zero GX", Camera: 2)));
        yield return ("window-plain", GameLaunchFile.Offline(null, window: new GameWindow("Port test")));
        yield return ("window-every-member", GameLaunchFile.Offline("b", window: new GameWindow("F-Zero GX", "Piconet", true, picture,
            Taskbar, LauncherSettings.PresentAsBefore, true, LauncherSettings.FullScreenExclusive, @"C:\data\boards\game-1.board",
            LauncherSettings.Refresh240, Rumble: false)));
        yield return ("window-race", GameLaunchFile.Race("b", Match, record: "race.fzrp", debug: debug, window: new GameWindow("F-Zero GX",
            "Piconet", false, picture, Taskbar, LauncherSettings.PresentAtTheCopy, false, LauncherSettings.FullScreenBorderless,
            "g.board", LauncherSettings.Refresh120)));
        yield return ("window-playback", GameLaunchFile.Playback("b", "r.fzrp", window: new GameWindow("F-Zero GX", "Piconet",
            RefreshRate: LauncherSettings.RefreshAsTheDisplay, FullScreen: LauncherSettings.FullScreenDefault)));
        // what the game would refuse, or what is its default, is left out: the name of spaces, a picture too large
        // or not its size, a taskbar group not of this computer, the console's moment, 60 a second, no full screen
        yield return ("window-left-out", GameLaunchFile.Offline(null, window: new GameWindow("t", "  ", true,
            new Picture(513, 1, new byte[513 * 4]), Taskbar with { Program = @"\\server\Launcher.App.exe" },
            LauncherSettings.PresentAsAConsole, false, "window", null, LauncherSettings.RefreshAsTheGame)));
        yield return ("window-picture-not-its-size", GameLaunchFile.Offline(null, window: new GameWindow("t", "Piconet",
            NamePicture: new Picture(2, 2, new byte[4]))));
    }

    private static string Sample(string name) => RepositoryFolder.Protocol("protocol", "tests", "vectors", "launch", name + ".json");

    [Fact]
    public void EachLaunchFileIsItsSampleByteForByte()
    {
        var cases = Cases().ToList();
        // the indented JSON's lines end as the system's do; a sample's end in "\n"
        foreach (var (name, text) in cases)
        {
            var wanted = text.ReplaceLineEndings("\n") + "\n";
            Assert.Equal(File.ReadAllText(Sample(name)), wanted);
        }
        // and no sample without its case, so that the game's tests read what the launcher writes and nothing else
        Assert.Equal(cases.Select(c => c.Name + ".json").Order(StringComparer.Ordinal),
            Directory.GetFiles(Path.GetDirectoryName(Sample("x"))!, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
