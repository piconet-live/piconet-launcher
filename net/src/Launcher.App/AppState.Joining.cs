using System.Globalization;
using System.Net.Sockets;
using System.Net.WebSockets;
using Launcher.Core;

namespace Launcher.App;

// A Quick Play join on its way, and what it rules out.
public sealed partial class AppState
{
    private int _joining;

    /// <summary>A Quick Play join is on its way: from RACE (or a local server's Join) until the connection is made, has
    /// failed, was given up after <see cref="JoinLimit"/>, or the player left. It counts as being in Quick Play for
    /// whatever starts a game: a game started in that moment was still open when the connection landed, and the
    /// first match start then found a game open and gave the seat away. <see cref="Changed"/> says when it begins
    /// and ends.</summary>
    public bool Joining => Volatile.Read(ref _joining) > 0;

    /// <summary>How long a join waits for the server's answer to its connection. A server that takes the connection
    /// and then says nothing would otherwise keep the launcher joining, and so out of every game start, for good.</summary>
    public TimeSpan JoinLimit { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Why no server is joined at this moment, as RACE and the Local network tab's Join say it (the one
    /// rule for both); null: one can be. A game that is open rules it out (<see cref="GameOpenText"/>), and so does a
    /// join already on its way (no word: the first press is still at work). Then what a race needs: the disc
    /// (<see cref="NoDiscText"/>), and the game with its online part (<see cref="RaceProblem"/>, the newest look's
    /// answer): a player taken into a lobby without them has no game to open at the race's start, and the seat goes
    /// to the AI.</summary>
    /// (In dev mode neither: the stand-in starts no race, <see cref="StandIn"/>.)
    public string? WhyNotJoin => Racing ? GameOpenText : Joining ? "" : StandIn ? null
        : DiscCheck.Check(Settings.DiscPath) != DiscStatus.Ok ? NoDiscText : RaceProblem;

    public const string NoDiscText = "No disc image";

    public const string GameOpenText = "Game running";

    /// <summary>Joins Quick Play on a coordinator with the player's racer (leaving any earlier session). An
    /// <see cref="IOException"/> when the server gave no answer within <see cref="JoinLimit"/>; an
    /// <see cref="OperationCanceledException"/> when the player left meanwhile (<see cref="LeaveAsync"/>).</summary>
    /// <param name="custom">A visit to the custom lobbies (<see cref="QuickPlaySession.Custom"/>): no queue, the
    /// browser's list.</param>
    public async Task<QuickPlaySession> JoinAsync(Uri server, bool custom = false)
    {
        using var left = new CancellationTokenSource();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(left.Token);
        // leaving takes the session away (LeaveAsync): seen here, it ends the connection that is being made
        var had = false;
        void Watch()
        {
            if (Session is not null)
            {
                had = true;
            }
            else if (had)
            {
                left.Cancel();
            }
        }
        Interlocked.Increment(ref _joining);
        try
        {
            Changed += Watch;
            Changed?.Invoke();   // inside the try: a handler that throws must not leave the launcher joining
            limit.CancelAfter(JoinLimit);
            return await ConnectAsync(server, custom, limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (limit.IsCancellationRequested && e is OperationCanceledException or WebSocketException)
        {
            if (left.IsCancellationRequested)
            {
                throw new OperationCanceledException("left while the launcher was connecting", e);
            }
            throw new NoAnswerException(JoinLimit, e);
        }
        finally
        {
            Changed -= Watch;
            Interlocked.Decrement(ref _joining);
            Changed?.Invoke();
        }
    }

    /// <summary>A join that found no server (<see cref="JoinAsync"/>'s errors but the player's leaving): the whole
    /// error to the launcher's log (<see cref="Noted"/>), and the notice that says it, its reason as a value
    /// (<see cref="CouldNotReachText"/>).</summary>
    public string JoinFailed(string server, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Noted?.Invoke($"a join of {server} found no server: {error}");
        return CouldNotReachText(error);
    }

    /// <summary>The notice when a join found no server, as a dropped connection is said: "Could not reach the server
    /// · refused".</summary>
    public static string CouldNotReachText(Exception error) => $"Could not reach the server · {WhyNotReached(error)}";

    /// <summary>A join's error as a short label; the error's own message for one not named here.</summary>
    public static string WhyNotReached(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error is NoAnswerException { Limit: var limit })
        {
            return string.Create(CultureInfo.InvariantCulture, $"no answer in {limit.TotalSeconds:0.#} s");
        }
        if (error is UriFormatException)
        {
            return "address not valid";
        }
        for (var e = error; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused }:
                    return "refused";
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }:
                    return "server not found";
                case SocketException { SocketErrorCode: SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.NetworkDown }:
                    return "network unreachable";
                case SocketException { SocketErrorCode: SocketError.TimedOut }:
                    return "no answer";
                case System.Security.Authentication.AuthenticationException:
                    return "secure connection failed";
                case WebSocketException { WebSocketErrorCode: WebSocketError.NotAWebSocket }:
                    return "not a game server";
            }
        }
        return error.Message.TrimEnd('.');
    }

    /// <summary>The server gave no answer to a join within <see cref="Limit"/> (<see cref="JoinLimit"/>).</summary>
    public sealed class NoAnswerException(TimeSpan limit, Exception inner) : IOException(
        string.Create(CultureInfo.InvariantCulture, $"no answer within {limit.TotalSeconds:0.#} seconds"), inner)
    {
        public TimeSpan Limit { get; } = limit;
    }
}
