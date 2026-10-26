using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The connection to a coordinator takes its messages in on one loop (<see cref="CoordinatorClient"/>): an
/// error raised while a message is taken in ended that loop without a word, and came back at the player's next
/// Join.</summary>
public sealed class SecurityReceiveLoopTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task Until(Func<bool> met, CancellationToken cancel)
    {
        for (var i = 0; i < 500 && !met(); i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.True(met(), "the wait ran out");
    }

    private QuickPlaySession Session() =>
        new(new LauncherSettings { Racer = new RacerProfile("ANA", 3, new Rgb(200, 0, 0)), RecordReplays = false }, Path.Combine(_dir, "ANA"), _ => { });

    /// <summary>A lobby names the players of other builds, by build, and the launcher adds them up for its notice:
    /// two counts near the largest number overflowed the sum. A count no service has is left out, and the session
    /// goes on.</summary>
    [Fact]
    public async Task ACountOfOtherPlayersNoServiceHasIsLeftOutAndTheSessionGoesOn()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome,
            """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":"ANA","machine":3,"color":{"r":1,"g":2,"b":3}}],"capacity":30,"votingMinimum":2,"otherBuilds":[{"build":"a/b/c","players":2147483647},{"build":"a/b/d","players":2147483647},{"build":"a/b/e","players":-5},{"build":"a/b/f","players":0},{"build":"a/b/g","players":2}]}""",
            """{"type":"chat","text":"last"}""");
        var session = Session();
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.Chat.Count == 1 || (session.Lobby is not null && session.ConnectedTo is null), cancel);
        Assert.NotNull(session.ConnectedTo);                                   // the loop went on to the next message
        Assert.Single(session.Chat);
        Assert.Null(session.Fault);
        Assert.Equal([new OtherBuild("a/b/g", 2)], session.Lobby!.OtherBuilds);
        Assert.StartsWith("Lobby · 1 of 30. 2 players online on another version", session.Status, StringComparison.Ordinal);
        await session.DisposeAsync();
    }

    /// <summary>The notice itself adds up whatever it is given.</summary>
    [Fact]
    public void TheNoticeOfOtherBuildsAddsUpAnyCounts()
    {
        var text = QuickPlaySession.OtherBuildsText("a/b/c", [new OtherBuild("a/b/d", int.MaxValue), new OtherBuild("a/b/e", int.MaxValue)]);
        Assert.StartsWith("4294967294 players online on another version", text, StringComparison.Ordinal);
    }

    /// <summary>Whatever else goes wrong while a message is taken in (here a screen's own fault, raised as the
    /// session tells it of the message): the connection ends there, the player reads at once that it ended and
    /// that the launcher's side failed, the error is kept for the log, and leaving (which the next Join does first)
    /// does not bring it back.</summary>
    [Fact]
    public async Task AnErrorWhileAMessageIsTakenInEndsTheConnectionWhereThePlayerSeesIt()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, """{"type":"chat","text":"one"}""");
        var session = Session();
        var thrown = 0;
        session.Changed += () =>
        {
            if (session.Chat.Count == 1 && Interlocked.Exchange(ref thrown, 1) == 0)
            {
                throw new InvalidOperationException("a screen's fault");
            }
        };
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.ConnectedTo is null, cancel);
        Assert.Equal(1, thrown);
        Assert.Equal($"Disconnected ({CoordinatorClient.FaultText})", session.Status);
        Assert.Equal("a screen's fault", Assert.IsType<InvalidOperationException>(session.Fault).Message);
        await session.DisposeAsync();
    }

    /// <summary>The review's case: the connection is lost during a race. The launcher goes on handing the race to
    /// the session ten times a second without waiting (AppState), and says the race is over when the game closes:
    /// every one of those sends raised an error nobody read, which the handler of last resort told the player as
    /// "Something went wrong" and wrote until its log was full. A send on a connection that has ended does
    /// nothing; the end is said once, by the session's status.</summary>
    [Fact]
    public async Task WhatIsSentAfterTheConnectionEndedIsNoError()
    {
        var cancel = TestContext.Current.CancellationToken;
        var server = new FakeCoordinator(FakeCoordinator.Welcome,
            """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":"ANA","machine":3,"color":{"r":1,"g":2,"b":3}}],"capacity":30,"votingMinimum":2}""",
            """{"type":"matchStart","matchId":"m1","relay":{"id":"r","host":"127.0.0.1","port":47811},"token":"1","seat":0,"reportLive":true,"setup":{"course":1,"laps":1,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}]}}""");
        var started = 0;
        var session = new QuickPlaySession(new LauncherSettings { RecordReplays = false }, Path.Combine(_dir, "race"), _ => started++);
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => started == 1, cancel);
        Assert.True(session.Race!.Start.ReportLive);
        await server.DisposeAsync();                                           // the connection is gone
        await Until(() => session.ConnectedTo is null, cancel);
        Assert.StartsWith("Racing", session.Status, StringComparison.Ordinal); // (a race's status stays while it runs)

        // as the launcher sends them, twenty at once and not waited for: a task that ends in an error nobody reads
        // is what reached the handler of last resort
        var sends = Enumerable.Range(0, 20).Select(i => session.ReportAsync(new RaceSnapshot(i, 0, []))).ToList();
        await Task.WhenAll(sends);
        Assert.All(sends, send => Assert.True(send.IsCompletedSuccessfully));
        // and every other thing a page or the launcher sends
        await session.ChatAsync("anyone there?");
        await session.VoteStartAsync(true);
        await session.VoteCpuFillAsync(false);
        await session.RaceOverAsync();
        await session.LeaveAsync();
        Assert.Null(session.Fault);
        await session.DisposeAsync();
        await session.ReportAsync(new RaceSnapshot(99, 0, []));                // and after the session is put away
        await session.ChatAsync("still nobody");
    }
}
