using System.Threading.Channels;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The stand-in service's connection (<see cref="StandInService"/>): an <see cref="ICoordinatorConnection"/> in the
/// launcher's own process, its Quick Play lobby played by <see cref="StandInLobby"/>, its custom lobbies by
/// <see cref="StandInRooms"/> (a lobby's votes, chat and machine choice go to the custom lobby the player is in). It keeps the interface's rules as the real
/// one does: nothing is delivered until <see cref="Received"/> has a handler; the messages go out in their order, one
/// at a time, on one task, each checked as a coordinator's are (<see cref="ServerMessages.Checked(ServerMessage?)"/>);
/// a send after the end does nothing; <see cref="Closed"/> is raised once.
/// </summary>
public sealed class StandInCoordinator : ICoordinatorConnection
{
    private readonly Channel<ServerMessage> _out = Channel.CreateUnbounded<ServerMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly StandInLobby _quick;
    private readonly StandInRooms _rooms;
    private string _name;
    private Action<ServerMessage>? _received;
    private Task? _delivering;
    private int _closed;

    private StandInCoordinator(Uri server, string name)
    {
        Server = server;
        _name = name;
        _quick = new StandInLobby(name, Send, _stop.Token);
        _rooms = new StandInRooms(name, Send, _stop.Token);
    }

    /// <summary>The stand-in's <see cref="CoordinatorConnect"/>: the hello is taken at once, whatever it says.</summary>
    public static Task<ICoordinatorConnection> ConnectAsync(Uri server, string name, string? build, bool debug,
        string? key, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancel.ThrowIfCancellationRequested();
        var connection = new StandInCoordinator(server, name ?? "");
        connection._quick.Welcome();
        return Task.FromResult<ICoordinatorConnection>(connection);
    }

    public Uri Server { get; }

    public event Action<ServerMessage>? Received
    {
        add
        {
            _received += value;
            if (value is not null && _delivering is null)
            {
                _delivering = Task.Run(DeliverAsync);
            }
        }
        remove => _received -= value;
    }

    public event Action<string>? Closed;

    public Exception? Fault { get; private set; }

    private void Send(ServerMessage message)
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            _out.Writer.TryWrite(message);
        }
    }

    private async Task DeliverAsync()
    {
        try
        {
            await foreach (var message in _out.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (ServerMessages.Checked(message) is { } checkedMessage)
                {
                    _received?.Invoke(checkedMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ended by the launcher
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException
                                      or IndexOutOfRangeException or NullReferenceException)
        {
            // as the real connection does with a message the launcher could not use: kept for the log, and the end
            Fault = e;
            End(CoordinatorClient.FaultText);
        }
    }

    private void End(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _out.Writer.TryComplete();
            Closed?.Invoke(reason);
        }
    }

    /// <summary>The lobby the player's votes, chat and choices go to: the custom one they are in, else Quick Play's.</summary>
    private StandInLobby Lobby => _rooms.Current ?? _quick;

    public Task EnterQuickPlayAsync(int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, CancellationToken cancel = default) => Done(() => _quick.Queue(machine, color, stockColor));

    public Task RaceOverAsync(string matchId, RaceSnapshot? final = null, CancellationToken cancel = default) => Task.CompletedTask;

    public Task ReportRaceAsync(string matchId, RaceSnapshot snapshot, CancellationToken cancel = default) => Task.CompletedTask;

    public Task LeaveQuickPlayAsync(CancellationToken cancel = default) => Done(() => _quick.Leave());

    public Task ChatAsync(string channel, string text, CancellationToken cancel = default) => Done(() => Lobby.Chat(channel, text));

    public Task SetNameAsync(string name, CancellationToken cancel = default) => Done(() =>
    {
        _name = name;
        _quick.Rename(name);
        _rooms.Current?.Rename(name);
    });

    public Task VoteStartAsync(bool yes, CancellationToken cancel = default) => Done(() => Lobby.VoteStart(yes));

    public Task WatchAsync(bool yes, CancellationToken cancel = default) => Task.CompletedTask;   // (its welcome offers no watching)

    public Task VoteCpuFillAsync(bool yes, CancellationToken cancel = default) => Done(() => Lobby.VoteCpuFill(yes));

    public Task VoteCourseAsync(string matchId, int choice, CancellationToken cancel = default) => Done(() => Lobby.VoteCourse(matchId, choice));

    public Task ChooseMachineAsync(string matchId, int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, string? pack = null, CancellationToken cancel = default) =>
        Done(() => Lobby.Choose(matchId, machine, color, stockColor));

    public Task BrowseLobbiesAsync(bool browse, CancellationToken cancel = default) => Done(() => _rooms.Browse(browse));

    public Task CreateLobbyAsync(CreateLobby request, CancellationToken cancel = default) => Done(() => _rooms.Create(request, _name));

    public Task JoinLobbyAsync(JoinLobby request, CancellationToken cancel = default) => Done(() => _rooms.Join(request));

    public Task SetLobbyRulesAsync(SetLobbyRules request, CancellationToken cancel = default) => Done(() => _rooms.SetRules(request));

    public Task KickPlayerAsync(string playerId, CancellationToken cancel = default) => Done(() => _rooms.Kick(playerId));

    public Task ChooseTeamAsync(int? team, string? playerId, CancellationToken cancel = default) => Done(() => _rooms.ChooseTeam(team, playerId));

    public Task StartLobbyRaceAsync(CancellationToken cancel = default) => Done(_rooms.Start);

    public Task LeaveLobbyAsync(CancellationToken cancel = default) => Done(() =>
    {
        if (_rooms.Current is null)
        {
            _quick.Leave();
        }
        else
        {
            _rooms.LeaveCurrent();
        }
    });

    /// <summary>The stand-in has no race server: no relay is ever named.</summary>
    public string RelayHost(RelayEndpoint relay) => Server.Host;

    /// <summary>A message to the stand-in, taken while the link is open (after its end, nothing).</summary>
    private Task Done(Action act)
    {
        if (Volatile.Read(ref _closed) == 0)
        {
            act();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        End(ICoordinatorConnection.Stopped);
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_delivering is { } delivering)
        {
            await delivering.ConfigureAwait(false);
        }
        _stop.Dispose();
    }
}
