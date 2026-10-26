using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// A coordinator a test leads by the hand, message by message, and that keeps what the launcher sends it: for what
/// a real server does only by chance or with other players (a stage closed while a choice is on its way, a message
/// that comes late, a server restarted that names its first match as the one before named its first). It takes one
/// connection after another; <see cref="SendAsync"/> goes to the latest.
/// </summary>
internal sealed class LedServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _serving;
    private readonly List<(int Connection, ClientMessage Message, DateTimeOffset At)> _received = [];
    private volatile WebSocket? _socket;
    private volatile HttpListenerContext? _context;
    private int _connections;

    public LedServer()
    {
        // (a free port asked for and given back can be taken by a test beside this one: then another, as FakeCoordinator)
        for (var attempt = 1; ; attempt++)
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                (_listener, Address) = (listener, new Uri($"ws://127.0.0.1:{port}/ws"));
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                listener.Close();
            }
        }
        _serving = ServeAsync();
    }

    public Uri Address { get; }

    /// <summary>How many connections were taken so far.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>What the launcher sent on the latest connection, in order, each with when it came.</summary>
    public IReadOnlyList<(ClientMessage Message, DateTimeOffset At)> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received.Where(r => r.Connection == _connections).Select(r => (r.Message, r.At))];
            }
        }
    }

    public IReadOnlyList<T> Of<T>() where T : ClientMessage => [.. Received.Select(r => r.Message).OfType<T>()];

    public async Task SendAsync(params ServerMessage[] messages)
    {
        for (var i = 0; i < 500 && _socket is null; i++)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
        foreach (var message in messages)
        {
            await _socket!.SendAsync(Encoding.UTF8.GetBytes(ControlProtocol.Encode(message)), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The latest connection ends without a word, as a server that was stopped ends it.</summary>
    public void Drop()
    {
        _socket?.Abort();
        _context?.Response.Abort();
    }

    /// <summary>A lobby of this player (p1, ANA) and one other, as a server tells it.</summary>
    public static LobbyState Lobby(string stage, int machine = 6, bool chosen = false, bool racing = false, string? waiting = null, bool otherChosen = false) =>
        new("l1",
            [
                new PlayerInfo("p1", "ANA", machine, new Rgb(1, 2, 3), 0, racing, chosen),
                new PlayerInfo("p2", "BEN", 2, new Rgb(9, 9, 200), 0, racing, otherChosen),
            ], 30, 2, stage == LobbyState.Forming, 0, 0, stage, Waiting: waiting);

    /// <param name="setting">The machine-setting rule; null: Quick Play's (100 for everyone).</param>
    public static Welcome Welcome(IReadOnlyList<int>? machines = null, SettingRange? setting = null) =>
        new("p1", "Led", ControlProtocol.Version, new ServerRules(3, 30, CpuFillMode.Never, CourseSelectMode.Vote, null,
            machines ?? [.. Enumerable.Range(0, GameMachines.Count)], setting ?? SettingRange.QuickPlay, new RaceRules()));

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                var socket = (await context.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
                var connection = Interlocked.Increment(ref _connections);
                (_context, _socket) = (context, socket);
                _ = ReadAsync(socket, connection);
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // the test is over
        }
    }

    private async Task ReadAsync(WebSocket socket, int connection)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var text = new StringBuilder();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                    text.Append(Encoding.UTF8.GetString(buffer, 0, part.Count));
                }
                while (!part.EndOfMessage);
                if (part.MessageType == WebSocketMessageType.Text && ControlProtocol.DecodeClient(text.ToString()) is { } message)
                {
                    lock (_received)
                    {
                        _received.Add((connection, message, DateTimeOffset.UtcNow));
                    }
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // the launcher left, or the connection was dropped
        }
    }

    public async ValueTask DisposeAsync()
    {
        Drop();
        _listener.Close();
        await _serving.ConfigureAwait(false);
    }
}
