using System.Text.Json.Nodes;
using Launcher.Core;

namespace Launcher.Tests.Files;

/// <summary>
/// The game's window as the launcher asks for it: the name for the strip at its top and the player's "show the
/// frame rate" setting, which reach the game as two members of its launch file (protocol/formats.md section
/// 2). The texts pinned here are read by the game's own reader in its tests (port/core/tests/test_launch.cpp,
/// TestALaunchFileOfTheLaunchers): when one changes here, it changes there.
/// </summary>
public sealed class GameWindowTests : IDisposable
{
    private const string Match = """{"match": {"matchId": "m7"}, "relayHost": "127.0.0.1"}""";

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Flat(string text) => JsonNode.Parse(text)!.ToJsonString();

    [Fact]
    public void TheFrameRateIsOffUntilThePlayerAsksAndIsKeptWithTheSettings()
    {
        Assert.False(new LauncherSettings().ShowFrameRate);
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        store.Save(new LauncherSettings { ShowFrameRate = true });
        Assert.True(new SettingsStore(file).Load().ShowFrameRate);
        Assert.Contains("\"showFrameRate\": true", File.ReadAllText(file), StringComparison.Ordinal);
        // settings written before the setting existed: off
        File.WriteAllText(file, """{"widescreen": false}""");
        var old = new SettingsStore(file).Load();
        Assert.False(old.ShowFrameRate);
        Assert.Equal(WidescreenMode.Off, old.Widescreen);
    }

    /// <summary>Rumble can be switched off in the settings, on by default. The setting is kept
    /// in settings.json (a file from before it: on) and reaches the game as "rumble": false only when off: on is the
    /// game's default, and a runner from before the key (0.3.0) refuses a launch file with it.</summary>
    [Fact]
    public void RumbleIsOnUntilThePlayerSwitchesItOffAndOnlyOffReachesTheLaunchFile()
    {
        Assert.True(new LauncherSettings().Rumble);
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        store.Save(new LauncherSettings { Rumble = false });
        Assert.False(new SettingsStore(file).Load().Rumble);
        Assert.Contains("\"rumble\": false", File.ReadAllText(file), StringComparison.Ordinal);
        File.WriteAllText(file, """{"widescreen": false}""");
        Assert.True(new SettingsStore(file).Load().Rumble);
        var off = new GameWindow("F-Zero GX", Rumble: false);
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"showFrameRate":false,"rumble":false}""",
            Flat(GameLaunchFile.Offline("b", window: off)));
        Assert.False((bool)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: off))!["rumble"]!);
        Assert.False((bool)JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: off))!["rumble"]!);
        var on = new GameWindow("F-Zero GX");
        Assert.Null(JsonNode.Parse(GameLaunchFile.Race("b", Match, window: on))!["rumble"]);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: on))!["rumble"]);
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"showFrameRate":false}""",
            Flat(GameLaunchFile.Offline("b", window: on)));
    }

    /// <summary>The player's last chase camera view is kept with the settings and reaches a race's launch file as
    /// <c>match.camera</c>: never a playback's or an offline start's, never a view the game does not take.</summary>
    [Fact]
    public void TheLastCameraViewStartsTheNextRaceOnly()
    {
        Assert.Equal(0, new LauncherSettings().StartCamera);
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        store.Save(new LauncherSettings { RaceCamera = 3 });
        Assert.Equal(3, new SettingsStore(file).Load().StartCamera);
        File.WriteAllText(file, """{"raceCamera": 4}""");
        Assert.Equal(4, new SettingsStore(file).Load().RaceCamera);
        File.WriteAllText(file, """{"raceCamera": 7}""");
        Assert.Equal(0, new SettingsStore(file).Load().RaceCamera);
        var three = new GameWindow("F-Zero GX", Camera: 3);
        Assert.Equal(3, (int)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: three))!["match"]!["camera"]!);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: three))!["replay"]!["camera"]);
        Assert.DoesNotContain("camera", GameLaunchFile.Offline("b", window: three), StringComparison.Ordinal);
        Assert.Equal(4, (int)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: new GameWindow("F-Zero GX", Camera: 4)))!["match"]!["camera"]!);   // first person
        foreach (var none in new[] { 0, 5, -1 })
        {
            Assert.Null(JsonNode.Parse(GameLaunchFile.Race("b", Match, window: new GameWindow("F-Zero GX", Camera: none)))!["match"]!["camera"]);
        }
    }

    /// <summary>The picture is one setting of three choices; the on/off it replaced carries over (true: the game's
    /// 16:9, false: Off), and a value that is no choice reads as the default, the game's 16:9.</summary>
    [Theory]
    [InlineData("true", WidescreenMode.Game)]
    [InlineData("false", WidescreenMode.Off)]
    [InlineData("\"off\"", WidescreenMode.Off)]
    [InlineData("\"game\"", WidescreenMode.Game)]
    [InlineData("\"trueWide\"", WidescreenMode.TrueWide)]
    [InlineData("\"square\"", WidescreenMode.Game)]
    [InlineData("3", WidescreenMode.Game)]
    public void TheWidescreenChoiceIsReadFromEitherKindOfFile(string written, WidescreenMode read)
    {
        Assert.Equal(WidescreenMode.Game, new LauncherSettings().Widescreen);
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{\"widescreen\": " + written + "}");
        var store = new SettingsStore(file);
        Assert.Equal(read, store.Load().Widescreen);
        store.Save(new LauncherSettings { Widescreen = WidescreenMode.TrueWide });
        Assert.Contains("\"widescreen\": \"trueWide\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(WidescreenMode.TrueWide, new SettingsStore(file).Load().Widescreen);
    }

    /// <summary>The frame rate is shown in all tests. A test build (a review or try copy:
    /// stamped anything but "public") and a test game show it whatever the setting says; the players' build and a
    /// build that names no stack follow the setting.</summary>
    [Fact]
    public void ATestBuildAndATestGameAlwaysShowTheFrameRate()
    {
        const string server = "wss://example.test/ws";
        var (test, review, players, unstamped) = (StackStamp.From("test", server, "test"), StackStamp.From("test", server, null),
            StackStamp.From("live", server, "public"), StackStamp.From(null, null, null));
        var (off, on) = (new LauncherSettings(), new LauncherSettings { ShowFrameRate = true });
        Assert.True(off.FrameRateShown(test, testGame: false));
        Assert.True(off.FrameRateShown(review, testGame: false));
        Assert.False(off.FrameRateShown(players, testGame: false));
        Assert.False(off.FrameRateShown(unstamped, testGame: false));
        Assert.True(off.FrameRateShown(players, testGame: true));
        Assert.True(off.FrameRateShown(unstamped, testGame: true));
        Assert.True(on.FrameRateShown(players, testGame: false));
        // what the launcher writes for a test build's game, with the player's setting off
        var window = new GameWindow("F-Zero GX", "Piconet", off.FrameRateShown(test, testGame: false));
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":true}""",
            Flat(GameLaunchFile.Offline("b", window: window)));
    }

    [Fact]
    public void TheLaunchFileNamesTheStripsNameAndTheFrameRate()
    {
        var window = new GameWindow("F-Zero GX", "Piconet", ShowFrameRate: true);
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":true}""",
            Flat(GameLaunchFile.Offline("b", window: window)));
        Assert.Equal(
            """{"about":"Piconet Launcher: a replay\u0027s playback","bundle":"b","widescreen":true,"lanes":0,"replay":{"file":"r.fzrp","otherSounds":"quieter"},"windowName":"Piconet","showFrameRate":false}""",
            Flat(GameLaunchFile.Playback("b", "r.fzrp", window: window with { ShowFrameRate = false })));
        var race = JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window, debug: new GameDebug(Autopilot: true)))!.AsObject();
        Assert.Equal(["about", "bundle", "widescreen", "lanes", "match", "windowName", "showFrameRate", "debug"], race.Select(m => m.Key));
        // the title is the runner's argument, never a member of the file; a window without a name names none
        Assert.DoesNotContain("F-Zero GX", GameLaunchFile.Offline("b", window: window), StringComparison.Ordinal);
        Assert.Equal("""{"about":"Piconet Launcher: offline play","lanes":0,"showFrameRate":false}""",
            Flat(GameLaunchFile.Offline(null, window: new GameWindow("Port test"))));
        Assert.Equal("""{"about":"Piconet Launcher: offline play","lanes":0,"showFrameRate":true}""",
            Flat(GameLaunchFile.Offline(null, window: new GameWindow("t", "  ", true))));
    }

    /// <summary>The input delay and the way of showing pictures, kept with the settings: 2 and a console's moment
    /// unless the file says otherwise; an input delay that is none of 0, 1 and 2 reads as 2 (it is never more than
    /// 2), and a way that is none of the three reads as "console". The way reaches the game as a member of its
    /// launch file only when it is one of the two kept for comparison: the game's own way is a console's.</summary>
    [Fact]
    public void TheDelayIsThePlayersAndNeverOverTwoAndPicturesGoOutAtAConsolesMoment()
    {
        var plain = new LauncherSettings();
        Assert.Equal((2, 2, "console", null, false), (plain.InputDelay, plain.RaceInputDelay, plain.Present, plain.GamePresent, plain.VerticalSync));
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        foreach (var (written, read) in new[] { (0, 0), (1, 1), (2, 2), (3, 2), (60, 2), (-1, 2) })
        {
            File.WriteAllText(file, $$"""{"inputDelay": {{written}}, "present": "field"}""");
            var settings = store.Load();
            Assert.Equal((read, read, "field", "field"), (settings.InputDelay, settings.RaceInputDelay, settings.Present, settings.GamePresent));
        }
        foreach (var (written, read, game) in new[] { ("copy", "copy", "copy"), ("field", "field", "field"), ("console", "console", null), ("early", "console", null), ("", "console", null), ("Console", "console", null) })
        {
            File.WriteAllText(file, $$"""{"present": "{{written}}"}""");
            Assert.Equal((read, game), (store.Load().Present, store.Load().GamePresent));
        }
        // settings made in code and never checked: the race's delay and the game's way are held all the same
        Assert.Equal(2, new LauncherSettings { InputDelay = 7 }.RaceInputDelay);
        Assert.Null(new LauncherSettings { Present = "sooner" }.GamePresent);
        // a file from before the settings: 2, a console's moment, no vertical sync
        File.WriteAllText(file, """{"widescreen": false}""");
        Assert.Equal((2, "console", false), (store.Load().InputDelay, store.Load().Present, store.Load().VerticalSync));
        store.Save(new LauncherSettings { InputDelay = 1, Present = "field", VerticalSync = true });
        Assert.Contains("\"inputDelay\": 1", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Contains("\"present\": \"field\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Contains("\"verticalSync\": true", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal((1, "field", true), (store.Load().InputDelay, store.Load().Present, store.Load().VerticalSync));

        // the launch file: nothing of either for a player's game; the two other ways by name; vertical sync when on
        var window = new GameWindow("F-Zero GX", "Piconet");
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":false}""",
            Flat(GameLaunchFile.Offline("b", window: window)));
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":false,"present":"field"}""",
            Flat(GameLaunchFile.Offline("b", window: window with { Present = "field" })));
        Assert.Equal("copy", (string)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { Present = "copy" }))!["present"]!);
        Assert.Equal("field", (string)JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: window with { Present = "field" }))!["present"]!);
        foreach (var none in new[] { null, "console", "early" })
        {
            Assert.Null(JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { Present = none }))!["present"]);
        }
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":false,"verticalSync":true}""",
            Flat(GameLaunchFile.Offline("b", window: window with { VerticalSync = true })));
        Assert.True((bool)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { VerticalSync = true }))!["verticalSync"]!);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: window))!["verticalSync"]);
    }

    /// <summary>The refresh rate (frames drawn between two steps): "60" for every player, the three others kept by
    /// name, anything else read as "60", and written into the launch file only when frames are drawn.</summary>
    [Fact]
    public void TheRefreshRateIsKeptByNameAndGoesIntoTheLaunchFileWhenFramesAreDrawn()
    {
        Assert.Equal(("60", null), (new LauncherSettings().RefreshRate, new LauncherSettings().GameRefreshRate));
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        foreach (var (written, read, game) in new[]
                 {
                     ("\"120\"", "120", "120"), ("\"240\"", "240", "240"), ("\"display\"", "display", "display"),
                     ("\"60\"", "60", null), ("\"144\"", "60", null), ("120", "60", null), ("\"Display\"", "60", null),
                 })
        {
            File.WriteAllText(file, $$"""{"refreshRate": {{written}}, "gameVolume": 35}""");
            var settings = store.Load();
            Assert.Equal((read, game, 35), (settings.RefreshRate, settings.GameRefreshRate, settings.GameVolume));
        }
        var window = new GameWindow("F-Zero GX", "Piconet");
        Assert.Null(JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window))!["refreshRate"]);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { RefreshRate = "60" }))!["refreshRate"]);
        Assert.Equal("240", (string)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { RefreshRate = "240" }))!["refreshRate"]!);
        Assert.Equal("display", (string)JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: window with { RefreshRate = "display" }))!["refreshRate"]!);
    }

    /// <summary>The kind of full screen: one default in one place (<see cref="LauncherSettings.FullScreenDefault"/>,
    /// Borderless), kept with the settings, anything that is neither kind read as the
    /// default, and written into every launch file the launcher makes for a start (the game's own reading of a
    /// file without it is the borderless window).</summary>
    [Fact]
    public void TheKindOfFullScreenHasOneDefaultAndGoesIntoTheLaunchFile()
    {
        Assert.Equal("borderless", LauncherSettings.FullScreenDefault);
        var plain = new LauncherSettings();
        Assert.Equal((LauncherSettings.FullScreenDefault, LauncherSettings.FullScreenDefault), (plain.FullScreen, plain.GameFullScreen));
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        foreach (var (written, read) in new[]
                 {
                     ("\"exclusive\"", "exclusive"), ("\"borderless\"", "borderless"), ("\"Exclusive\"", LauncherSettings.FullScreenDefault),
                     ("\"\"", LauncherSettings.FullScreenDefault), ("true", LauncherSettings.FullScreenDefault), ("1", LauncherSettings.FullScreenDefault),
                     ("[\"exclusive\"]", LauncherSettings.FullScreenDefault), ("null", LauncherSettings.FullScreenDefault),
                 })
        {
            File.WriteAllText(file, $$"""{"fullScreen": {{written}}, "gameVolume": 35}""");
            var settings = store.Load();
            Assert.Equal((read, read, 35), (settings.FullScreen, settings.GameFullScreen, settings.GameVolume));
        }
        File.WriteAllText(file, """{"widescreen": false}""");
        Assert.Equal(LauncherSettings.FullScreenDefault, store.Load().FullScreen);
        store.Save(new LauncherSettings { FullScreen = "exclusive" });
        Assert.Contains("\"fullScreen\": \"exclusive\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal("exclusive", store.Load().FullScreen);
        Assert.Equal(LauncherSettings.FullScreenDefault, new LauncherSettings { FullScreen = "windowed" }.GameFullScreen);

        var window = new GameWindow("F-Zero GX", "Piconet");
        Assert.Null(JsonNode.Parse(GameLaunchFile.Offline("b", window: window))!["fullScreen"]);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Offline("b", window: window with { FullScreen = "windowed" }))!["fullScreen"]);
        Assert.Equal("""{"about":"Piconet Launcher: offline play","bundle":"b","lanes":0,"windowName":"Piconet","showFrameRate":false,"fullScreen":"exclusive"}""",
            Flat(GameLaunchFile.Offline("b", window: window with { FullScreen = "exclusive" })));
        Assert.Equal("borderless", (string)JsonNode.Parse(GameLaunchFile.Race("b", Match, window: window with { FullScreen = "borderless" }))!["fullScreen"]!);
        Assert.Equal("exclusive", (string)JsonNode.Parse(GameLaunchFile.Playback("b", "r.fzrp", window: window with { FullScreen = "exclusive" }))!["fullScreen"]!);
    }

    /// <summary>A value of the wrong kind in one of the three settings a tester wrote by hand (a number in quotation
    /// marks, a word for true, a list) costs that one setting: it reads as its default, every other setting of the
    /// file stays as written, the server among them, and the file as it stood is kept beside the settings. (Read by
    /// the library's own converters, one such value made the whole file unreadable: every setting back to its
    /// default and no server.) Text that is no JSON, a missing comma, is still the whole file's loss.</summary>
    [Fact]
    public void AWrongKindOfValueInOneOfTheseSettingsCostsOnlyThatSetting()
    {
        var file = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(file);
        const string Rest = """ "widescreen": false, "gameVolume": 35, "server": "ws://192.168.1.20:47810/ws", "racer": {"name": "ANA", "machine": 7, "color": {"r": 1, "g": 2, "b": 3}}""";
        foreach (var (bad, delay, present, sync) in new[]
                 {
                     ("\"inputDelay\": \"0\", \"present\": \"field\", \"verticalSync\": true", 2, "field", true),
                     ("\"inputDelay\": 0.5, \"present\": \"field\", \"verticalSync\": true", 2, "field", true),
                     ("\"inputDelay\": [0], \"present\": \"copy\", \"verticalSync\": false", 2, "copy", false),
                     ("\"inputDelay\": {\"frames\": 0}, \"present\": \"field\", \"verticalSync\": true", 2, "field", true),
                     ("\"inputDelay\": null, \"present\": \"field\", \"verticalSync\": true", 2, "field", true),
                     ("\"inputDelay\": 99999999999, \"present\": \"field\", \"verticalSync\": true", 2, "field", true),
                     ("\"inputDelay\": 1, \"present\": 1, \"verticalSync\": true", 1, "console", true),
                     ("\"inputDelay\": 1, \"present\": [\"field\"], \"verticalSync\": true", 1, "console", true),
                     ("\"inputDelay\": 1, \"present\": null, \"verticalSync\": true", 1, "console", true),
                     ("\"inputDelay\": 0, \"present\": \"field\", \"verticalSync\": \"on\"", 0, "field", false),
                     ("\"inputDelay\": 0, \"present\": \"field\", \"verticalSync\": 1", 0, "field", false),
                     ("\"inputDelay\": 0, \"present\": \"field\", \"verticalSync\": {\"on\": true}", 0, "field", false),
                     ("\"inputDelay\": true, \"present\": false, \"verticalSync\": \"yes\"", 2, "console", false),
                 })
        {
            foreach (var kept in Directory.GetFiles(_dir, "settings.json.damaged*"))
            {
                File.Delete(kept);
            }
            var text = "{" + bad + "," + Rest + "}";
            File.WriteAllText(file, text);
            var settings = store.Load();
            Assert.Equal((delay, present, sync), (settings.InputDelay, settings.Present, settings.VerticalSync));
            // the rest of the file as written, and its server
            Assert.Equal((WidescreenMode.Off, 35, "ws://192.168.1.20:47810/ws", false, "ANA", 7),
                (settings.Widescreen, settings.GameVolume, settings.Server, settings.NoServer, settings.Racer.Name, settings.Racer.Machine));
            Assert.NotEqual(SettingsRead.Unreadable, store.LastRead);
        }
        // a missing comma is no JSON: the whole file's loss, as ever, with the file kept beside
        File.WriteAllText(file, "{\"inputDelay\": 0 \"present\": \"field\"," + Rest + "}");
        var lost = store.Load();
        Assert.Equal((SettingsRead.Unreadable, true, 2, "console"), (store.LastRead, lost.NoServer, lost.InputDelay, lost.Present));
        Assert.True(File.Exists(store.DamagedPath));
    }
}
