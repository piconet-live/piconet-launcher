using System.Net;
using Launcher.Core;

namespace Launcher.Tests;

public sealed class ServerHealthTests
{
    [Theory]
    [InlineData("wss://piconet-coordinator.fly.dev/ws", "https://piconet-coordinator.fly.dev/health")]
    [InlineData("ws://127.0.0.1:47810/ws", "http://127.0.0.1:47810/health")]
    [InlineData("wss://example.org:8443/ws?x=1", "https://example.org:8443/health")]
    [InlineData("  ws://lan-box/ws  ", "http://lan-box/health")]
    [InlineData("https://example.org/ws", "https://example.org/health")]
    public void TheHealthCheckIsTheCoordinatorsHostOverHttp(string coordinator, string health) =>
        Assert.Equal(new Uri(health), ServerHealth.HealthUri(coordinator));

    [Theory]
    [InlineData("")]
    [InlineData("piconet-coordinator.fly.dev")]
    [InlineData("ftp://example.org/ws")]
    public void NotAServerAddressHasNoHealthCheck(string coordinator) => Assert.Null(ServerHealth.HealthUri(coordinator));

    [Theory]
    [InlineData("wss://piconet-coordinator.fly.dev/ws", "piconet-coordinator.fly.dev")]
    [InlineData("ws://127.0.0.1:47810/ws", "127.0.0.1:47810")]
    public void TheServerShowsByItsHost(string coordinator, string shown) => Assert.Equal(shown, ServerHealth.HostOf(coordinator));

    [Fact]
    public void AnAnswerIsOnlineASlowOneSlowNoneOffline()
    {
        Assert.Equal(Connectivity.Online, ServerHealth.Judge(TimeSpan.FromMilliseconds(80)));
        Assert.Equal(Connectivity.Slow, ServerHealth.Judge(ServerHealth.SlowAfter + TimeSpan.FromMilliseconds(1)));
        Assert.Equal(Connectivity.Offline, ServerHealth.Judge(null));
    }

    [Fact]
    public async Task ItAsksTheHealthPathAndAnOkIsOnline()
    {
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var health = new ServerHealth(() => "wss://coordinator.test/ws", () => null, answers);
        Assert.Equal(Connectivity.Checking, health.State);

        await health.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Connectivity.Online, health.State);
        Assert.Equal(new Uri("https://coordinator.test/health"), Assert.Single(answers.Asked));
        Assert.NotNull(health.LastCheck);
        Assert.NotNull(health.LastAnswer);
    }

    [Fact]
    public async Task AnErrorOrNoAnswerIsOffline()
    {
        using var failing = new ServerHealth(() => "wss://coordinator.test/ws", () => null,
            new Answers(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await failing.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Connectivity.Offline, failing.State);
        Assert.Null(failing.LastAnswer);

        using var refused = new ServerHealth(() => "wss://coordinator.test/ws", () => null,
            new Answers(_ => throw new HttpRequestException("refused")));
        await refused.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Connectivity.Offline, refused.State);
        Assert.NotNull(refused.LastCheck);
    }

    /// <summary>The check needs the status line only. A server (a LAN server is anyone's computer) that answers
    /// /health with a body of 48 MiB had all of it read into memory (112 MiB allocated, the review measured): the
    /// body is not read now, and the server finds the connection closed long before it has sent it.</summary>
    [Fact]
    public async Task AHealthAnswersBodyIsNotRead()
    {
        var cancel = TestContext.Current.CancellationToken;
        const long body = 48L << 20;
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        long sent = 0;
        var hungUp = false;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var request = new System.Text.StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buffer, cancel);
                if (n == 0)
                {
                    break;
                }
                request.Append(System.Text.Encoding.ASCII.GetString(buffer, 0, n));
            }
            try
            {
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body}\r\n\r\n"), cancel);
                var chunk = new byte[1 << 16];
                while (sent < body)
                {
                    await stream.WriteAsync(chunk, cancel);
                    sent += chunk.Length;
                }
            }
            catch (IOException)
            {
                hungUp = true;   // the asker closed the connection
            }
        }, cancel);
        using var health = new ServerHealth(() => $"ws://127.0.0.1:{port}/ws", () => null);

        await health.CheckAsync(cancel);
        await serving.WaitAsync(TimeSpan.FromSeconds(20), cancel);

        Assert.Equal(Connectivity.Online, health.State);
        Assert.True(hungUp, "the whole body was taken");
        Assert.True(sent < body / 2, $"{sent >> 20} MiB of the body were taken");
    }

    [Fact]
    public async Task NotAServerAddressIsOfflineWithoutAsking()
    {
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var health = new ServerHealth(() => "not a server", () => null, answers);
        await health.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Connectivity.Offline, health.State);
        Assert.Empty(answers.Asked);
    }

    [Fact]
    public async Task ANewServerStartsOverAndIsAskedAtOnce()
    {
        var server = "wss://one.test/ws";
        var answers = new Answers(r => new HttpResponseMessage(r.RequestUri!.Host == "one.test" ? HttpStatusCode.OK : HttpStatusCode.BadGateway));
        using var health = new ServerHealth(() => server, () => null, answers);
        await health.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Connectivity.Online, health.State);

        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        health.Changed += () =>
        {
            if (health.LastCheck is not null)
            {
                asked.TrySetResult();
            }
        };
        server = "ws://two.test:9000/ws";
        health.Refresh();
        Assert.Equal("ws://two.test:9000/ws", health.Server);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(Connectivity.Offline, health.State);
        Assert.Equal(new Uri("http://two.test:9000/health"), answers.Asked[^1]);
    }

    [Fact]
    public void QuickPlayOnTheServerIsOnlineWhateverTheCheckSaid()
    {
        Uri? connected = null;
        using var health = new ServerHealth(() => "wss://coordinator.test/ws", () => connected,
            new Answers(_ => throw new HttpRequestException("refused")));
        Assert.Equal(Connectivity.Checking, health.State);

        connected = new Uri("wss://coordinator.test/ws");
        Assert.Equal(Connectivity.Online, health.State);
        Assert.True(health.InQuickPlay);

        // Quick Play on a LAN server says nothing about the online one
        connected = new Uri("ws://192.168.1.20:47810/ws");
        Assert.Equal(Connectivity.Checking, health.State);
    }

    [Fact]
    public async Task NothingListeningIsOffline()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var health = new ServerHealth(() => $"ws://127.0.0.1:{port}/ws", () => null);
        await health.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Connectivity.Offline, health.State);
    }

    /// <summary>Answers the checks without a network, keeping what was asked.</summary>
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
