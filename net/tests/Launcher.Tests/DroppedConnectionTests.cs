using Launcher.App;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A connection that ends without the player's doing is said, with the server's reason when it gave one: a
/// player the server had limited for sending too fast saw only the lobby go away. Against a scripted coordinator
/// (<see cref="FakeCoordinator"/>) that says what the real one says and closes as it closes.</summary>
public sealed class DroppedConnectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string RateLimited = """{"type":"error","code":"rate_limited","message":"too many messages; the connection is closed"}""";

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

    /// <summary>The server's reason, then its close: the session says the reason once, and the launcher closes its
    /// own side, so the server has nothing to wait for.</summary>
    [Fact]
    public async Task ALimitedPlayerIsToldWhyAndTheLauncherAnswersTheClose()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, RateLimited, FakeCoordinator.Close("rate_limited"));
        var told = new List<string>();
        await using var session = Session();
        session.Dropped += told.Add;
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => told.Count > 0, cancel);
        Assert.Equal(["Disconnected by the server · too many messages"], told);
        Assert.Null(session.ConnectedTo);
        Assert.Equal("rate_limited", session.LastError?.Code);
        await server.ClosedByLauncher.WaitAsync(TimeSpan.FromSeconds(10), cancel);
    }

    /// <summary>The words: the server's own for a reason this launcher has none for; none of a reason that was not
    /// the server's last word.</summary>
    [Fact]
    public void TheWordsForAConnectionThatEnded()
    {
        Assert.Equal("Lost the connection to the server", QuickPlaySession.DroppedText(null));
        Assert.Equal("Disconnected by the server · too many messages",
            QuickPlaySession.DroppedText(new ErrorMessage("rate_limited", "too many messages; the connection is closed")));
        Assert.Equal("Disconnected by the server · a new build is out", QuickPlaySession.DroppedText(new ErrorMessage("later", "a new build is out")));
        Assert.Equal("Disconnected by the server", QuickPlaySession.DroppedText(new ErrorMessage("later", "")));
    }

    /// <summary>A connection that is cut, and one the server closes after an error that was not its last word (a
    /// chat came after): lost, with no reason made up.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AConnectionThatIsCutIsSaidAsLost(bool closed)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby,
            """{"type":"error","code":"bad_text","message":"empty message"}""", """{"type":"chat","text":"hi"}""",
            closed ? FakeCoordinator.Close("") : FakeCoordinator.Drop);
        var told = new List<string>();
        await using var session = Session();
        session.Dropped += told.Add;
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => told.Count > 0, cancel);
        Assert.Equal(["Lost the connection to the server"], told);
        Assert.Null(session.ConnectedTo);
    }

    /// <summary>Leaving is the player's own doing: nothing is said.</summary>
    [Fact]
    public async Task LeavingSaysNothing()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby);
        var told = new List<string>();
        var session = Session();
        session.Dropped += told.Add;
        await session.JoinAsync(server.Address, cancel: cancel);
        await Until(() => session.Lobby is not null, cancel);
        await session.LeaveAsync();
        await session.DisposeAsync();
        await Task.Delay(200, cancel);
        Assert.Empty(told);
    }

    /// <summary>The launcher itself (its shared state): the reason stands where a server that cannot be reached is
    /// said, the player is out of Quick Play, and the next join clears it.</summary>
    [Fact]
    public async Task TheLauncherShowsTheReasonInItsNoticeAndIsOutOfQuickPlay()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, RateLimited, FakeCoordinator.Close("rate_limited"));
        await using var next = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby);
        var state = AppState.Load(Path.Combine(_dir, "data"));
        try
        {
            await state.JoinAsync(server.Address);
            await Until(() => state.Problem is not null, cancel);
            Assert.Equal("Disconnected by the server · too many messages", state.Problem);
            Assert.False(state.InQuickPlay);

            var again = await state.JoinAsync(next.Address);
            await Until(() => again.Lobby is not null, cancel);
            Assert.Null(state.Problem);
            Assert.True(state.InQuickPlay);
            await state.LeaveAsync();
            Assert.Null(state.Problem);                       // leaving is not a lost connection
        }
        finally
        {
            state.Shutdown();
        }
    }
}
