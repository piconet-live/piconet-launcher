using System.Net.WebSockets;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.Tests;

// A Quick Play join on its way (from RACE until the connection is made, has failed or is given up): it counts as
// being in Quick Play for whatever starts a game, it ends by itself when the server does not answer, and the player
// can leave it. And the other side: with a game open nothing is joined, and the left column says why.
public sealed partial class SetUpInGameTests
{
    /// <summary>The press is taken out of Quick Play, when the button is rightly on, and a join begins while the press
    /// waits for the controllers' check: the press looks again after its wait, and starts no game, though the
    /// connection is not made yet.</summary>
    [Fact]
    public async Task AJoinBegunWhileThePressChecksTheControllersStartsNoGame()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = State(disc: true, game: true);
        await using var server = new LedCoordinator();
        Task? join = null;
        try
        {
            var (checking, started, after) = await OnThePage(state, async (window, button, look) =>
            {
                File.WriteAllText(Hold, "");   // the check's helper does not answer yet
                Click(window, button);
                var checking = state.CheckingController;
                join = await Connecting(state, server);
                File.Delete(Hold);
                await Until(() => !state.CheckingController);
                var started = await Until(() => File.Exists(Started), 1);
                return (checking, started, look());
            });
            Assert.True(checking);    // the press was taken and was waiting for the controllers
            Assert.False(started);
            Assert.False(File.Exists(GameLaunchFile.PathFor(state.GamePaths)));
            Assert.Equal(new Seen(false, LeaveFirst), after);
        }
        finally
        {
            File.Delete(Hold);
            await End(state, server, join);
        }
    }

    /// <summary>A game started while a join was on its way stayed open when the connection landed, and the first
    /// match start then found a game open and gave the seat away. A join on its way counts as being in Quick Play:
    /// offline play starts nothing, in either form; and when the join has failed it starts again.</summary>
    [Fact]
    public async Task WhileAJoinIsOnItsWayOfflinePlayStartsNoGame()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = WithTheOnlinePart(State(disc: true, game: true));
        var server = new LedCoordinator();
        Task? join = null;
        try
        {
            join = await Connecting(state, server);
            Assert.Equal(AppState.InQuickPlayText, state.PlayOffline());
            Assert.Equal(AppState.InQuickPlayText, state.PlayOffline(controllerSetup: true));
            Assert.False(state.Racing);
            Assert.False(File.Exists(GameLaunchFile.PathFor(state.GamePaths)));
            Assert.Equal("", state.WhyNotJoin);                    // a second RACE has nothing to say (pressed: below)
            Assert.Equal(AppState.InQuickPlayText, state.Play(Path.Combine(_dir, "any.fzrp")));   // nor does a replay start a game
            Assert.False(state.Racing);
            await server.DisposeAsync();                           // the server goes: the join fails
            await Assert.ThrowsAnyAsync<Exception>(() => join);
            Assert.False(state.Joining);
            Assert.Null(state.WhyNotJoin);
            Assert.Null(state.PlayOffline());                      // out of Quick Play it starts
            Assert.Equal(AppState.GameOpenText, state.WhyNotJoin);   // and with the game open, RACE joins nothing
            Assert.True(state.Fork!.WaitForExit(TimeSpan.FromSeconds(10)));
            Assert.Null(state.WhyNotJoin);
        }
        finally
        {
            await End(state, server);
        }
    }

    /// <summary>The launcher's play control over a state, on the application's thread.</summary>
    private Task<T> WithTheControl<T>(AppState state, Func<MainViewModel, Task<T>> steps) =>
        application.Session.Dispatch(async () =>
        {
            using var main = new MainViewModel(state);
            return await steps(main);
        }, TestContext.Current.CancellationToken);

    /// <summary>The state with the game's online part too (a folder with an image): what a race needs, and so what
    /// RACE and Join need. The launcher's look at the game is taken again, as after a preparation.</summary>
    private AppState WithTheOnlinePart(AppState state)
    {
        var bundle = Path.Combine(_dir, "fzgx");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "mod.bin"), "");
        state.Update(s => s with { Recomp = s.Recomp! with { Bundle = bundle } });
        Assert.Null(state.LookForTheGame().GetAwaiter().GetResult().RaceProblem);
        return state;
    }

    /// <summary>A state whose server is <paramref name="server"/> (a disc, and a game that is "installed", with its
    /// online part).</summary>
    private AppState StateOn(Uri server)
    {
        var state = WithTheOnlinePart(State(disc: true, game: true));
        state.Update(s => s with { Server = server.ToString(), NoServer = false });
        return state;
    }

    /// <summary>A server that takes the connection and never answers its opening kept the launcher joining for good:
    /// RACE and Join did nothing and said nothing, the game could not be started, and there was no way out but to
    /// close the launcher. The join is given up after its limit and says so, as a server that cannot be reached is
    /// said; meanwhile the left column shows it with its way out, and the game's button is off.</summary>
    [Fact]
    public async Task AJoinThatGetsNoAnswerIsGivenUpAfterItsLimitAndSaysSo()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        await using var server = new LedCoordinator();   // it takes the connection and says nothing
        var state = StateOn(server.Address);
        state.JoinLimit = TimeSpan.FromSeconds(1.5);
        var logged = new List<string>();
        state.Noted += logged.Add;
        try
        {
            var (during, after) = await WithTheControl(state, async main =>
            {
                var race = main.Control.GoCommand.ExecuteAsync(null);
                await Until(() => state.Joining && main.Control.InQuickPlay);
                var during = (main.Control.InQuickPlay, main.Control.Word, main.Control.Line1, main.Control.LeaveText, main.Control.GameEnabled,
                    state.PlayOffline(), state.Play(Path.Combine(_dir, "any.fzrp")));
                await race;
                // out of the queue and saying why: two refreshes of the column, the join's end (out of Quick Play, no
                // notice yet) and then the leave's (the notice); the test could read the column between them, the
                // notice still empty (1 of 6 loaded runs)
                await Until(() => !main.Control.InQuickPlay && main.Control.Notice.Length > 0);
                return (during, (state.Joining, state.Session is null, main.Control.Notice, main.Control.GameEnabled, main.Control.GoText));
            });
            Assert.Equal((true, "JOINING", "Connecting", "Leave Queue", false, "In Quick Play", "In Quick Play"), during);
            Assert.Equal((false, true, "Could not reach the server · no answer in 1.5 s", true, "RACE"), after);
            Assert.Contains(logged, l => l.Contains("NoAnswerException: no answer within 1.5 seconds", StringComparison.Ordinal));   // the whole error, in the log
            Assert.Null(state.PlayOffline());                      // and the game starts again
            Assert.True(state.Fork!.WaitForExit(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A join's error is said as a short label after "Could not reach the server ·"; one not named keeps
    /// its own message.</summary>
    [Fact]
    public void AJoinsErrorIsSaidAsAShortLabel() => Assert.Equal(
        ["address not valid", "refused", "server not found", "no answer in 10 s", "not a game server", "Something else"],
        new Exception[]
        {
            new UriFormatException("x"),
            new WebSocketException("x", new HttpRequestException("x", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused))),
            new WebSocketException("x", new HttpRequestException("x", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound))),
            new AppState.NoAnswerException(TimeSpan.FromSeconds(10), new OperationCanceledException()),
            new WebSocketException(WebSocketError.NotAWebSocket),
            new IOException("Something else."),
        }.Select(AppState.WhyNotReached));

    /// <summary>"Leave the queue" while connecting ends the connecting: nothing is said, and RACE is there again. A
    /// second RACE while the first is on its way joins nothing more.</summary>
    [Fact]
    public async Task LeavingWhileConnectingEndsTheJoinAndASecondRaceJoinsNothingMore()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        await using var server = new LedCoordinator();
        var state = StateOn(server.Address);
        try
        {
            var (second, after) = await WithTheControl(state, async main =>
            {
                var race = main.Control.GoCommand.ExecuteAsync(null);
                await Until(() => state.Joining && state.Session is not null);
                var session = state.Session;
                await main.Control.GoCommand.ExecuteAsync(null);   // RACE again: it returns at once
                var second = (state.Joining, ReferenceEquals(session, state.Session), main.Control.Notice);
                await main.Control.LeaveCommand.ExecuteAsync(null);
                await race;                                         // the first press ends, without an error
                await Until(() => !main.Control.InQuickPlay);
                return (second, (state.Joining, state.Session is null, main.Control.Notice, main.Control.GoText, main.Control.GameEnabled));
            });
            Assert.Equal((true, true, ""), second);
            Assert.Equal((false, true, "", "RACE", true), after);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A handler of the launcher's own "something changed" that throws as the join begins does not leave
    /// the launcher joining (the word was given before the part that undoes it).</summary>
    [Fact]
    public async Task AHandlerThatThrowsAsTheJoinBeginsDoesNotLeaveTheLauncherJoining()
    {
        var state = State(disc: false, game: false);
        var thrown = false;
        state.Changed += () =>
        {
            if (state.Joining && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("a page's own fault");
            }
        };
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => state.JoinAsync(new Uri("ws://127.0.0.1:9/ws")));
            Assert.True(thrown);
            Assert.False(state.Joining);
            Assert.Equal(AppState.NoDiscText, state.WhyNotJoin);   // (this state has no disc: that is what is said now)
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>RACE with a game open, pressed in the launcher's own play control: nothing is joined, and the left
    /// column says why, over a notice that was up already, for as long as the game is open. (It joined: the first
    /// match start then found a game open and gave the seat away.) With the game closed the older notice is back,
    /// and RACE goes for the server again. The Local network tab's Join is held to the same.</summary>
    [Fact]
    public async Task RaceAndTheLocalJoinWithAGameOpenJoinNothingAndSayWhy()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        // a server's address where nothing listens: a join that is tried fails at once, and says so
        var state = StateOn(new Uri("ws://127.0.0.1:9/ws"));
        File.WriteAllText(Stay, "");
        try
        {
            Assert.Null(state.PlayOffline());
            state.Report("An older notice.");
            var (open, local, closed, tried) = await WithTheControl(state, async main =>
            {
                await main.Control.GoCommand.ExecuteAsync(null);
                var open = (main.Control.Notice, main.Control.GameNote, state.Session is null, state.Joining);
                main.Lan.Address = "127.0.0.1:9";
                await main.Lan.JoinAddressCommand.ExecuteAsync(null);
                var local = (main.Lan.Notice, state.Session is null);
                File.Delete(Stay);
                await Until(() => !state.Racing);
                await Until(() => main.Control.Notice is { Length: > 0 } notice && notice != AppState.GameOpenText);
                var closed = main.Control.Notice;
                await main.Control.GoCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                return (open, local, closed, main.Control.Notice);
            });
            Assert.Equal(("", "Game running", true, false), open);   // said once, under Play F-Zero GX
            Assert.Equal(("Game running", true), local);
            Assert.Equal("An older notice.", closed);              // the reason went with the game
            Assert.Equal("Could not reach the server · refused", tried);   // it tried, now that it may
        }
        finally
        {
            File.Delete(Stay);
            state.Shutdown();
        }
    }
}
