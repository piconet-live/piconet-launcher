using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

public sealed class RecompLaunchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "recomp-launch-" + Guid.NewGuid().ToString("N"));

    public RecompLaunchTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheGameIsLookedForBesideTheLauncherUnlessTheSettingsSay()
    {
        var paths = RecompLaunch.Resolve(null, "C:/launcher", "C:/data");
        Assert.Equal(Path.Combine("C:/launcher", "recomp", "moderngekko-run.exe"), paths.Runner);
        Assert.Equal(Path.Combine("C:/launcher", "recomp", "gGFZE01_recomp.dll"), paths.Module);
        Assert.Equal(Path.Combine("C:/launcher", "recomp", "game"), paths.GameFolder);
        Assert.Equal(Path.Combine("C:/data", "recomp-user"), paths.UserFolder);
        Assert.Equal(Path.Combine("C:/launcher", "recomp", "fzgx"), paths.Bundle);

        var own = RecompLaunch.Resolve(new RecompSettings(Runner: "D:/mg/run.exe", GameFolder: "D:/gx"), "C:/launcher", "C:/data");
        Assert.Equal("D:/mg/run.exe", own.Runner);
        Assert.Equal("D:/gx", own.GameFolder);
        Assert.Equal(paths.Module, own.Module);
        Assert.Equal(paths.UserFolder, own.UserFolder);
    }

    [Fact]
    public void WhatIsMissingIsSaidBeforeAnythingStarts()
    {
        var paths = new RecompPaths(Path.Combine(_folder, "run.exe"), Path.Combine(_folder, "module.dll"),
            Path.Combine(_folder, "game"), Path.Combine(_folder, "user"));
        Assert.Equal("Game program not installed", RecompLaunch.Problem(paths));
        Assert.Equal(HostCpu.NotSupported, RecompLaunch.Problem(paths, processorRunsTheGame: false));   // first
        File.WriteAllText(paths.Runner, "");
        Assert.Equal("Game program not installed", RecompLaunch.Problem(paths));   // no module
        File.WriteAllText(paths.Module, "");
        Assert.Equal("Game files not prepared", RecompLaunch.Problem(paths));
        Directory.CreateDirectory(Path.Combine(paths.GameFolder, "sys"));
        File.WriteAllText(Path.Combine(paths.GameFolder, "sys", "main.dol"), "");
        Assert.Null(RecompLaunch.Problem(paths));
        Assert.Equal(HostCpu.NotSupported, RecompLaunch.Problem(paths, processorRunsTheGame: false));
    }

    [Fact]
    public void OfflinePlayIsAPlainStartInTheGamesOwnFolder()
    {
        var paths = new RecompPaths("C:/r/moderngekko-run.exe", "C:/r/gGFZE01_recomp.dll", "C:/r/game", "C:/data/recomp-user");
        var args = RecompLaunch.Arguments(paths, "F-Zero GX", "C:/data/recomp-user/launch.json");
        Assert.Equal(
            ["--game", "C:/r/game", "--module", "C:/r/gGFZE01_recomp.dll", "--user-dir", "C:/data/recomp-user", "--no-mods",
                "--title", "F-Zero GX", "--fzgx-log", Path.Combine("C:/data/recomp-user", "runner.log"),
                "--config", "C:/data/recomp-user/launch.json"],
            args);
        // nothing that belongs to test runs: sound on, the player's controller, no saved state
        Assert.DoesNotContain("--mute", args);
        Assert.DoesNotContain("--automation-dir", args);
        Assert.DoesNotContain("--load-state", args);
        Assert.Equal(["--mute"], RecompLaunch.Arguments(paths, "t", "launch.json", ["--mute"]).TakeLast(1));
        // every setting of the start is in the launch file: the runner is given no option of the game's but its log
        Assert.DoesNotContain(args, a => a.StartsWith("--fzgx-", StringComparison.Ordinal) && a != "--fzgx-log");

        // the launch file of a plain start: no race, no replay, no test setting; the lanes off, as in every start
        var plain = JsonNode.Parse(RecompLaunch.OfflineLaunch(paths))!.AsObject();
        Assert.Equal(["about", "lanes"], plain.Select(m => m.Key));
        Assert.Equal(0, (int?)plain["lanes"]);
        // a mod bundle of the native form goes along (its patched code runs in a plain start too); the fork's kind
        // of bundle, which has no patch list, does not
        var folder = Directory.CreateTempSubdirectory("fzgx-bundle").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "mod.bin"), "");
            File.WriteAllText(Path.Combine(folder, "codes.txt"), "");
            Assert.Null(JsonNode.Parse(RecompLaunch.OfflineLaunch(paths with { Bundle = folder }))!["bundle"]);
            File.WriteAllText(Path.Combine(folder, "patches.json"), "{}");
            var with = JsonNode.Parse(RecompLaunch.OfflineLaunch(paths with { Bundle = folder }))!.AsObject();
            Assert.Equal(["about", "bundle", "lanes"], with.Select(m => m.Key));
            Assert.Equal(folder, (string?)with["bundle"]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ARaceIsBootedFromItsMatchFileAsOnTheFork()
    {
        var paths = new RecompPaths("C:/r/moderngekko-run.exe", "C:/r/gGFZE01_recomp.dll", "C:/r/game", "C:/data/recomp-user",
            "C:/r/fzgx");
        var matchFile = Path.Combine(_folder, "match.json");
        MatchFile.Write(matchFile, new MatchStart("m22", new RelayEndpoint("r", "", 47811), "1f", 0,
            new RaceSetup(16, 1, [new SeatSetup(0, "p1", "ANA", 1, null)], 1, 2)), new RaceOptions("127.0.0.1", OtherSounds.Off));
        var file = JsonNode.Parse(RecompLaunch.RaceLaunch(paths, matchFile, "C:/data/replays/r.fzrp", widescreen: WidescreenMode.Off))!.AsObject();
        Assert.Equal(["about", "bundle", "widescreen", "lanes", "match"], file.Select(m => m.Key));
        Assert.Equal("Piconet Launcher: a race (match m22)", (string?)file["about"]);
        Assert.Equal("C:/r/fzgx", (string?)file["bundle"]);
        Assert.False((bool)file["widescreen"]!);
        // the "match" part is the match file, with where the race is recorded (the live race is on the status board,
        // which the window's members name: GameWindow.Board)
        var match = file["match"]!.AsObject();
        Assert.Equal(["match", "relayHost", "delay", "overclock", "otherSounds", "record"], match.Select(m => m.Key));
        Assert.Equal("m22", (string?)match["match"]!["matchId"]);
        Assert.Equal("off", (string?)match["otherSounds"]);
        Assert.Equal("C:/data/replays/r.fzrp", (string?)match["record"]);

        // without a replay or a listener nothing is said of them; widescreen is the default; no "debug" part
        var plain = JsonNode.Parse(RecompLaunch.RaceLaunch(paths, matchFile, null))!.AsObject();
        Assert.Null(plain["match"]!["record"]);
        Assert.Null(plain["match"]!["live"]);
        Assert.True((bool)plain["widescreen"]!);
        Assert.Null(plain["trueWidescreen"]);
        Assert.Null(plain["debug"]);
        // true widescreen: the game's 16:9 mode, and the key of its view after it
        var wide = JsonNode.Parse(RecompLaunch.RaceLaunch(paths, matchFile, null, WidescreenMode.TrueWide))!.AsObject();
        Assert.Equal(["about", "bundle", "widescreen", "trueWidescreen", "lanes", "match"], wide.Select(m => m.Key));
        Assert.True((bool)wide["widescreen"]! && (bool)wide["trueWidescreen"]!);
        var replay = JsonNode.Parse(RecompLaunch.PlaybackLaunch(paths, "r.fzrp", OtherSounds.Off, WidescreenMode.TrueWide))!.AsObject();
        Assert.Equal(["about", "bundle", "widescreen", "trueWidescreen", "lanes", "replay"], replay.Select(m => m.Key));
        Assert.Throws<ArgumentException>(() => RecompLaunch.RaceLaunch(paths with { Bundle = null }, matchFile, null));
        // a match file that is no JSON object: said, not started
        File.WriteAllText(matchFile, "[]");
        Assert.Throws<FormatException>(() => RecompLaunch.RaceLaunch(paths, matchFile, null));
        File.WriteAllText(matchFile, "{ not json");
        Assert.Throws<FormatException>(() => RecompLaunch.RaceLaunch(paths, matchFile, null));
    }

    [Fact]
    public void AReplayIsPlayedFromItsFile()
    {
        var paths = new RecompPaths("C:/r/moderngekko-run.exe", "C:/r/gGFZE01_recomp.dll", "C:/r/game", "C:/data/recomp-user",
            "C:/r/fzgx");
        var file = JsonNode.Parse(RecompLaunch.PlaybackLaunch(paths, "C:/data/replays/r.fzrp", OtherSounds.Off, widescreen: WidescreenMode.Off))!.AsObject();
        // a playback is neither a race from a match file nor offline play, and records nothing
        Assert.Equal(["about", "bundle", "widescreen", "lanes", "replay"], file.Select(m => m.Key));
        Assert.Equal("C:/r/fzgx", (string?)file["bundle"]);
        Assert.False((bool)file["widescreen"]!);
        Assert.Equal("""{"file":"C:/data/replays/r.fzrp","otherSounds":"off"}""", file["replay"]!.ToJsonString());
        // the sound choice by its word, the file as given, widescreen by default, and no view: it plays as the race
        foreach (var (sounds, word) in new[] { (OtherSounds.Full, "full"), (OtherSounds.Quieter, "quieter"), (OtherSounds.Off, "off") })
        {
            var ours = JsonNode.Parse(RecompLaunch.PlaybackLaunch(paths, "C:/data/replays/r.fzrp", sounds))!;
            Assert.Equal("C:/data/replays/r.fzrp", (string?)ours["replay"]!["file"]);
            Assert.Equal(word, (string?)ours["replay"]!["otherSounds"]);
            Assert.Null(ours["replay"]!["view"]);
            Assert.True((bool)ours["widescreen"]!);
        }
        Assert.Throws<ArgumentException>(() => RecompLaunch.PlaybackLaunch(paths with { Bundle = null }, "r.fzrp"));
        var args = RecompLaunch.Arguments(paths, "F-Zero GX", GameLaunchFile.PathFor(paths));
        Assert.Equal(Path.Combine("C:/data/recomp-user", "launch.json"), args[^1]);
        // started as every other start of the game: no console, its own log
        var info = RecompLaunch.StartInfo(paths, args);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(args, info.ArgumentList);
    }

    /// <summary>A replay file as the game writes one: the header (with the mod image's SHA-1 when given), the frame
    /// preamble, the lineup and some steps of two player seats.</summary>
    private string Replay(string name, string? modSha1, string? more = null)
    {
        var header = "{\"match\": {\"matchId\": \"m7\", \"setup\": {\"course\": 21, \"laps\": 2, \"seats\": [" +
                     "{\"seat\": 0, \"playerId\": \"p1\", \"name\": \"ANA\", \"machine\": 1}," +
                     "{\"seat\": 1, \"playerId\": \"p2\", \"name\": \"BEN\", \"machine\": 0}]}}," +
                     "\"seat\": 1, \"mask\": 3, \"delay\": 2" + (modSha1 is null ? "" : $", \"mod_sha1\": \"{modSha1}\"") +
                     (more is null ? "" : ", " + more) + "}";
        var json = System.Text.Encoding.UTF8.GetBytes(header);
        var path = Path.Combine(_folder, name);
        using var f = File.Create(path);
        f.Write([.. "FZRP"u8, 0, 2, (byte)(json.Length >> 24), (byte)(json.Length >> 16), (byte)(json.Length >> 8), (byte)json.Length]);
        f.Write(json);
        f.Write([.. "FZRF"u8, 0, 0, 0, 3, 0, 0, 0, 1, .. "FZRL"u8, 0, 0, 0, 124]);
        f.Write(new byte[124 + (60 * 16)]);
        return path;
    }

    [Fact]
    public void APlaybackNeedsTheBundleAndAReplayFile()
    {
        var paths = new RecompPaths(Path.Combine(_folder, "run.exe"), Path.Combine(_folder, "module.dll"),
            Path.Combine(_folder, "game"), Path.Combine(_folder, "user"), Path.Combine(_folder, "fzgx"));
        File.WriteAllText(paths.Runner, "");
        File.WriteAllText(paths.Module, "");
        Directory.CreateDirectory(Path.Combine(paths.GameFolder, "sys"));
        File.WriteAllText(Path.Combine(paths.GameFolder, "sys", "main.dol"), "");
        var replay = Replay("r.fzrp", null);
        Assert.Equal("Online part not installed", RecompLaunch.PlaybackProblem(paths, replay));
        Directory.CreateDirectory(paths.Bundle!);
        File.WriteAllBytes(Path.Combine(paths.Bundle!, "mod.bin"), "FZMD image"u8.ToArray());
        Assert.Null(RecompLaunch.PlaybackProblem(paths, replay));
        Assert.Equal("Replay file missing", RecompLaunch.PlaybackProblem(paths, Path.Combine(_folder, "none.fzrp")));
        var other = Path.Combine(_folder, "notes.fzrp");
        File.WriteAllText(other, "not a replay");
        Assert.Equal("Not a replay of this game", RecompLaunch.PlaybackProblem(paths, other));
    }

    [Fact]
    public void AReplayThatCannotBePlayedIsRefusedWithItsReason()
    {
        var paths = new RecompPaths(Path.Combine(_folder, "run.exe"), Path.Combine(_folder, "module.dll"),
            Path.Combine(_folder, "game"), Path.Combine(_folder, "user"), Path.Combine(_folder, "fzgx"));
        File.WriteAllText(paths.Runner, "");
        File.WriteAllText(paths.Module, "");
        Directory.CreateDirectory(Path.Combine(paths.GameFolder, "sys"));
        File.WriteAllText(Path.Combine(paths.GameFolder, "sys", "main.dol"), "");
        Directory.CreateDirectory(paths.Bundle!);
        var image = "FZMD image"u8.ToArray();
        File.WriteAllBytes(Path.Combine(paths.Bundle!, "mod.bin"), image);
#pragma warning disable CA5350 // the replay format names the image by its SHA-1
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(image));
#pragma warning restore CA5350
        var whole = File.ReadAllBytes(Replay("whole.fzrp", sha));
        string Cut(string name, int length)
        {
            var path = Path.Combine(_folder, name);
            File.WriteAllBytes(path, whole[..length]);
            return path;
        }

        // a whole replay of this game's image is played; one that names no image too (the fork's oldest)
        Assert.Null(ReplayFile.Problem(Path.Combine(_folder, "whole.fzrp")));
        Assert.Null(RecompLaunch.PlaybackProblem(paths, Path.Combine(_folder, "whole.fzrp")));
        Assert.Null(RecompLaunch.PlaybackProblem(paths, Replay("silent.fzrp", null)));

        // a file that ends early: in the header, before the frames' preamble, in the lineup, in a step, before any step
        var header = 10 + ((whole[6] << 24) | (whole[7] << 16) | (whole[8] << 8) | whole[9]);
        var steps = header + 12 + 8 + 124;
        foreach (var length in new[] { header - 5, header + 6, steps - 40, steps, steps + 7, whole.Length - 1, whole.Length - 16 + 3 })
        {
            Assert.Equal(ReplayFile.Incomplete, ReplayFile.Problem(Cut("cut.fzrp", length)));
            Assert.Equal(ReplayFile.Incomplete, RecompLaunch.PlaybackProblem(paths, Path.Combine(_folder, "cut.fzrp")));
        }
        // a race cut short ends on a whole step, and plays
        Assert.Null(ReplayFile.Problem(Cut("short.fzrp", whole.Length - (16 * 20))));
        Assert.Equal(ReplayFile.NotAReplay, ReplayFile.Problem(Cut("stub.fzrp", 6)));

        // another replay format: another version of the game program wrote it
        var later = (byte[])whole.Clone();
        later[5] = 8;
        File.WriteAllBytes(Path.Combine(_folder, "later.fzrp"), later);
        Assert.Equal("Replay of another version of the game · format 8",
            RecompLaunch.PlaybackProblem(paths, Path.Combine(_folder, "later.fzrp")));

        // another game than the USA disc's; this game's own id is fine
        Assert.Equal("Replay of another version of F-Zero GX · GFZJ01",
            RecompLaunch.PlaybackProblem(paths, Replay("japan.fzrp", sha, "\"game\": \"GFZJ01\"")));
        Assert.Null(RecompLaunch.PlaybackProblem(paths, Replay("usa.fzrp", sha, "\"game\": \"GFZE01\"")));

        // an image with no row in the table of images that are the same race
        var fork = new string('b', 40);
        Assert.Equal(RecompLaunch.OtherImage, RecompLaunch.PlaybackProblem(paths, Replay("fork.fzrp", fork)));
        File.WriteAllText(Path.Combine(paths.Bundle!, RecompLaunch.SameRaceFile),
            $$"""{"version": 1, "rows": [{"recorded": "{{fork}}", "plays_on": "{{sha}}", "evidence": "a run"}]}""");
        Assert.Null(RecompLaunch.PlaybackProblem(paths, Path.Combine(_folder, "fork.fzrp")));
        Assert.Equal(RecompLaunch.OtherImage, RecompLaunch.PlaybackProblem(paths, Replay("other.fzrp", new string('a', 40))));
    }

    [Fact]
    public void AReplaySaysWhatRecordedIt()
    {
        var native = Replay("native.fzrp", new string('c', 40), "\"engine\": \"native\", \"module_sha1\": \"" + new string('d', 40) + "\"");
        Assert.Equal(("native", new string('d', 40)), ReplayFile.RecordedBy(native));
        Assert.Equal((null, null), ReplayFile.RecordedBy(Replay("fork.fzrp", new string('c', 40))));
        Assert.Equal((null, null), ReplayFile.RecordedBy(Path.Combine(_folder, "none.fzrp")));
        // the listing reads such a file as any other
        Assert.Equal("BEN", ReplayFile.Read(native)!.You);
    }

    [Fact]
    public void ARaceIsRecordedIntoTheReplayFolderUnlessToldOtherwise()
    {
        var match = Path.Combine(_folder, "match.json");
        File.WriteAllText(match, """{"match": {"matchId": "m32", "seat": 0}, "relayHost": "127.0.0.1"}""");
        var now = new DateTimeOffset(2026, 10, 6, 4, 30, 5, TimeSpan.FromHours(-4));
        var replays = Path.Combine(_folder, "replays");
        Assert.Equal(Path.Combine(replays, "20261006-043005-m32.fzrp"), RecompLaunch.ReplayFileFor(replays, match, now));
        // the name is the fork's: the Replays page lists the folder's *.fzrp
        Assert.Equal(RecompLaunch.ReplayFileName(now, "m32"), Path.GetFileName(RecompLaunch.ReplayFileFor(replays, match, now)));
        Assert.Null(RecompLaunch.ReplayFileFor(null, match, now));
        Assert.Null(RecompLaunch.ReplayFileFor(replays, Path.Combine(_folder, "none.json"), now));
        File.WriteAllText(match, """{"match": {"matchId": "..\\up"}}""");
        Assert.Null(RecompLaunch.ReplayFileFor(replays, match, now));
        File.WriteAllText(match, "not a match");
        Assert.Null(RecompLaunch.ReplayFileFor(replays, match, now));
    }

    [Fact]
    public void AReplayRecordedWithAnotherModImageIsToldApart()
    {
        // A replay holds the inputs, not the race: it plays back as recorded only on a game that simulates as the
        // recording game did. Its header names the mod image it was recorded with (SHA-1); the game's is the bundle's.
        var paths = new RecompPaths("run.exe", "module.dll", "game", "user", Path.Combine(_folder, "fzgx"));
        Directory.CreateDirectory(paths.Bundle!);
        var image = "FZMD image"u8.ToArray();
        File.WriteAllBytes(Path.Combine(paths.Bundle!, "mod.bin"), image);
#pragma warning disable CA5350 // the replay format names the image by its SHA-1
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(image));
#pragma warning restore CA5350
        var same = Replay("same.fzrp", sha.ToLowerInvariant());
        Assert.Equal(sha.ToLowerInvariant(), ReplayFile.ModImage(same));
        Assert.True(RecompLaunch.RecordedWithThisImage(paths, same));
        Assert.True(RecompLaunch.RecordedWithThisImage(paths, Replay("upper.fzrp", sha)));
        Assert.False(RecompLaunch.RecordedWithThisImage(paths, Replay("other.fzrp", new string('a', 40))));
        // nothing to compare: the replay does not say, the file is no replay, or there is no bundle
        Assert.Null(RecompLaunch.RecordedWithThisImage(paths, Replay("silent.fzrp", null)));
        Assert.Null(ReplayFile.ModImage(Path.Combine(_folder, "none.fzrp")));
        Assert.Null(RecompLaunch.RecordedWithThisImage(paths, Replay("short.fzrp", "abc")));
        Assert.Null(RecompLaunch.RecordedWithThisImage(paths with { Bundle = null }, same));
        Assert.Null(RecompLaunch.RecordedWithThisImage(paths with { Bundle = Path.Combine(_folder, "nowhere") }, same));

        // Two images that are the same race (the fork's and the native form's of one set of routines are two files):
        // only by a row of the table staged beside the image, in that direction, for this game's image.
        var fork = new string('b', 40);
        var table = Path.Combine(paths.Bundle!, RecompLaunch.SameRaceFile);
        var fromFork = Replay("fork.fzrp", fork);
        Assert.False(RecompLaunch.RecordedWithThisImage(paths, fromFork));
        File.WriteAllText(table, $$"""{"version": 1, "rows": [{"recorded": "{{fork.ToUpperInvariant()}}", "plays_on": "{{sha.ToLowerInvariant()}}", "evidence": "a run"}]}""");
        Assert.True(RecompLaunch.RecordedWithThisImage(paths, fromFork));
        Assert.False(RecompLaunch.RecordedWithThisImage(paths, Replay("other2.fzrp", new string('a', 40))));
        File.WriteAllText(table, $$"""{"version": 1, "rows": [{"recorded": "{{sha}}", "plays_on": "{{fork}}"}, {"recorded": "{{fork}}"}, 7]}""");
        Assert.False(RecompLaunch.RecordedWithThisImage(paths, fromFork));   // the other direction, or half a row
        File.WriteAllText(table, "not a table");
        Assert.False(RecompLaunch.RecordedWithThisImage(paths, fromFork));
        Assert.True(RecompLaunch.RecordedWithThisImage(paths, same));
    }

    [Fact]
    public void TheGameIsStartedWithNoConsoleAndWritesItsOwnLog()
    {
        // The runner is a console program and the launcher has no console: started any other way, Windows opens a
        // command prompt window beside the game's. Offline play and a race are started alike.
        var paths = new RecompPaths(Path.Combine(_folder, "r", "moderngekko-run.exe"), "m.dll", "game", Path.Combine(_folder, "user"),
            "fzgx");
        foreach (var args in new[] { RecompLaunch.Arguments(paths, "F-Zero GX", "launch.json"),
                     RecompLaunch.Arguments(paths, "F-Zero GX", "launch.json", ["--mute"]) })
        {
            var info = RecompLaunch.StartInfo(paths, args);
            Assert.True(info.CreateNoWindow);
            Assert.False(info.UseShellExecute);
            Assert.Equal(paths.Runner, info.FileName);
            Assert.Equal(Path.Combine(_folder, "r"), info.WorkingDirectory);
            Assert.Equal(args, info.ArgumentList);
            // nothing is read through a pipe the launcher would have to keep emptying: the runner writes the file
            Assert.False(info.RedirectStandardOutput);
            Assert.False(info.RedirectStandardError);
            Assert.False(info.RedirectStandardInput);
            Assert.Equal(Path.Combine(_folder, "user", "runner.log"), info.ArgumentList[info.ArgumentList.IndexOf("--fzgx-log") + 1]);
        }
        Assert.Equal(Path.Combine(_folder, "user", "runner.log"), RecompLaunch.LogFile(paths));
    }

    [Fact]
    public void ARaceNeedsTheModBundle()
    {
        var paths = new RecompPaths(Path.Combine(_folder, "run.exe"), Path.Combine(_folder, "module.dll"),
            Path.Combine(_folder, "game"), Path.Combine(_folder, "user"), Path.Combine(_folder, "fzgx"));
        File.WriteAllText(paths.Runner, "");
        File.WriteAllText(paths.Module, "");
        Directory.CreateDirectory(Path.Combine(paths.GameFolder, "sys"));
        File.WriteAllText(Path.Combine(paths.GameFolder, "sys", "main.dol"), "");
        Assert.Null(RecompLaunch.Problem(paths));
        Assert.Equal("Online part not installed", RecompLaunch.RaceProblem(paths));
        Assert.NotNull(RecompLaunch.RaceProblem(paths with { Bundle = null }));
        Directory.CreateDirectory(paths.Bundle!);
        File.WriteAllText(Path.Combine(paths.Bundle!, "mod.bin"), "");
        Assert.Null(RecompLaunch.RaceProblem(paths));
    }

    /// <summary>What the launcher's own checks of a file being there say (they were written where they were asked;
    /// now beside the other reads): a race's match file, the fork's program.</summary>
    [Fact]
    public void AMatchFileThatIsNotThereIsSaid()
    {
        var match = Path.Combine(_folder, "match.json");
        Assert.Equal("Match file missing", RecompLaunch.MatchFileProblem(match));
        File.WriteAllText(match, "{}");
        Assert.Null(RecompLaunch.MatchFileProblem(match));
    }

    [Fact]
    public void TheGamesSettingsAreAddedAndThePlayersKept()
    {
        // a fresh folder: everything the game needs
        var fresh = RecompLaunch.WithDefaults([]);
        Assert.Equal("6.0", RecompLaunch.Setting(fresh, "Core", "Overclock"));
        Assert.Equal("True", RecompLaunch.Setting(fresh, "Core", "OverclockEnable"));
        Assert.Equal("0x8001071C", RecompLaunch.Setting(fresh, "Core", "StaticRecompIdlePC"));
        Assert.Equal("1.0", RecompLaunch.Setting(fresh, "Core", "EmulationSpeed"));
        Assert.Equal("False", RecompLaunch.Setting(fresh, "Core", "EnableCheats"));
        Assert.Equal("False", RecompLaunch.Setting(fresh, "Interface", "ConfirmStop"));
        Assert.Null(RecompLaunch.Setting(fresh, "DSP", "Volume"));                 // the emulator's own: full volume
        Assert.Null(RecompLaunch.Setting(fresh, "Core", "EnableCustomRTC"));       // offline play runs on the real clock

        // a folder the player has used: their values stay, what the module fixes is put back, nothing else is lost
        string[] used =
        [
            "[Core]", "Overclock = 1.0", "EmulationSpeed = 0.5", "EnableCheats = True", "CPUThread = True",
            "[DSP]", "Volume = 35", "Backend = OpenAL",
            "[Display]", "Fullscreen = True",
        ];
        var kept = RecompLaunch.WithDefaults(used);
        Assert.Equal("6.0", RecompLaunch.Setting(kept, "Core", "Overclock"));
        Assert.Equal("False", RecompLaunch.Setting(kept, "Core", "EnableCheats"));
        Assert.Equal("0.5", RecompLaunch.Setting(kept, "Core", "EmulationSpeed"));
        Assert.Equal("True", RecompLaunch.Setting(kept, "Core", "CPUThread"));
        Assert.Equal("35", RecompLaunch.Setting(kept, "DSP", "Volume"));
        Assert.Equal("OpenAL", RecompLaunch.Setting(kept, "DSP", "Backend"));
        Assert.Equal("True", RecompLaunch.Setting(kept, "Display", "Fullscreen"));
        Assert.Equal(kept, RecompLaunch.WithDefaults(kept));                       // and a second start changes nothing
    }

    [Fact]
    public void TheControllerIsTakenFromTheForksFolderOnceAndNeverWrittenThere()
    {
        var user = Path.Combine(_folder, "recomp-user");
        var fork = Path.Combine(_folder, "dolphin-user");
        Directory.CreateDirectory(Path.Combine(fork, "Config"));
        var theirs = Path.Combine(fork, "Config", "GCPadNew.ini");
        File.WriteAllText(theirs, "[GCPad1]\nDevice = XInput/0/Gamepad\n");
        var stamp = File.GetLastWriteTimeUtc(theirs);

        RecompLaunch.PrepareUserFolder(user, fork);
        var mine = Path.Combine(user, "Config", "GCPadNew.ini");
        Assert.Equal(File.ReadAllText(theirs), File.ReadAllText(mine));
        Assert.Equal("6.0", RecompLaunch.Setting(File.ReadAllLines(Path.Combine(user, "Config", "Dolphin.ini")), "Core", "Overclock"));
        Assert.True(Directory.Exists(Path.Combine(user, "Cache")));   // where the emulator keeps its shader cache

        // the game's own profile, once it has one, is the player's to change: a later start leaves it
        File.WriteAllText(mine, "[GCPad1]\nDevice = SDL/0/Adapter\n");
        File.WriteAllText(theirs, "[GCPad1]\nDevice = XInput/1/Gamepad\n");
        File.SetLastWriteTimeUtc(theirs, stamp);
        RecompLaunch.PrepareUserFolder(user, fork);
        Assert.Contains("SDL/0/Adapter", File.ReadAllText(mine), StringComparison.Ordinal);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(theirs));
        Assert.Equal(["GCPadNew.ini"], Directory.GetFiles(Path.Combine(fork, "Config")).Select(Path.GetFileName));

        // no fork folder, or none with a profile: the game starts without one (the runner then makes its own)
        var alone = Path.Combine(_folder, "alone");
        RecompLaunch.PrepareUserFolder(alone, Path.Combine(_folder, "nowhere"));
        RecompLaunch.PrepareUserFolder(alone);
        Assert.False(File.Exists(Path.Combine(alone, "Config", "GCPadNew.ini")));
    }

    [Fact]
    public void TheGameTakesEachPortsDeviceFromTheForkWhereItNamesNone()
    {
        var user = Path.Combine(_folder, "ports-user");
        var fork = Path.Combine(_folder, "ports-fork");
        Directory.CreateDirectory(Path.Combine(fork, "Config"));
        var theirs = Path.Combine(fork, "Config", "Dolphin.ini");
        // the adapter on port 1, a standard controller on port 2, nothing said about port 3, port 4 empty
        File.WriteAllText(theirs, "[Core]\nSIDevice0 = 12\nSIDevice1 = 6\nSIDevice3 = 0\nOverclock = 1.0\n[DSP]\nVolume = 40\n");
        var before = File.ReadAllText(theirs);

        RecompLaunch.PrepareUserFolder(user, fork);
        var mine = Path.Combine(user, "Config", "Dolphin.ini");
        var lines = File.ReadAllLines(mine);
        Assert.Equal("12", RecompLaunch.Setting(lines, "Core", "SIDevice0"));
        Assert.Equal("6", RecompLaunch.Setting(lines, "Core", "SIDevice1"));
        Assert.Null(RecompLaunch.Setting(lines, "Core", "SIDevice2"));
        Assert.Equal("0", RecompLaunch.Setting(lines, "Core", "SIDevice3"));
        // nothing else of the fork's settings comes along, and the fork's file is only read
        Assert.Equal("6.0", RecompLaunch.Setting(lines, "Core", "Overclock"));
        Assert.Null(RecompLaunch.Setting(lines, "DSP", "Volume"));
        Assert.Equal(before, File.ReadAllText(theirs));

        // a port the game's settings name is the player's: a later start leaves it, and adds nothing twice
        File.WriteAllLines(mine, lines.Select(l => l.StartsWith("SIDevice0", StringComparison.Ordinal) ? "SIDevice0 = 6" : l));
        RecompLaunch.PrepareUserFolder(user, fork);
        lines = File.ReadAllLines(mine);
        Assert.Equal("6", RecompLaunch.Setting(lines, "Core", "SIDevice0"));
        Assert.Single(lines, l => l.StartsWith("SIDevice1", StringComparison.Ordinal));

        // a value that is not a device number is not carried
        Assert.Null(RecompLaunch.Setting(RecompLaunch.WithPorts(["[Core]"], ["[Core]", "SIDevice2 = adapter"]), "Core", "SIDevice2"));
        // settings without a [Core] section get one
        Assert.Equal("12", RecompLaunch.Setting(RecompLaunch.WithPorts([], ["[Core]", "SIDevice0 = 12"]), "Core", "SIDevice0"));
    }
}
