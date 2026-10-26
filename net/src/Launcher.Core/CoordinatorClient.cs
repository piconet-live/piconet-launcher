using System.Net.WebSockets;
using System.Text;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The launcher's connection to a coordinator: one WebSocket of JSON messages. It says hello with the
/// player's name, and answers the coordinator's relay measurements by itself (pinging each relay over UDP) and its
/// pings (at once, also while it measures); every other message reaches <see cref="Received"/>, on the receiving
/// thread, in the order it came. Matches start with <see cref="MatchStart"/>.
/// Nothing is read until <see cref="Received"/> has a handler: the coordinator answers the hello at once (welcome, or
/// the error that refuses it), and an answer read before anyone listened was lost, which on a busy computer happened
/// (the session then waited for ever for a welcome that had come and gone).
/// </summary>
public sealed partial class CoordinatorClient : ICoordinatorConnection
{
    private readonly ClientWebSocket _socket = new() { Options = { KeepAliveInterval = ControlProtocol.KeepAlive } };
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly TaskCompletionSource _listened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _handlers = new();
    private Action<ServerMessage>? _received;
    private Task _receiving = Task.CompletedTask;

    private CoordinatorClient(Uri server) => Server = server;

    public Uri Server { get; }

    /// <summary>Every message from the coordinator but the relay measurements and pings, which this client answers.
    /// Reading begins with the first handler, so none is missed: attach <see cref="Closed"/> before it.</summary>
    public event Action<ServerMessage>? Received
    {
        add
        {
            lock (_handlers)
            {
                _received += value;
            }
            _listened.TrySetResult();
        }
        remove
        {
            lock (_handlers)
            {
                _received -= value;
            }
        }
    }

    /// <summary>The connection ended (the reason; <see cref="Stopped"/>: by the launcher itself).</summary>
    public event Action<string>? Closed;

    /// <summary>The reason of <see cref="Closed"/> when the launcher ended the connection (<see cref="DisposeAsync"/>).</summary>
    public const string Stopped = ICoordinatorConnection.Stopped;

    /// <summary>The reason of <see cref="Closed"/> when the connection ended on an error raised while a message was
    /// taken in (<see cref="Fault"/>), as the player reads it.</summary>
    public const string FaultText = "the launcher could not use a message from the server";

    /// <summary>The error that ended the connection while a message was taken in, for the launcher's log (null: it
    /// ended otherwise, or has not ended).</summary>
    public Exception? Fault { get; private set; }

    /// <param name="build">The player's game build (<see cref="Hello.Build"/>; null: none named).</param>
    /// <param name="debug">A test setting that reaches a race is on in this launcher (<see cref="Hello.Debug"/>,
    /// <see cref="DebugSettings.ReachesARace"/>): a server that takes none answers the hello with an error.</param>
    /// <param name="key">The launcher's secret, which the player's tag is worked out from (<see cref="Hello.Key"/>;
    /// null: none, a random tag).</param>
    public static async Task<CoordinatorClient> ConnectAsync(
        Uri server, string name, string? build = null, bool debug = false,
        string? key = null, CancellationToken cancel = default)
    {
        var client = new CoordinatorClient(server);
        try
        {
            await client._socket.ConnectAsync(server, cancel).ConfigureAwait(false);
            await client.SendNowAsync(new Hello(name, ControlProtocol.Version, build, VoteCounts: true, Debug: debug, StatPacks: true, Pings: true, Notices: true, Key: key), cancel).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        client._receiving = Task.Run(() => client.ReceiveLoopAsync(client._stop.Token), CancellationToken.None);
        return client;
    }

    /// <summary><see cref="ConnectAsync"/> as a <see cref="CoordinatorConnect"/>: how the launcher's sessions reach a
    /// real coordinator.</summary>
    public static async Task<ICoordinatorConnection> ConnectionAsync(
        Uri server, string name, string? build, bool debug, string? key, CancellationToken cancel) =>
        await ConnectAsync(server, name, build, debug, key, cancel).ConfigureAwait(false);

    public Task EnterQuickPlayAsync(int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, CancellationToken cancel = default) =>
        SendAsync(new EnterQuickPlay(machine, color, setting, stockColor, CustomMachine.Checked(custom)), cancel);

    public Task RaceOverAsync(string matchId, RaceSnapshot? final = null, CancellationToken cancel = default) =>
        SendAsync(new RaceOver(matchId, final), cancel);

    public Task ReportRaceAsync(string matchId, RaceSnapshot snapshot, CancellationToken cancel = default) =>
        SendAsync(new RaceReport(matchId, snapshot), cancel);

    public Task LeaveQuickPlayAsync(CancellationToken cancel = default) => SendAsync(new LeaveQuickPlay(), cancel);

    public Task ChatAsync(string channel, string text, CancellationToken cancel = default) =>
        SendAsync(new SendChat(channel, text), cancel);

    public Task SetNameAsync(string name, CancellationToken cancel = default) => SendAsync(new SetName(name), cancel);

    public Task VoteStartAsync(bool yes, CancellationToken cancel = default) => SendAsync(new VoteStart(yes), cancel);

    /// <summary>Watch the lobby's races instead of racing (a spectator seat), or race again.</summary>
    public Task WatchAsync(bool yes, CancellationToken cancel = default) => SendAsync(new WatchRace(yes), cancel);

    public Task VoteCpuFillAsync(bool yes, CancellationToken cancel = default) => SendAsync(new VoteCpuFill(yes), cancel);

    public Task VoteCourseAsync(string matchId, int choice, CancellationToken cancel = default) =>
        SendAsync(new VoteCourse(matchId, choice), cancel);

    public Task ChooseMachineAsync(string matchId, int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, string? pack = null, CancellationToken cancel = default) =>
        SendAsync(new MachineChoice(matchId, machine, color, setting, stockColor, CustomMachine.Checked(custom), pack), cancel);

    public Task BrowseLobbiesAsync(bool browse, CancellationToken cancel = default) => SendAsync(new BrowseLobbies(browse), cancel);

    public Task CreateLobbyAsync(CreateLobby request, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(request with { Custom = CustomMachine.Checked(request.Custom) }, cancel);
    }

    public Task JoinLobbyAsync(JoinLobby request, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(request with { Custom = CustomMachine.Checked(request.Custom) }, cancel);
    }

    public Task SetLobbyRulesAsync(SetLobbyRules request, CancellationToken cancel = default) => SendAsync(request, cancel);

    public Task KickPlayerAsync(string playerId, CancellationToken cancel = default) => SendAsync(new KickPlayer(playerId), cancel);

    public Task StartLobbyRaceAsync(CancellationToken cancel = default) => SendAsync(new StartLobbyRace(), cancel);

    public Task LeaveLobbyAsync(CancellationToken cancel = default) => SendAsync(new LeaveLobby(), cancel);

    /// <summary>The relay's host for a match whose relay names none: the coordinator's own.</summary>
    public string RelayHost(RelayEndpoint relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        return string.IsNullOrEmpty(relay.Host) ? Server.Host : relay.Host;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(1000);
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException)
            {
            }
        }
        try
        {
            await _receiving.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _socket.Dispose();
        _stop.Dispose();
        _sending.Dispose();
    }

    /// <summary>Sends a message if the connection is open. On a connection that has ended, or that ends while the
    /// message is sent, it does nothing and raises no error: the end is said once, by <see cref="Closed"/>. (The
    /// launcher sends without waiting, the live race ten times a second: each send after the end was an error
    /// nobody read, which reached the handler of last resort as "Something went wrong" and filled its log.)</summary>
    private async Task SendAsync(ClientMessage message, CancellationToken cancel)
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await SendNowAsync(message, cancel).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or IOException
                                      || (e is InvalidOperationException && _socket.State != WebSocketState.Open))
        {
            // the connection ended meanwhile (or the client was disposed)
        }
    }

    /// <summary>Sends a message; an error if it cannot be sent (the hello: a connection that takes none is none).</summary>
    private async Task SendNowAsync(ClientMessage message, CancellationToken cancel)
    {
        await _sending.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(ControlProtocol.Encode(message)), WebSocketMessageType.Text,
                true, cancel).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }
}
