using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A coordinator the test leads by the hand: it takes the launcher's connection only when told (until then
/// the launcher is "Connecting"), sends what it is given, and drops the connection when told. It listens on a port
/// the system gave it and keeps, and answers the WebSocket's opening itself.</summary>
internal sealed class LedCoordinator : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly TaskCompletionSource _take = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<WebSocket> _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serving;

    public LedCoordinator()
    {
        _listener.Start();
        _serving = ServeAsync();
    }

    public Uri Address => new($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/ws");

    /// <summary>The player's lobby while its race is on, with the player in the race.</summary>
    public const string LobbyRacing = """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":"ANA","machine":3,"color":{"r":1,"g":2,"b":3},"racing":true}],"capacity":30,"votingMinimum":2,"stage":"racing"}""";

    public const string MatchStart = """
        {"type":"matchStart","matchId":"m1","relay":{"id":"r1","host":"","port":47811},"token":"1f2e3d4c5b6a7988","seat":0,
         "setup":{"course":21,"laps":3,"seats":[{"seat":0,"playerId":"p1","name":"ANA","machine":3}],"shuffleSeed":1,"cpuSeed":2}}
        """;

    /// <summary>The connection is taken from here on: the launcher's join completes.</summary>
    public void Take() => _take.TrySetResult();

    public async Task SendAsync(params string[] messages)
    {
        var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        foreach (var message in messages)
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The connection ends without a word, as a network that went away ends it.</summary>
    public async Task DropAsync() => (await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Abort();

    private async Task ServeAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            var stream = client.GetStream();
            // the opening request, to its empty line; the answer is held back until the test says so
            var request = new StringBuilder();
            var one = new byte[1];
            while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one).ConfigureAwait(false) == 1)
            {
                request.Append((char)one[0]);
            }
            var key = request.ToString().Split("\r\n").First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim();
            await _take.Task.ConfigureAwait(false);
#pragma warning disable CA5350 // the WebSocket protocol answers an opening with the SHA-1 of its key
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore CA5350
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n")).ConfigureAwait(false);
            using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
            _socket.TrySetResult(socket);
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or SocketException or IOException or ObjectDisposedException or InvalidOperationException
            or OperationCanceledException)
        {
            // the launcher left, the connection was dropped, or the test is over
        }
    }

    public async ValueTask DisposeAsync()
    {
        _take.TrySetCanceled();
        _listener.Stop();
        if (_socket.Task.IsCompletedSuccessfully)
        {
            _socket.Task.Result.Abort();
        }
        await _serving.ConfigureAwait(false);
    }
}

// "Set up in the game" in Quick Play: off with its reason in the queue or a lobby and in a race, and following the
// connection as it is made and lost, with the page open all the while (the session alone says those changes). The
// second half guards a trap: the button, on while "Connecting", staying on once connected, and
// its press starting the game in Quick Play, where the next match start found a game open and gave the seat away.
public sealed partial class SetUpInGameTests
{
    private const string LeaveFirst = "In Quick Play";

    /// <summary>The launcher's join on its way to <paramref name="server"/>, which has not taken it: "Connecting".</summary>
    private static async Task<Task> Connecting(AppState state, LedCoordinator server)
    {
        var join = state.JoinAsync(server.Address);
        for (var i = 0; i < 400 && state.Session is not { Status: "Connecting" }; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.Equal("Connecting", state.Session?.Status);
        Assert.False(state.InQuickPlay);
        Assert.True(state.Joining);
        return join;
    }

    /// <summary>The launcher in a lobby of <paramref name="server"/>'s, by itself.</summary>
    private static async Task InALobby(AppState state, LedCoordinator server)
    {
        server.Take();
        var session = await state.JoinAsync(server.Address);
        await server.SendAsync(FakeCoordinator.Welcome, FakeCoordinator.Lobby);
        for (var i = 0; i < 400 && session.Lobby is null; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.NotNull(session.Lobby);
        Assert.True(state.InQuickPlay);
    }

    /// <summary>Puts the launcher's visit away, and the server; and the join, when its connection was never taken
    /// (it ends when the server has gone).</summary>
    private static async Task End(AppState state, LedCoordinator server, Task? join = null)
    {
        state.Shutdown();
        await server.DisposeAsync();
        try
        {
            await (join ?? Task.CompletedTask);
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException)
        {
            // the test failed before the connection was taken
        }
    }

    [Fact]
    public async Task InQuickPlayTheButtonIsOffAndSaysToLeaveFirst()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = State(disc: true, game: true);
        await using var server = new LedCoordinator();
        try
        {
            await InALobby(state, server);
            var (lobby, left) = await OnThePage(state, async (window, button, look) =>
            {
                var lobby = look();
                Click(window, button);
                await Until(() => File.Exists(Started), 1);
                await state.LeaveAsync();
                await Until(() => look().Enabled, 5);
                return (lobby, look());
            });
            Assert.Equal(new Seen(false, LeaveFirst), lobby);
            Assert.False(File.Exists(Started));                                       // the press started nothing
            Assert.False(File.Exists(GameLaunchFile.PathFor(state.GamePaths)));
            Assert.Equal(new Seen(true, ControllerViewModel.SetUpInGameWhat), left);   // out of Quick Play: it can be pressed
        }
        finally
        {
            await End(state, server);
        }
    }

    /// <summary>The start itself looks, for both buttons that come to it ("Play F-Zero GX" asks before it leaves
    /// Quick Play, and waits for the controllers' check after its look: a join can land in that wait).</summary>
    [Fact]
    public async Task InQuickPlayOfflinePlayStartsNoGame()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = State(disc: true, game: true);
        await using var server = new LedCoordinator();
        try
        {
            await InALobby(state, server);
            Assert.Equal("In Quick Play", state.PlayOffline());
            Assert.Equal(AppState.InQuickPlayText, state.PlayOffline(controllerSetup: true));
            Assert.False(state.Racing);
            Assert.False(File.Exists(GameLaunchFile.PathFor(state.GamePaths)));
            await state.LeaveAsync();
            Assert.Null(state.PlayOffline());   // out of Quick Play it starts
            Assert.True(state.Fork!.WaitForExit(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await End(state, server);
        }
    }

    [Fact]
    public async Task InARaceTheButtonSaysSo()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var state = State(disc: true, game: true);
        // a race needs the game's online part: a folder with an image
        var bundle = Path.Combine(_dir, "fzgx");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "mod.bin"), "");
        state.Update(s => s with { Recomp = s.Recomp! with { Bundle = bundle } });
        Assert.Null(RecompLaunch.RaceProblem(state.GamePaths));
        File.WriteAllText(Stay, "");   // the race's game stays open until the test lets it go
        await using var server = new LedCoordinator();
        try
        {
            await InALobby(state, server);
            var (racing, launch, after) = await OnThePage(state, async (window, button, look) =>
            {
                // the match starts with the page open: the session says so, the launcher's game starts
                await server.SendAsync(LedCoordinator.MatchStart, LedCoordinator.LobbyRacing);
                await Until(() => state.Racing && state.Session is { Racing: true } && File.Exists(Started));
                Dispatcher.UIThread.RunJobs();
                var racing = look();
                Click(window, button);
                await Until(() => false, 1);
                var launch = File.ReadAllText(GameLaunchFile.PathFor(state.GamePaths));
                File.Delete(Stay);
                await Until(() => !state.Racing);
                await Until(() => look().Note == LeaveFirst, 5);
                return (racing, launch, look());
            });
            Assert.Equal(new Seen(false, ""), racing);                                     // (the left column says "In a race")
            Assert.DoesNotContain("controllerSetup", launch, StringComparison.Ordinal);   // the game that ran was the race's
            Assert.Single(File.ReadAllLines(Started), "--config");                         // and the only one started
            Assert.Equal(new Seen(false, LeaveFirst), after);                              // back in the lobby
        }
        finally
        {
            File.Delete(Stay);
            await End(state, server);
        }
    }

    /// <summary>The page is open while the connection is made, and then lost. Nothing of the launcher's own says
    /// either (<see cref="AppState.Changed"/> came before the connection): the button follows the session. It is
    /// off from the moment the join is on its way.</summary>
    [Fact]
    public async Task TheButtonFollowsAConnectionMadeAndLostWhileThePageIsOpen()
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
            join = await Connecting(state, server);
            var (connecting, connected, pressed, dropped, started) = await OnThePage(state, async (window, button, look) =>
            {
                var connecting = look();
                // from "a join is on its way" to "in Quick Play" the button never comes on: looked at every 5 ms
                // while the connection is made (the launcher's word of the join's end and the session's of its
                // connection are two events)
                var cameOn = false;
                server.Take();
                for (var i = 0; i < 2000 && !(state.InQuickPlay && !state.Joining); i++)
                {
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                    Dispatcher.UIThread.RunJobs();
                    cameOn |= look().Enabled;
                }
                Assert.True(state.InQuickPlay && !state.Joining);
                Dispatcher.UIThread.RunJobs();
                var connected = look() with { Enabled = look().Enabled || cameOn };
                Click(window, button);
                var pressed = await Until(() => File.Exists(Started), 1);
                await server.DropAsync();
                await Until(() => !state.InQuickPlay);
                await Until(() => look().Enabled, 5);
                var dropped = look();
                Click(window, button);   // and it works again: the game starts on its controller options
                var started = await Until(() => File.Exists(Started));
                await Until(() => !state.Racing);
                return (connecting, connected, pressed, dropped, started);
            });
            Assert.Equal(new Seen(false, LeaveFirst), connecting);   // a join on its way counts as Quick Play
            Assert.Equal(new Seen(false, LeaveFirst), connected);
            Assert.False(pressed);   // no game was started in Quick Play
            Assert.Equal(new Seen(true, ControllerViewModel.SetUpInGameWhat), dropped);
            Assert.True(started);
            Assert.Contains("controllerSetup", File.ReadAllText(GameLaunchFile.PathFor(state.GamePaths)), StringComparison.Ordinal);
        }
        finally
        {
            await End(state, server, join);
        }
    }
}
