using Launcher.App;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A server that takes no players any more (its stack is retired: a new build has its own servers) says so
/// in its own words, and the launcher shows them as they are. Against a scripted coordinator
/// (<see cref="FakeCoordinator"/>) that answers the hello as the real one does.</summary>
public sealed class RetiredServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Words = "A new build is out. Get it from the Piconet Alpha Discord, channel #builds.";

    private static readonly string Refusal =
        """{"type":"quickPlayEnded","matchId":"","reason":"a new build is out","message":""" + System.Text.Json.JsonSerializer.Serialize(Words) + "}";

    private static async Task Until(Func<bool> met, CancellationToken cancel)
    {
        for (var i = 0; i < 500 && !met(); i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.True(met(), "the wait ran out");
    }

    [Fact]
    public void TheServersOwnWordsAreShownWhenItSentSome()
    {
        Assert.Equal(Words, QuickPlaySession.RemovedText("a new build is out", Words));
        Assert.Equal("Taken out of Quick Play (a new build is out)", QuickPlaySession.RemovedText("a new build is out"));
        Assert.Equal("Taken out of Quick Play (later)", QuickPlaySession.RemovedText("later", ""));
        Assert.Equal("Servers at capacity", QuickPlaySession.RemovedText("at_capacity"));
    }

    /// <summary>The words are a server's: kept as text within a chat message's length, like any other of its texts.</summary>
    [Fact]
    public void TheWordsAreCheckedLikeAnyTextOfAServer()
    {
        var read = Assert.IsType<QuickPlayEnded>(ServerMessages.Checked(Refusal));
        Assert.Equal(("a new build is out", Words), (read.Reason, read.Message));
        var long500 = Assert.IsType<QuickPlayEnded>(ServerMessages.Checked(
            """{"type":"quickPlayEnded","matchId":"","reason":"r","message":""" + System.Text.Json.JsonSerializer.Serialize(new string('x', 900) + "‮\n") + "}"));
        Assert.Equal(new string('x', ServerMessages.MaxChatLength), long500.Message);
        Assert.Null(Assert.IsType<QuickPlayEnded>(ServerMessages.Checked("""{"type":"quickPlayEnded","matchId":"","reason":"at_capacity"}""")).Message);
    }

    /// <summary>The launcher itself: RACE on a retired server ends at Home with the server's words as the notice,
    /// and the server's close after them says nothing more.</summary>
    [Fact]
    public async Task TheLauncherIsSentHomeWithTheServersWords()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new FakeCoordinator(Refusal, FakeCoordinator.Close("retired"));
        var state = AppState.Load(Path.Combine(_dir, "data"));
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => state.Problem is not null, cancel);
            Assert.Equal(Words, state.Problem);
            Assert.Null(state.Session);
            Assert.False(state.InQuickPlay);
            Assert.Null(session.Lobby);
            await Task.Delay(300, cancel);                    // the connection's end changes nothing
            Assert.Equal(Words, state.Problem);
        }
        finally
        {
            state.Shutdown();
        }
    }
}
