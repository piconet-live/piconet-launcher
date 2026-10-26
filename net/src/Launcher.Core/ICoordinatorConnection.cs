using Online.Protocol;

namespace Launcher.Core;

/// <summary>Opens a connection to a coordinator and says hello (<see cref="CoordinatorClient.ConnectAsync"/>'s
/// arguments): how a <see cref="QuickPlaySession"/> reaches its server.</summary>
public delegate Task<ICoordinatorConnection> CoordinatorConnect(
    Uri server, string name, string? build, bool debug, string? key, CancellationToken cancel);

/// <summary>
/// The launcher's link to a coordinator, as a <see cref="QuickPlaySession"/> uses it: the messages that come, how the
/// link ended, and the messages the launcher sends. <see cref="CoordinatorClient"/> is the real one (a WebSocket); the
/// interface is here so that a stand-in service (a launcher dev mode) or a test's scripted double can take its place.
/// </summary>
/// <remarks>An implementation keeps <see cref="CoordinatorClient"/>'s rules: nothing is delivered until
/// <see cref="Received"/> has a handler (attach <see cref="Closed"/> first); messages reach it in their order, one at a
/// time; a send on a link that has ended does nothing and raises no error; <see cref="Closed"/> is raised once.</remarks>
public interface ICoordinatorConnection : IAsyncDisposable
{
    /// <summary>The reason of <see cref="Closed"/> when the launcher ended the link itself (<c>DisposeAsync</c>).</summary>
    const string Stopped = "stopped";

    /// <summary>The coordinator this link goes to.</summary>
    Uri Server { get; }

    /// <summary>Every message from the coordinator but those the link answers by itself (relay measurements, pings).</summary>
    event Action<ServerMessage>? Received;

    /// <summary>The link ended (the reason; <see cref="Stopped"/>: by the launcher itself).</summary>
    event Action<string>? Closed;

    /// <summary>The error that ended the link while a message was taken in, for the launcher's log (null: it ended
    /// otherwise, or has not ended).</summary>
    Exception? Fault { get; }

    Task EnterQuickPlayAsync(int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, CancellationToken cancel = default);

    Task RaceOverAsync(string matchId, RaceSnapshot? final = null, CancellationToken cancel = default);

    Task ReportRaceAsync(string matchId, RaceSnapshot snapshot, CancellationToken cancel = default);

    Task LeaveQuickPlayAsync(CancellationToken cancel = default);

    Task ChatAsync(string channel, string text, CancellationToken cancel = default);

    Task SetNameAsync(string name, CancellationToken cancel = default);

    Task VoteStartAsync(bool yes, CancellationToken cancel = default);

    /// <summary>Watch the lobby's races instead of racing (a spectator seat), or race again.</summary>
    Task WatchAsync(bool yes, CancellationToken cancel = default);

    Task VoteCpuFillAsync(bool yes, CancellationToken cancel = default);

    Task VoteCourseAsync(string matchId, int choice, CancellationToken cancel = default);

    Task ChooseMachineAsync(string matchId, int machine, Rgb color, int? setting = null, int? stockColor = null,
        CustomMachine? custom = null, string? pack = null, CancellationToken cancel = default);

    /// <summary>The custom lobbies' list while the browser shows (<see cref="LobbyList"/>), or no more of it.</summary>
    Task BrowseLobbiesAsync(bool browse, CancellationToken cancel = default);

    /// <summary>A custom lobby of the player's, as its host.</summary>
    Task CreateLobbyAsync(CreateLobby request, CancellationToken cancel = default);

    Task JoinLobbyAsync(JoinLobby request, CancellationToken cancel = default);

    /// <summary>The host's name, rules and password for their lobby.</summary>
    Task SetLobbyRulesAsync(SetLobbyRules request, CancellationToken cancel = default);

    /// <summary>The host removes a member from their lobby.</summary>
    Task KickPlayerAsync(string playerId, CancellationToken cancel = default);

    /// <summary>The host starts their lobby's next race.</summary>
    Task StartLobbyRaceAsync(CancellationToken cancel = default);

    /// <summary>Out of the lobby, still connected.</summary>
    Task LeaveLobbyAsync(CancellationToken cancel = default);

    /// <summary>The relay's host for a match whose relay names none: the coordinator's own.</summary>
    string RelayHost(RelayEndpoint relay);
}
