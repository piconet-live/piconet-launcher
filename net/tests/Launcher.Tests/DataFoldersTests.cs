using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.App;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The replays and the fork's user folder belong to the data folder they are in (<see cref="DataFolders"/>): a data
/// folder moved into another copy, as the read-me tells a tester to do, and a copy renamed after its first start,
/// keep their replays and write new ones beside them; a replay folder the player chose is kept. The settings files
/// here are written as the first build wrote them, with both folders as full paths. Replays are made up.
/// </summary>
public sealed class DataFoldersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A whole made-up replay of one match, as <c>ReplayTests</c> writes one.</summary>
    private static void Replay(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var json = Encoding.UTF8.GetBytes("""
            {"match": {"matchId": "m7", "seat": 0, "setup": {"course": 21, "laps": 2, "seats": [
              {"seat": 0, "playerId": "p1", "name": "ANA", "machine": 1}, {"seat": 1, "name": "CPU", "machine": 3}]}},
             "seat": 0, "mask": 1, "delay": 2, "recorded": "2026-10-08T20:00:00+00:00"}
            """);
        using var f = File.Create(Path.Combine(folder, name));
        var w = new BinaryWriter(f);
        void Number(uint value)
        {
            Span<byte> u32 = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(u32, value);
            w.Write(u32);
        }
        w.Write("FZRP"u8);
        w.Write((byte)0);
        w.Write((byte)2);
        Number((uint)json.Length);
        w.Write(json);
        w.Write("FZRF"u8);
        Number(3);
        Number(1);
        w.Write("FZRL"u8);
        Number(122);
        w.Write(new byte[122]);
        w.Write(new byte[600 * 16]);
    }

    /// <summary>A copy's data folder as the first build left it after a start: both folders written as full paths
    /// of that data folder (<paramref name="replays"/>: another replay folder, the player's choice), one replay.</summary>
    private string FirstBuildData(string copy, string? replays = null)
    {
        var data = Path.Combine(_dir, copy, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            racer = new { name = "ANA", machine = 6 },
            userFolder = Path.Combine(data, "dolphin-user"),
            replayFolder = replays ?? Path.Combine(data, "replays"),
            noServer = true,
        }));
        Replay(replays ?? Path.Combine(data, "replays"), "old-race.fzrp");
        return data;
    }

    private static JsonNode Stored(string data) => JsonNode.Parse(File.ReadAllText(Path.Combine(data, "settings.json")))!;

    private static string[] Listed(AppState state) => [.. ReplayFile.List(state.Settings.ReplayFolder).Select(r => Path.GetFileName(r.Path))];

    [Fact]
    public void AFirstBuildsDataFolderMovedIntoANewCopyListsItsReplaysAndWritesNewOnesThere()
    {
        var old = FirstBuildData("Copy-0.1.0");
        var moved = Path.Combine(_dir, "Copy-0.2.0", "data");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(old, moved);                                            // as the read-me says: only "data" moves

        var state = AppState.Load(moved);
        Assert.Equal("ANA", state.Settings.Racer.Name);                        // the settings came along
        Assert.Equal(Path.Combine(moved, "replays"), state.Settings.ReplayFolder);
        Assert.Equal(Path.Combine(moved, "dolphin-user"), state.Settings.UserFolder);
        Assert.Equal(["old-race.fzrp"], Listed(state));                        // the Replays page has them

        // a race is recorded where they are, as a session and a match start name the file
        var match = Path.Combine(moved, "race", "match.json");
        Directory.CreateDirectory(Path.GetDirectoryName(match)!);
        File.WriteAllText(match, """{"match": {"matchId": "m8"}}""");
        var next = RecompLaunch.ReplayFileFor(state.Settings.ReplayFolder, match, DateTimeOffset.Now)!;
        Assert.Equal(Path.Combine(moved, "replays"), Path.GetDirectoryName(next));
        Replay(Path.GetDirectoryName(next)!, Path.GetFileName(next));
        Assert.Equal(2, Listed(state).Length);

        // nothing of the old copy is made again, and the file no longer names a folder of any copy
        Assert.False(Directory.Exists(Path.Combine(_dir, "Copy-0.1.0", "data")));
        Assert.Null(Stored(moved)["replayFolder"]);
        Assert.Null(Stored(moved)["userFolder"]);
        Assert.DoesNotContain("Copy-0.1.0", File.ReadAllText(Path.Combine(moved, "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyRenamedAfterItsFirstStartKeepsItsReplays()
    {
        var first = Path.Combine(_dir, "Copy", "data");
        var state = AppState.Load(first);                                      // a first start: no settings yet
        Assert.Equal(Path.Combine(first, "replays"), state.Settings.ReplayFolder);
        Assert.Null(Stored(first)["replayFolder"]);                            // its own folders are not written as full paths
        Assert.Null(Stored(first)["userFolder"]);
        Replay(state.Settings.ReplayFolder!, "a-race.fzrp");
        state.Update(s => s with { Widescreen = WidescreenMode.Off });                      // any change saved
        Assert.Null(Stored(first)["replayFolder"]);

        Directory.Move(Path.Combine(_dir, "Copy"), Path.Combine(_dir, "Copy renamed"));
        var renamed = Path.Combine(_dir, "Copy renamed", "data");
        var again = AppState.Load(renamed);
        Assert.Equal(WidescreenMode.Off, again.Settings.Widescreen);
        Assert.Equal(Path.Combine(renamed, "replays"), again.Settings.ReplayFolder);
        Assert.Equal(["a-race.fzrp"], Listed(again));
        Assert.False(Directory.Exists(Path.Combine(_dir, "Copy")));

        // and a copy the first build started, renamed: its full paths name the old name
        var old = FirstBuildData("Old name");
        Directory.Move(Path.Combine(_dir, "Old name"), Path.Combine(_dir, "New name"));
        var there = AppState.Load(Path.Combine(_dir, "New name", "data"));
        Assert.Equal(Path.Combine(_dir, "New name", "data", "replays"), there.Settings.ReplayFolder);
        Assert.Equal(["old-race.fzrp"], Listed(there));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void AReplayFolderThePlayerChoseIsKept()
    {
        var chosen = Path.Combine(_dir, "My replays");
        var old = FirstBuildData("Copy-0.1.0", chosen);
        var moved = Path.Combine(_dir, "Copy-0.2.0", "data");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(old, moved);

        var state = AppState.Load(moved);
        Assert.Equal(chosen, state.Settings.ReplayFolder);                     // the player's choice, wherever the copy is
        Assert.Equal(["old-race.fzrp"], Listed(state));
        Assert.Equal(Path.Combine(moved, "dolphin-user"), state.Settings.UserFolder);   // never the player's: this copy's
        Assert.Equal(chosen, (string?)Stored(moved)["replayFolder"]);          // and it stays written
        Assert.Null(Stored(moved)["userFolder"]);

        // chosen in Settings while the launcher runs: written; set back to the copy's own: not written
        state.Update(s => s with { ReplayFolder = Path.Combine(_dir, "Other") });
        Assert.Equal(Path.Combine(_dir, "Other"), (string?)Stored(moved)["replayFolder"]);
        state.Update(s => s with { ReplayFolder = Path.Combine(moved, "replays") });
        Assert.Null(Stored(moved)["replayFolder"]);
    }

    [Fact]
    public void WhichStoredFolderIsAnotherDataFoldersOwn()
    {
        const string Here = @"D:\Games\Piconet-0.2.0\data";
        static LauncherSettings Read(string? user, string? replays) => new() { UserFolder = user, ReplayFolder = replays };
        static (string?, string?) At(LauncherSettings s) => (s.UserFolder?.Replace('/', '\\'), s.ReplayFolder?.Replace('/', '\\'));
        var own = (Here + @"\dolphin-user", Here + @"\replays");

        // the first build's file, as Windows wrote it: another copy's, the installed launcher's, with other case and separators
        Assert.Equal(own, At(DataFolders.AtHome(Read(@"C:\Old\Piconet-0.1.0\data\dolphin-user", @"C:\Old\Piconet-0.1.0\data\replays"), Here)));
        Assert.Equal(own, At(DataFolders.AtHome(Read(@"C:\Users\ana\AppData\Local\fzgx-launcher\dolphin-user", @"C:\Users\ana\AppData\Local\fzgx-launcher\replays"), Here)));
        Assert.Equal(own, At(DataFolders.AtHome(Read(@"c:/old/DATA/dolphin-user/", @"C:\old\data\Replays"), Here)));
        Assert.Equal(own, At(DataFolders.AtHome(Read(null, null), Here)));     // a first start
        Assert.Equal(own, At(DataFolders.AtHome(Read(own.Item1, own.Item2), Here)));

        // a chosen replay folder is kept beside a user folder that is another data folder's own
        Assert.Equal((own.Item1, @"E:\Replays"), At(DataFolders.AtHome(Read(@"C:\Old\Piconet-0.1.0\data\dolphin-user", @"E:\Replays"), Here)));
        Assert.Equal((own.Item1, @"E:\Old\replays"), At(DataFolders.AtHome(Read(null, @"E:\Old\replays"), Here)));   // nothing says it was a data folder's
        // a user folder set by hand to a folder that is no data folder's own stays, with the replay folder beside it
        Assert.Equal((@"C:\fork\work\dolphin-user", @"E:\Replays"), At(DataFolders.AtHome(Read(@"C:\fork\work\dolphin-user", @"E:\Replays"), Here)));
        Assert.Equal((@"C:\fork\my-user", own.Item2), At(DataFolders.AtHome(Read(@"C:\fork\my-user", null), Here)));

        // written: this data folder's own are not named, anything else is
        var kept = DataFolders.ForTheFile(Read(Here + @"\dolphin-user\", @"d:/games/piconet-0.2.0/data/replays"), Here);
        Assert.Equal((null, null), (kept.UserFolder, kept.ReplayFolder));
        var named = DataFolders.ForTheFile(Read(@"C:\fork\my-user", @"E:\Replays"), Here);
        Assert.Equal((@"C:\fork\my-user", @"E:\Replays"), (named.UserFolder, named.ReplayFolder));
    }
}
