using System.Text.Json.Nodes;
using Launcher.Core;

namespace Launcher.Tests.Files;

/// <summary>
/// The game's launch file as the launcher writes it (protocol/launch.toml; formats.md section 2). The files the
/// game's reader is held to are the shared samples (LaunchVectorsTests, protocol/tests/vectors/launch).
/// </summary>
public sealed class GameLaunchFileTests
{
    private const string Match = """
        {"match": {"matchId": "m7", "seat": 1, "token": "1f2e3d4c5b6a7988", "relay": {"host": "", "port": 47811},
                   "setup": {"course": 8, "laps": 3, "seats": [{"seat": 0, "machine": 5, "playerId": "p1"}]}},
         "relayHost": "127.0.0.1", "delay": 2, "overclock": 6, "otherSounds": "quieter"}
        """;

    private static string Flat(string text) => JsonNode.Parse(text)!.ToJsonString();

    [Fact]
    public void APlayersStartHasNoDebugPart()
    {
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"C:\\game\\fzgx","lanes":0}""",
            Flat(GameLaunchFile.Offline(@"C:\game\fzgx")));
        Assert.Equal("""{"about":"Piconet Launcher: offline play","lanes":0}""", Flat(GameLaunchFile.Offline(null, GameDebug.Off)));
        // the Controller page's "Set up in the game": offline play's start with the game's key for its controller
        // options, which is a player's key (at the file's own level; the game refuses it among the test settings)
        Assert.Equal("""{"about":"Piconet Launcher: the game\u0027s controller options","bundle":"b","lanes":0,"controllerSetup":true}""",
            Flat(GameLaunchFile.Offline("b", controllerSetup: true)));
        Assert.Equal("""{"about":"Piconet Launcher: the game\u0027s controller options","lanes":0,"controllerSetup":true}""",
            Flat(GameLaunchFile.Offline(null, GameDebug.Off, controllerSetup: true)));
        Assert.Equal(
            """{"about":"Piconet Launcher: a replay\u0027s playback","bundle":"b","widescreen":true,"lanes":0,"replay":{"file":"r.fzrp","otherSounds":"full"}}""",
            Flat(GameLaunchFile.Playback("b", "r.fzrp", OtherSounds.Full)));
        var race = JsonNode.Parse(GameLaunchFile.Race("b", Match, record: "race.fzrp", window: new GameWindow("t", Board: "g.board")))!.AsObject();
        Assert.Equal(["about", "bundle", "widescreen", "lanes", "match", "showFrameRate", "board"], race.Select(m => m.Key));
        Assert.Equal(0, (int)race["lanes"]!);
        Assert.Equal("Piconet Launcher: a race (match m7)", (string?)race["about"]);
        Assert.Equal(["match", "relayHost", "delay", "overclock", "otherSounds", "record"], race["match"]!.AsObject().Select(m => m.Key));
        Assert.Null(GameDebug.Off.Part());
        Assert.Null(GameDebug.From(DebugSettings.Off).Part());
    }

    [Fact]
    public void EachKindOfStartHasTheLanesOff()
    {
        // every start with the lanes off: a race (online, a rejoin, a
        // spectator's), a practice, a replay's playback and offline play; in a live race a lane met a jump-table
        // target with no native entry after the player's finish and the game ended (exit 86), and a replay's jumps
        // and a practice's rewinds had met such code before (crash-233532)
        static int LanesOf(string file) => (int)JsonNode.Parse(file)!["lanes"]!;
        Assert.Equal(0, LanesOf(GameLaunchFile.Race("b", Match)));
        Assert.Equal(0, LanesOf(GameLaunchFile.Race("b", Match, rejoin: true)));
        Assert.Equal(0, LanesOf(GameLaunchFile.Race("b", Match.Replace("\"relayHost\"", "\"spectate\": true, \"relayHost\"", StringComparison.Ordinal))));
        Assert.Equal(0, LanesOf(GameLaunchFile.Playback("b", "r.fzrp")));
        var practice = PracticeMatch.Build(PracticeSetup.Default, "P", null, DateTimeOffset.UnixEpoch);
        Assert.Equal(0, LanesOf(GameLaunchFile.Race("b", practice, record: "practice.fzrp")));
        Assert.Equal(0, LanesOf(GameLaunchFile.Race("b", """{"match": {}, "practice": false}""")));
        Assert.Equal(0, LanesOf(GameLaunchFile.Offline("b")));
        Assert.Equal(0, LanesOf(GameLaunchFile.Offline(null, controllerSetup: true)));
        // a test's own settings are the debug part's, which a launcher hands on whatever the kind
        var debug = GameDebug.From(new DebugSettings { Game = """{"partialRollback": {"mode": "check"}}""" });
        Assert.Equal("check", (string)JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", debug: debug))!["debug"]!["partialRollback"]!["mode"]!);
    }

    [Fact]
    public void ALaunchFilesTestSettingsAreHandedOnAsTheGamesDebugPart()
    {
        var debug = GameDebug.From(new DebugSettings { Autopilot = true, TestLagMs = 50, Game = """{"rollbackLog": "rb.csv", "jitterMs": 8}""" });
        Assert.Equal("""{"rollbackLog":"rb.csv","jitterMs":8,"autopilot":true,"testLagMs":50}""", debug.Part()!.ToJsonString());
        foreach (var file in new[] { GameLaunchFile.Offline("b", debug), GameLaunchFile.Race("b", Match, debug: debug),
                     GameLaunchFile.Playback("b", "r.fzrp", debug: debug) })
        {
            Assert.Equal(debug.Part()!.ToJsonString(), JsonNode.Parse(file)!["debug"]!.ToJsonString());
        }
        // only what is on is named: a lag of 0 and autopilot off leave the game's file without the members
        Assert.Equal("""{"alterStep":1500}""", new GameDebug(false, 0, """{"alterStep": 1500}""").Part()!.ToJsonString());
        Assert.Equal("""{"testLagMs":84}""", new GameDebug(TestLagMs: 84).Part()!.ToJsonString());
    }

    [Fact]
    public void AMatchFileThatIsNoJsonObjectIsSaid()
    {
        Assert.Throws<FormatException>(() => GameLaunchFile.Race("b", "[1, 2]"));
        Assert.Throws<FormatException>(() => GameLaunchFile.Race("b", "{\"match\": "));
        // a match file somebody wrote goes on as it is: the game's reader is the judge of its members
        var odd = JsonNode.Parse(GameLaunchFile.Race("b", """{"match": {}, "autopilot": true}"""))!;
        Assert.True((bool)odd["match"]!["autopilot"]!);
        Assert.Equal("Piconet Launcher: a race (match ?)", (string?)odd["about"]);
        // half a character in a text, and a member named twice, are said too: the JSON reader raises on them only
        // when the text is read or the object written, and with another kind of error
        Assert.Throws<FormatException>(() => GameLaunchFile.Race("b", "{\"match\": {}, \"relayHost\": \"\\ud800\"}"));
        Assert.Throws<FormatException>(() => GameLaunchFile.Race("b", "{\"match\": {\"matchId\": \"\\udc00\"}}"));
        Assert.Throws<FormatException>(() => GameLaunchFile.Race("b", """{"match": {}, "delay": 2, "delay": 3}"""));
    }

    [Fact]
    public void TheFileIsWrittenIntoTheGamesUserFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "game-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new RecompPaths("r.exe", "m.dll", "game", Path.Combine(folder, "user"), "b");
            var path = GameLaunchFile.Write(paths, GameLaunchFile.Offline("b"));
            Assert.Equal(Path.Combine(folder, "user", "launch.json"), path);
            Assert.Equal(GameLaunchFile.PathFor(paths), path);
            Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0}""", Flat(File.ReadAllText(path)));
            // a match file is read whole or not at all
            var match = Path.Combine(folder, "match.json");
            File.WriteAllText(match, Match);
            Assert.Equal(Match, GameLaunchFile.MatchFileText(match));
            File.WriteAllText(match, Match + new string(' ', 1 << 20));
            Assert.Throws<FormatException>(() => GameLaunchFile.MatchFileText(match));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
