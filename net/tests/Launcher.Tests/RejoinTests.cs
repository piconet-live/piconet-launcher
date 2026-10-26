using Launcher.App;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>Rejoin on the lobby's line: offered while the lobby races the race
/// the player's game left, and no longer once the race server says that race is over (the lobby's state,
/// <see cref="LobbyRace.Over"/>), so a game is not started again into a race with nothing to come back to.</summary>
public sealed class RejoinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rejoin-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Start = """
        {"type":"matchStart","matchId":"m1","relay":{"id":"r1","host":"","port":47811},"token":"1f2e3d4c5b6a7988","seat":0,
         "setup":{"course":21,"laps":3,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}],"shuffleSeed":1,"cpuSeed":2}}
        """;

    private static string Racing(bool over)
    {
        const string race = """
            "race":{"matchId":"m1","course":21,"laps":3,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}],"started":"2026-10-18T10:00:00Z"
            """;
        return FakeCoordinator.Lobby[..^1] + ",\"stage\":\"racing\"," + race + (over ? ",\"over\":true}}" : "}}");
    }

    /// <summary>The game left its race while it ran (a crash): Rejoin while the race runs; none once it is over.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RejoinIsOfferedOnlyWhileTheRaceIsNotOver(bool over, bool offered)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, Start, Racing(over));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"),
            _ => started.TrySetResult());
        await session.JoinAsync(server.Address, cancel: cancel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        while (session.Lobby is not { Stage: LobbyState.Racing })
        {
            await Task.Delay(10, cancel);
        }
        Assert.Equal(over, session.Lobby.Race!.Over);

        await session.RaceOverAsync(rejoinable: true);   // the game ended while its race ran

        Assert.NotNull(session.RejoinableRace);
        Assert.Equal(offered, session.CanRejoin);
        Assert.Equal(offered, session.Rejoin());
    }

    /// <summary>What the game's exit code makes of its race (<see cref="AppState.KeepsTheRace"/>): kept for Rejoin
    /// when the game was closed while its seat raced, crashed or was ended from outside; a normal leave when it
    /// stopped by itself (0: the results left, a desync, closed after finishing or a knock-out, as a spectator), or
    /// when a game started again found its race over.</summary>
    [Theory]
    [InlineData(RecompLaunch.LeftRacingExitCode, true, false, true)]    // closed while its seat raced
    [InlineData(RecompLaunch.LeftRacingExitCode, true, true, true)]     // a rejoined game, closed mid-race again
    [InlineData(-1073741819, true, false, true)]                        // a crash (an access violation)
    [InlineData(-1, true, false, true)]                                 // ended from outside
    [InlineData(0, true, false, false)]                                 // its own stop
    [InlineData(RecompLaunch.RaceOverExitCode, true, true, false)]      // started again into a race that is over
    [InlineData(RecompLaunch.LeftRacingExitCode, false, false, false)]  // the race never ran in it
    [InlineData(null, true, false, false)]                              // no exit code to read
    public void TheGamesExitCodeDecidesWhetherTheRaceIsKept(int? code, bool raceRan, bool startedAgain, bool kept) =>
        Assert.Equal(kept, AppState.KeepsTheRace(code, raceRan, startedAgain));

    /// <summary>The player closed the game while their seat raced (<see cref="RecompLaunch.LeftRacingExitCode"/>):
    /// the race is kept, Rejoin is offered and the race server is not told the race is over. Closed by its own stop
    /// (0) it is told, and nothing is offered.</summary>
    [Theory]
    [InlineData(RecompLaunch.LeftRacingExitCode, true)]
    [InlineData(0, false)]
    public async Task AGameClosedMidRaceKeepsItsRaceAndTellsTheServerNothing(int code, bool kept)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, Start, Racing(false));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"),
            _ => started.TrySetResult());
        await session.JoinAsync(server.Address, cancel: cancel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        while (session.Lobby is not { Stage: LobbyState.Racing })
        {
            await Task.Delay(10, cancel);
        }

        await session.RaceOverAsync(rejoinable: AppState.KeepsTheRace(code, raceRan: true, startedAgain: false));
        Assert.Equal(kept, session.RejoinableRace is not null);
        Assert.Equal(kept, session.CanRejoin);

        await session.LeaveAsync();   // (a message after it: when the server has it, it has whatever came before)
        while (!server.Received.Any(m => m.Contains("\"leaveQueue\"", StringComparison.Ordinal)))
        {
            await Task.Delay(10, cancel);
        }
        Assert.Equal(!kept, server.Received.Any(m => m.Contains("\"raceOver\"", StringComparison.Ordinal)));
    }
}
