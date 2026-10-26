using System.Diagnostics;

namespace Launcher.Core;

/// <summary>Whether the online server answers, as the dot in the launcher's corner shows it.</summary>
public enum Connectivity
{
    /// <summary>Not asked yet (just started, or the server changed).</summary>
    Checking,

    /// <summary>It answered quickly, or the player is in Quick Play on it now.</summary>
    Online,

    /// <summary>It answered, but slower than <see cref="ServerHealth.SlowAfter"/>.</summary>
    Slow,

    /// <summary>No answer (or not a server address).</summary>
    Offline,

    /// <summary>No server is set (<see cref="LauncherSettings.NoServer"/>): nothing is asked.</summary>
    NoServer,
}

/// <summary>
/// Watches whether the coordinator Quick Play uses answers (a dot in the launcher's corner). It
/// asks the coordinator's <c>/health</c> (which answers "ok") every <see cref="Interval"/>, and at once when the server
/// changes; an open Quick Play connection to that server counts as online whatever the last check said. With no
/// server set it asks nobody. Plain C#, no UI: <see cref="Changed"/> fires on a background thread.
/// </summary>
public sealed class ServerHealth : IDisposable
{
    private readonly Func<string?> _server;
    private readonly Func<Uri?> _connectedTo;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private Connectivity _checked = Connectivity.Checking;

    /// <param name="server">The coordinator's address as the settings hold it (wss://host/ws), read again on
    /// <see cref="Refresh"/>; null: no server is set.</param>
    /// <param name="connectedTo">The coordinator the player's Quick Play session is connected to now (null: none).</param>
    /// <param name="handler">How to send the checks (tests answer them themselves); the network otherwise.</param>
    public ServerHealth(Func<string?> server, Func<Uri?> connectedTo, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(connectedTo);
        _server = server;
        _connectedTo = connectedTo;
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = Timeout };
        Server = server();
        _checked = Unasked(Server);
    }

    /// <summary>How often the server is asked.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a check waits for an answer before calling the server offline.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>An answer slower than this shows as slow: reachable, but races may not feel right.</summary>
    public static TimeSpan SlowAfter { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The state changed (a check came back, the server changed, Quick Play connected or ended).</summary>
    public event Action? Changed;

    /// <summary>The coordinator these results are for (null: no server is set).</summary>
    public string? Server { get; private set; }

    /// <summary>When the last check came back (null: none yet for this server).</summary>
    public DateTimeOffset? LastCheck { get; private set; }

    /// <summary>How long the last answer took (null: no answer).</summary>
    public TimeSpan? LastAnswer { get; private set; }

    /// <summary>In Quick Play on this server now.</summary>
    public bool InQuickPlay => _connectedTo() is { } c && Server is { } server && SameServer(c.ToString(), server);

    public Connectivity State
    {
        get
        {
            lock (_lock)
            {
                return InQuickPlay ? Connectivity.Online : _checked;
            }
        }
    }

    /// <summary>Starts checking: now, then every <see cref="Interval"/> until disposed.</summary>
    public void Start() => _ = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await CheckAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // a listener's failure must not end the checking: the next one comes in 30 s
                    Trace.TraceWarning($"server check: {e.Message}");
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    });

    /// <summary>Looks again at the settings and the session: a new server starts over (checking, asked at once);
    /// otherwise the shown state is passed on in case Quick Play connected or ended.</summary>
    public void Refresh()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }
        var now = _server();
        bool moved;
        lock (_lock)
        {
            moved = now != Server;
            if (moved)
            {
                Server = now;
                _checked = Unasked(now);
                LastCheck = null;
                LastAnswer = null;
            }
        }
        Changed?.Invoke();
        if (moved)
        {
            _ = CheckAsync(_stop.Token);
        }
    }

    /// <summary>Asks the server once and keeps the result (unless the server changed meanwhile).</summary>
    public async Task CheckAsync(CancellationToken cancel = default)
    {
        string? asked;
        lock (_lock)
        {
            asked = Server;
        }
        if (asked is null)
        {
            return;   // no server is set: nobody to ask (the state says so since the server was read)
        }
        TimeSpan? answer = null;
        if (HealthUri(asked) is { } uri)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                // the status line is the answer: the body is not read, however long it is (a server is anyone's
                // computer, and a body without end was read into memory to its end)
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    answer = clock.Elapsed;
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                // no answer (refused, no network, or the timeout): offline
            }
        }
        lock (_lock)
        {
            if (asked != Server)
            {
                return;   // a check of the server before; the new one's check is on its way
            }
            _checked = Judge(answer);
            LastAnswer = answer;
            LastCheck = DateTimeOffset.Now;
        }
        Changed?.Invoke();
    }

    /// <summary>The state of a server not asked yet: checking, or no server when there is none to ask.</summary>
    private static Connectivity Unasked(string? server) => server is null ? Connectivity.NoServer : Connectivity.Checking;

    /// <summary>What an answer (or none) means for the dot.</summary>
    public static Connectivity Judge(TimeSpan? answer) =>
        answer is not { } took ? Connectivity.Offline : took > SlowAfter ? Connectivity.Slow : Connectivity.Online;

    /// <summary>The health check's address for a coordinator's: wss to https and ws to http, the same host and port,
    /// path /health. Null when it is not a server address.</summary>
    public static Uri? HealthUri(string coordinator)
    {
        if (!Uri.TryCreate(coordinator?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }
        var scheme = uri.Scheme switch
        {
            "wss" or "https" => Uri.UriSchemeHttps,
            "ws" or "http" => Uri.UriSchemeHttp,
            _ => null,
        };
        return scheme is null ? null : new UriBuilder(scheme, uri.Host, uri.Port, "/health").Uri;
    }

    /// <summary>The server's address as players know it: the host, and the port when it is not the usual one.</summary>
    public static string HostOf(string coordinator) =>
        Uri.TryCreate(coordinator?.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
            : coordinator?.Trim() ?? "";

    private static bool SameServer(string a, string b) => HealthUri(a) is { } x && x == HealthUri(b);

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _http.Dispose();
    }
}
