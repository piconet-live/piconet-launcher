using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A coordinator that says what a test tells it to: the welcome, then each message, to the one launcher
/// that connects (what a hostile or broken server can make a launcher do).</summary>
internal sealed class FakeCoordinator : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _serving;

    public FakeCoordinator(params string[] messages)
    {
        // a free port is asked for and given back before the listener takes it: between the two, a test running
        // beside this one can be given the same port ("Address already in use", met once in about twenty runs of
        // the suite). Then another port is asked for.
        for (var attempt = 1; ; attempt++)
        {
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            try
            {
                listener.Start();
                _listener = listener;
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                listener.Close();
            }
        }
        _serving = ServeAsync(messages);
    }

    public int Port { get; }

    public Uri Address => new($"ws://127.0.0.1:{Port}/ws");

    public const string Welcome = """{"type":"welcome","playerId":"p1","serverName":"Den","version":3}""";

    /// <summary>The welcomed player in a lobby by themselves.</summary>
    public const string Lobby = """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":"ANA","machine":3,"color":{"r":1,"g":2,"b":3}}],"capacity":30,"votingMinimum":2}""";

    /// <summary>In place of a message: the server closes its side with this word (a close frame), as the real one
    /// does after the reason it ends a connection for, and goes on reading.</summary>
    public static string Close(string why) => ClosePrefix + why;

    /// <summary>In place of a message: the connection is dropped without a word.</summary>
    public const string Drop = "\0drop";

    private const string ClosePrefix = "\0close:";

    /// <summary>Done when the launcher has closed its side too, after the server's <see cref="Close"/>.</summary>
    public Task ClosedByLauncher => _closedByLauncher.Task;

    private readonly TaskCompletionSource _closedByLauncher = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every text message the launcher sent, in order.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> Received { get; } = new();

    private async Task ServeAsync(string[] messages)
    {
        try
        {
            var context = await _listener.GetContextAsync().ConfigureAwait(false);
            var socket = (await context.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
            foreach (var message in messages)
            {
                if (message == Drop)
                {
                    socket.Abort();
                    context.Response.Abort();
                    return;
                }
                if (message.StartsWith(ClosePrefix, StringComparison.Ordinal))
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, message[ClosePrefix.Length..], CancellationToken.None).ConfigureAwait(false);
                    continue;
                }
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            var buffer = new byte[4096];
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var got = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (got.MessageType == WebSocketMessageType.Close)
                {
                    _closedByLauncher.TrySetResult();
                }
                else if (got.MessageType == WebSocketMessageType.Text && got.EndOfMessage)
                {
                    Received.Enqueue(Encoding.UTF8.GetString(buffer, 0, got.Count));   // (the launcher's are short)
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // the launcher left, or the test is over
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Close();
        await _serving.ConfigureAwait(false);
    }
}

/// <summary>A replay's file is named after its match, and the match's id is the coordinator's word.</summary>
public sealed class SecurityFileNameTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("m7", "m7")]
    [InlineData("lobby-3_race-12", "lobby-3_race-12")]
    [InlineData("x\\..\\..\\..\\Users\\Public\\y", "x__________Users_Public_y")]
    [InlineData("../../../etc/cron.d/x", "_________etc_cron_d_x")]
    [InlineData("C:\\Windows\\x", "C__Windows_x")]
    [InlineData("a.bat:stream", "a_bat_stream")]
    [InlineData("", "match")]
    [InlineData(null, "match")]
    public void AMatchIdIsOnlyEverAPartOfAFileName(string? id, string part)
    {
        Assert.Equal(part, RecompLaunch.FileNamePart(id));
        var name = RecompLaunch.ReplayFileName(new DateTimeOffset(2026, 10, 6, 5, 1, 34, TimeSpan.Zero), id!);
        Assert.Equal($"20261006-050134-{part}.fzrp", name);
        Assert.Equal(name, Path.GetFileName(name));
        Assert.True(RecompLaunch.FileNamePart(new string('a', 500)).Length <= 40);
    }

    /// <summary>The whole way: a coordinator starts a match whose id is a path out of the replay folder. The race is
    /// recorded inside the folder all the same. (Before the check the launcher handed the game
    /// "replays/20261006-…-x/../../../outside.fzrp" to write.)</summary>
    [Fact]
    public async Task AMatchWhoseIdIsAPathIsStillRecordedInTheReplayFolder()
    {
        var cancel = TestContext.Current.CancellationToken;
        const string start = """
            {"type":"matchStart","matchId":"x/../../../outside","relay":{"id":"r1","host":"","port":47811},"token":"1f2e3d4c5b6a7988","seat":0,
             "setup":{"course":21,"laps":3,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}],"shuffleSeed":1,"cpuSeed":2}}
            """;
        // (a match start is acted on for a player in a lobby whose last race is over: each id in a visit of its own)
        var replays = Path.Combine(_dir, "data", "deep", "replays");
        var races = new List<ForkRace>();
        foreach (var message in new[] { start, start.Replace("x/../../../outside", "x\\\\..\\\\..\\\\outside", StringComparison.Ordinal) })
        {
            await using var server = new FakeCoordinator(FakeCoordinator.Welcome, FakeCoordinator.Lobby, message);
            var raced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Start(ForkRace race)
            {
                lock (races)
                {
                    races.Add(race);
                }
                raced.TrySetResult();
            }
            await using var session = new QuickPlaySession(new LauncherSettings { ReplayFolder = replays }, Path.Combine(_dir, "race"), Start);
            await session.JoinAsync(server.Address, cancel: cancel);
            await raced.Task.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        }
        Assert.Equal(2, races.Count);
        foreach (var race in races)
        {
            var file = race.ReplayFile!;
            Assert.Equal(Path.GetFullPath(replays), Path.GetDirectoryName(Path.GetFullPath(file)));
            Assert.DoesNotContain("..", Path.GetFileName(file), StringComparison.Ordinal);
            Assert.DoesNotContain('\\', Path.GetFileName(file));
        }
    }
}
