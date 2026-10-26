using System.Net.Http.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// What the coordinator says of itself to anyone (<see cref="ServiceStatus"/>, GET /api/status: the players online, the
/// open lobbies, the races running), for the launcher's Play page while the player is only looking. Asked every
/// <see cref="Interval"/> while <see cref="Watching"/> is set, and never otherwise; the server's own status page asks
/// the same every 3 seconds. With no server set (the settings name none: <see cref="LauncherSettings.NoServer"/>)
/// nothing is asked of anyone. Plain C#, no UI: <see cref="Changed"/> fires on a background thread.
/// </summary>
public sealed class ServerStatusFeed : IDisposable
{
    private readonly Func<string?> _server;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private string? _asked = "";
    private bool _started;

    /// <param name="server">The coordinator's address as the settings hold it (wss://host/ws), read before each ask;
    /// null: no server is set, and nothing is asked.</param>
    /// <param name="handler">How to send the asks (tests answer them themselves); the network otherwise.</param>
    public ServerStatusFeed(Func<string?> server, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
        // an answer is no longer than a coordinator's longest message: a longer one is no answer
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = Timeout, MaxResponseContentBufferSize = ServerMessages.MaxBytes };
    }

    /// <summary>How often the server is asked while someone is watching.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(5);

    /// <summary>How long an ask waits for its answer.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(4);

    /// <summary>A new answer came, or an ask went unanswered.</summary>
    public event Action? Changed;

    /// <summary>Whether a page that shows the status is on screen: the server is asked only then.</summary>
    public bool Watching { get; set; }

    /// <summary>The last answer of the server now set (null: none yet).</summary>
    public ServiceStatus? Latest { get; private set; }

    /// <summary>When <see cref="Latest"/> came.</summary>
    public DateTimeOffset? LatestAt { get; private set; }

    /// <summary>The last ask got no answer (<see cref="Latest"/> is then the last one that did, if any).</summary>
    public bool Failed { get; private set; }

    /// <summary>No server is set, as of the last ask: there is nobody to ask, which is no failure.</summary>
    public bool NoServer { get; private set; }

    /// <summary>Starts asking: now, then every <see cref="Interval"/> until disposed, while <see cref="Watching"/>.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started)
            {
                return;
            }
            _started = true;
        }
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(Interval);
            try
            {
                do
                {
                    if (Watching)
                    {
                        await AskAsync(_stop.Token).ConfigureAwait(false);
                    }
                }
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // disposed
            }
        });
    }

    /// <summary>Asks the server once and keeps what it said; an answer for a server the settings have since left is
    /// dropped. With no server set it asks nobody.</summary>
    public async Task AskAsync(CancellationToken cancel = default)
    {
        var server = _server();
        bool moved;
        lock (_lock)
        {
            moved = server != _asked;
            if (moved)
            {
                _asked = server;
                Latest = null;
                LatestAt = null;
                Failed = false;
                NoServer = server is null;
            }
        }
        if (server is null)
        {
            if (moved)
            {
                Changed?.Invoke();
            }
            return;
        }
        ServiceStatus? answer = null;
        if (StatusUri(server) is { } uri)
        {
            try
            {
                // checked before a page reads it, as a coordinator's messages are (ServerMessages)
                answer = ServerMessages.Checked(
                    await _http.GetFromJsonAsync<ServiceStatus>(uri, ControlProtocol.Json, cancel).ConfigureAwait(false));
            }
            catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
                || (e is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                // no answer (refused, no network, the timeout), or not a coordinator's answer
            }
        }
        lock (_lock)
        {
            if (server != _asked)
            {
                return;
            }
            Failed = answer is null;
            if (answer is not null)
            {
                Latest = answer;
                LatestAt = DateTimeOffset.UtcNow;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>The status address for a coordinator's: the health check's host (<see cref="ServerHealth.HealthUri"/>),
    /// path /api/status. Null when it is not a server address.</summary>
    public static Uri? StatusUri(string coordinator) =>
        ServerHealth.HealthUri(coordinator) is { } health ? new Uri(health, "/api/status") : null;

    /// <summary>The lobby Quick Play would put a new player in: the fullest one with room that is not starting a race
    /// (nobody joins a lobby while it starts or chooses machines); null when there is none.</summary>
    public static LobbyView? QuickPlayLobby(ServiceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.Lobbies
            .Where(l => l.Players.Count < l.Capacity && l.Stage is LobbyState.Forming or LobbyState.Racing)
            .OrderByDescending(l => l.Players.Count)
            .FirstOrDefault();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _http.Dispose();
    }
}
