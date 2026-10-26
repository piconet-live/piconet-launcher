using System.Net;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

public sealed class ServerStatusFeedTests
{
    [Theory]
    [InlineData("wss://piconet-coordinator.fly.dev/ws", "https://piconet-coordinator.fly.dev/api/status")]
    [InlineData("ws://127.0.0.1:47810/ws", "http://127.0.0.1:47810/api/status")]
    public void TheStatusIsAskedOfTheCoordinatorsHostOverHttp(string coordinator, string status) =>
        Assert.Equal(new Uri(status), ServerStatusFeed.StatusUri(coordinator));

    [Fact]
    public void NotAServerAddressHasNoStatus() => Assert.Null(ServerStatusFeed.StatusUri("not a server"));

    [Fact]
    public async Task AnUnansweredAskKeepsTheLastAnswerAndSaysItFailed()
    {
        var up = true;
        var answers = new Answers(_ => up
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ControlProtocolJson(Status())) }
            : throw new HttpRequestException("refused"));
        using var feed = new ServerStatusFeed(() => "wss://coordinator.test/ws", answers);
        await feed.AskAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, feed.Latest?.PlayersOnline);

        up = false;
        await feed.AskAsync(TestContext.Current.CancellationToken);

        Assert.True(feed.Failed);
        Assert.Equal(3, feed.Latest?.PlayersOnline);
        Assert.Equal(new Uri("https://coordinator.test/api/status"), answers.Asked[^1]);
    }

    [Fact]
    public async Task ANewServerStartsWithNoAnswer()
    {
        var server = "wss://one.test/ws";
        var answers = new Answers(r => r.RequestUri!.Host == "one.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ControlProtocolJson(Status())) }
            : new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var feed = new ServerStatusFeed(() => server, answers);
        await feed.AskAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(feed.Latest);

        server = "wss://two.test/ws";
        await feed.AskAsync(TestContext.Current.CancellationToken);

        Assert.Null(feed.Latest);
        Assert.True(feed.Failed);
    }

    [Fact]
    public async Task SomethingElseAnsweringIsNoAnswer()
    {
        using var feed = new ServerStatusFeed(() => "wss://coordinator.test/ws",
            new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>a page</html>") }));
        await feed.AskAsync(TestContext.Current.CancellationToken);
        Assert.True(feed.Failed);
        Assert.Null(feed.Latest);
    }

    /// <summary>The status reaches the pages checked (ServerMessages): an answer with its lists left out is a status
    /// with empty ones, and an answer longer than a coordinator's longest message is no answer.</summary>
    [Fact]
    public async Task AnAnswerIsCheckedBeforeAPageReadsIt()
    {
        var body = """{"name":"den","playersOnline":2,"lobbies":[{"id":"l1","capacity":2000000000,"players":[null]}]}""";
        using var feed = new ServerStatusFeed(() => "wss://coordinator.test/ws",
            new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        await feed.AskAsync(TestContext.Current.CancellationToken);
        Assert.False(feed.Failed);
        Assert.Empty(feed.Latest!.Races);
        Assert.Equal(60, Assert.Single(feed.Latest.Lobbies).Capacity);
        Assert.Empty(feed.Latest.Lobbies[0].Players);
        Assert.Null(ServerStatusFeed.QuickPlayLobby(feed.Latest)?.RaceId);

        body = "{\"name\":\"" + new string('x', ServerMessages.MaxBytes) + "\"}";
        await feed.AskAsync(TestContext.Current.CancellationToken);
        Assert.True(feed.Failed);
        Assert.Equal("den", feed.Latest.Name);
    }

    [Fact]
    public void QuickPlayGoesToTheFullestLobbyWithRoomThatIsNotStarting()
    {
        var status = Status(
            Lobby("full", 30, LobbyState.Racing),
            Lobby("starting", 20, LobbyState.Starting),
            Lobby("racing", 12, LobbyState.Racing),
            Lobby("gathering", 5, LobbyState.Forming));
        Assert.Equal("racing", ServerStatusFeed.QuickPlayLobby(status)?.Id);
        Assert.Null(ServerStatusFeed.QuickPlayLobby(Status(Lobby("choosing", 4, LobbyState.Choosing))));
        Assert.Null(ServerStatusFeed.QuickPlayLobby(Status()));
    }

    private static LobbyView Lobby(string id, int players, string stage) =>
        new(id, [.. Enumerable.Range(0, players).Select(i => new RacerView($"P{i}", 0, "#102030"))], 30, false, 0, stage);

    private static ServiceStatus Status(params LobbyView[] lobbies) =>
        new("test", DateTimeOffset.UnixEpoch, 3, lobbies, [], [], []);

    private static string ControlProtocolJson(ServiceStatus status) =>
        System.Text.Json.JsonSerializer.Serialize(status, ControlProtocol.Json);

    /// <summary>Answers the asks without a network, keeping what was asked.</summary>
    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Asked)
            {
                Asked.Add(request.RequestUri!);
            }
            return Task.FromResult(answer(request));
        }
    }
}
