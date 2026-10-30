using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;
using TestSupport;

namespace Launcher.Tests;

/// <summary>A race of a game mode: every match file the launcher writes carries the race's <c>setup.mode</c> inline,
/// the package whose SHA-256 is its hash, on each of the three paths a game is started on: a racer's match start, a
/// watcher's (a spectator without the package would run another race and desync), and the rejoin, which starts the
/// game again on the same match file. A match start or a watcher's start whose mode is not one is dropped.</summary>
public sealed class ModeMatchFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mode-match-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly RelayEndpoint Relay = new("r1", "", 47811);

    /// <summary>Overdrive of the protocol's vectors, on its defaults, as a match start carries it.</summary>
    private static ModeStart Mode() =>
        ModeCompiler.Compile(File.ReadAllText(RepositoryFolder.Protocol("protocol", "tests", "vectors", "quickplay", "overdrive.fzmode")))
            .Mode!.Start();

    private static RaceSetup Setup(ModeStart mode) =>
        new(8, 0, [new SeatSetup(0, "p1", "ANA", 3, new Rgb(1, 2, 3)), new SeatSetup(1, "p2", "BEN", 5, new Rgb(4, 5, 6))], 1, 2, Mode: mode);

    /// <summary>The match file's <c>match.setup.mode</c>: its package is the mode's, and hashes to its hash.</summary>
    private static void HoldsTheMode(string matchFileText, ModeStart mode)
    {
        var written = JsonNode.Parse(matchFileText)!["match"]!["setup"]!["mode"]!;
        var package = Convert.FromBase64String(written["package"]!.GetValue<string>());
        Assert.Equal(mode.Hash, written["hash"]!.GetValue<string>());
        Assert.Equal(mode.Hash, Convert.ToHexStringLower(SHA256.HashData(package)));
        Assert.Equal(mode.Params, written["params"]!.AsArray().Select(p => p!.GetValue<int>()));
    }

    private QuickPlaySession Session(Action<ForkRace> started) =>
        new(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), started);

    private static async Task<ForkRace> Started(TaskCompletionSource<ForkRace> started) =>
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public async Task ARacersMatchFileCarriesTheMode()
    {
        var mode = Mode();
        var started = new TaskCompletionSource<ForkRace>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LedServer();
        await using var session = Session(r => started.TrySetResult(r));
        var joining = session.JoinAsync(server.Address, cancel: TestContext.Current.CancellationToken);
        await Headless.Until(() => server.Of<Hello>().Count == 1, TestContext.Current.CancellationToken);
        await server.SendAsync(LedServer.Welcome(), LedServer.Lobby(LobbyState.Starting));
        await joining;
        await server.SendAsync(new MatchStart("m1", Relay, "1f2e3d4c5b6a7988", 0, Setup(mode)));
        var race = await Started(started);
        Assert.False(race.Spectate);
        HoldsTheMode(File.ReadAllText(race.MatchFile), mode);
    }

    [Fact]
    public async Task AWatchersMatchFileCarriesTheMode()
    {
        var mode = Mode();
        var started = new TaskCompletionSource<ForkRace>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LedServer();
        await using var session = Session(r => started.TrySetResult(r));
        var joining = session.JoinAsync(server.Address, cancel: TestContext.Current.CancellationToken);
        await Headless.Until(() => server.Of<Hello>().Count == 1, TestContext.Current.CancellationToken);
        await server.SendAsync(LedServer.Welcome() with { Spectators = true }, LedServer.Lobby(LobbyState.Forming));
        await joining;
        await Headless.Until(() => session.CanWatch, TestContext.Current.CancellationToken);
        await session.WatchAsync(true);
        await server.SendAsync(new WatchStart("m1", Relay, "1f2e3d4c5b6a7988", 1, Setup(mode)));
        var race = await Started(started);
        Assert.True(race.Spectate);
        var text = File.ReadAllText(race.MatchFile);
        Assert.True(JsonNode.Parse(text)!["spectate"]!.GetValue<bool>());
        HoldsTheMode(text, mode);
    }

    [Fact]
    public async Task TheRejoinStartsOnTheSameMatchFileWithTheMode()
    {
        var mode = Mode();
        var starts = new List<ForkRace>();
        var first = new TaskCompletionSource<ForkRace>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LedServer();
        await using var session = Session(r =>
        {
            starts.Add(r);
            first.TrySetResult(r);
        });
        var joining = session.JoinAsync(server.Address, cancel: TestContext.Current.CancellationToken);
        await Headless.Until(() => server.Of<Hello>().Count == 1, TestContext.Current.CancellationToken);
        await server.SendAsync(LedServer.Welcome(), LedServer.Lobby(LobbyState.Starting));
        await joining;
        var setup = Setup(mode);
        await server.SendAsync(new MatchStart("m1", Relay, "1f2e3d4c5b6a7988", 0, setup),
            LedServer.Lobby(LobbyState.Racing, racing: true) with { Race = new LobbyRace("m1", 8, 0, setup.Seats, DateTimeOffset.UtcNow) });
        var race = await Started(first);
        await Headless.Until(() => session.Lobby is { Stage: LobbyState.Racing }, TestContext.Current.CancellationToken);

        await session.RaceOverAsync(rejoinable: true);   // the game left its race while it ran
        Assert.True(session.Rejoin());
        var again = session.Race!;
        Assert.True(again.Rejoin);
        Assert.Equal(race.MatchFile, again.MatchFile);
        // the launch file of the rejoin: the match file, mode and all, started again into its race
        var launch = JsonNode.Parse(GameLaunchFile.Race("bundle", GameLaunchFile.MatchFileText(again.MatchFile), rejoin: true))!;
        Assert.True(launch["match"]!["rejoin"]!.GetValue<bool>());
        HoldsTheMode(launch["match"]!.ToJsonString(), mode);
    }

    [Fact]
    public void AStartWhoseModeIsNotOneIsDroppedOnEveryPath()
    {
        var mode = Mode();
        var start = new MatchStart("m1", Relay, "1f2e3d4c5b6a7988", 0, Setup(mode));
        var watch = new WatchStart("m1", Relay, "1f2e3d4c5b6a7988", 1, Setup(mode));
        Assert.NotNull(ServerMessages.Checked(start));
        Assert.NotNull(ServerMessages.Checked(watch));
        foreach (var bad in new[]
        {
            mode with { Hash = "nothex" },
            mode with { Hash = mode.Hash.ToUpperInvariant() },
            mode with { Package = "" },
            mode with { Package = new string('A', ((ModeWords.PackageBytes + 2) / 3 * 4) + 4) },
            mode with { Params = null! },
        })
        {
            Assert.Null(ServerMessages.Checked(start with { Setup = Setup(bad) }));
            Assert.Null(ServerMessages.Checked(watch with { Setup = Setup(bad) }));
        }
    }
}
