using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.App;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>A name changed while connected is told to the server, which shows it to everybody at once; it went with
/// the hello only, so the others saw the old name until the player left Quick Play and joined again. And the
/// launcher sees to it that the server ends with the name that is saved: after a refusal, after two names in a
/// second, after a name saved while the connection was still being made; and it sends no name again that the server
/// took. Against a coordinator that keeps a name as the real one does (one change a second, the lobby sent again) and
/// can be told to refuse, or to hold its lobbies back.</summary>
public sealed class RenameTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A coordinator for one launcher. It welcomes (saying, or not, that it takes a new name; when told),
    /// puts the player in a lobby, and takes the message <c>name</c> as the real server does: at most one a second
    /// (else rename_too_soon), the lobby sent again with the name.</summary>
    private sealed class Naming : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serving;
        private readonly bool _renames;
        private readonly TaskCompletionSource _welcomes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _sending = new(1, 1);
        private readonly ConcurrentQueue<string> _held = new();
        private volatile WebSocket? _socket;
        private DateTimeOffset _renamed = DateTimeOffset.MinValue;

        public Naming(bool renames = true, bool welcomeAtOnce = true)
        {
            _renames = renames;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                Address = new Uri($"ws://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/ws");
            }
            _listener.Prefixes.Add($"http://127.0.0.1:{Address.Port}/");
            _listener.Start();
            if (welcomeAtOnce)
            {
                Welcome();
            }
            _serving = ServeAsync();
        }

        public Uri Address { get; }

        /// <summary>The name the server has for the player (the hello's at first).</summary>
        public string? Name { get; private set; }

        /// <summary>Every <c>name</c> message, as its name, in the order they came.</summary>
        public ConcurrentQueue<string> Asked { get; } = new();

        /// <summary>The names of those that the server took (the others it refused), in the order it took them.</summary>
        public ConcurrentQueue<string> Taken { get; } = new();

        /// <summary>The lobby a taken name is answered with is not sent but kept, until <see cref="ReleaseAsync"/>.</summary>
        public bool HoldLobbies { get; set; }

        /// <summary>Sends the oldest lobby held back, as it was when the name was taken.</summary>
        public async Task ReleaseAsync()
        {
            Assert.True(_held.TryDequeue(out var lobby), "no lobby is held back");
            await Send(_socket!, lobby);
        }

        /// <summary>What the server answers the next rename with, in place of taking it (an error's code); once.</summary>
        public string? RefuseNext { get; set; }

        public void Welcome() => _welcomes.TrySetResult();

        private async Task Send(WebSocket socket, string text)
        {
            await _sending.WaitAsync();   // (a lobby released by the test and an answer of the server's: one at a time)
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _sending.Release();
            }
        }

        private string Lobby() =>
            """{"type":"lobby","lobbyId":"l1","players":[{"id":"p1","name":""" + System.Text.Json.JsonSerializer.Serialize(Name) +
            ""","machine":3,"color":{"r":1,"g":2,"b":3}}],"capacity":30,"votingMinimum":2}""";

        private Task SendLobby(WebSocket socket) => Send(socket, Lobby());

        private async Task ServeAsync()
        {
            try
            {
                var socket = _socket = (await (await _listener.GetContextAsync()).AcceptWebSocketAsync(null)).WebSocket;
                var buffer = new byte[16 * 1024];
                while (socket.State == WebSocketState.Open)
                {
                    var r = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (r.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }
                    var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, r.Count))!;
                    switch ((string?)message["type"])
                    {
                        case "hello":
                            Name = ((string)message["name"]!).Trim();
                            await _welcomes.Task;
                            await Send(socket, """{"type":"welcome","playerId":"p1","serverName":"Den","version":3""" + (_renames ? ""","renames":true}""" : "}"));
                            break;
                        case "queue":
                            await SendLobby(socket);
                            break;
                        case "name":
                            var name = ((string)message["name"]!).Trim();
                            Asked.Enqueue(name);
                            var refuse = RefuseNext ?? (DateTimeOffset.UtcNow - _renamed < TimeSpan.FromSeconds(1) ? "rename_too_soon" : null);
                            RefuseNext = null;
                            if (refuse is not null)
                            {
                                await Send(socket, """{"type":"error","code":"REFUSAL","message":"a name is changed once a second at most"}""".Replace("REFUSAL", refuse, StringComparison.Ordinal));
                                break;
                            }
                            (Name, _renamed) = (name, DateTimeOffset.UtcNow);
                            Taken.Enqueue(name);
                            if (HoldLobbies)
                            {
                                _held.Enqueue(Lobby());
                                break;
                            }
                            await SendLobby(socket);
                            break;
                        default:
                            break;
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
            _welcomes.TrySetCanceled();
            _listener.Close();
            try
            {
                await _serving;
            }
            catch (OperationCanceledException)
            {
                // the welcome was never asked for
            }
        }
    }

    private static async Task Until(Func<bool> met, CancellationToken cancel, int seconds = 10)
    {
        for (var i = 0; i < seconds * 50 && !met(); i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.True(met(), "the wait ran out");
    }

    private AppState State(string name = "ANA")
    {
        var state = AppState.Load(Path.Combine(_dir, "data"));
        state.Rename(name);                                         // not connected: saved, nobody to tell
        Assert.Equal(name, state.Settings.Racer.Name);
        return state;
    }

    private static string Me(QuickPlaySession session) => session.Lobby!.Players.Single().Name;

    [Fact]
    public async Task ANameChangedInALobbyIsSavedAndShownByTheServer()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming();
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => session.Lobby is not null, cancel);
            Assert.Equal(("ANA", "ANA"), (server.Name, Me(session)));
            Assert.Empty(server.Asked);                                  // the hello's name is the saved one: nothing to send

            state.Rename("ANNA");

            await Until(() => Me(session) == "ANNA", cancel);
            Assert.Equal(("ANNA", "ANNA"), (server.Name, state.Settings.Racer.Name));
            Assert.Equal(["ANNA"], server.Asked);
            Assert.Equal("ANNA", AppState.Load(Path.Combine(_dir, "data")).Settings.Racer.Name);   // and kept for the next start
            await Task.Delay(1500, cancel);
            Assert.Equal(["ANNA"], server.Asked);                        // once they agree, nothing more is sent
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>Two names saved within a second: the server takes one change a second, and refused the second, so
    /// the first name stayed on the server for the whole visit while the launcher showed the second. The launcher
    /// sends the second when the server will take it.</summary>
    [Fact]
    public async Task TwoNamesSavedWithinASecondEndWithTheLastOneOnTheServer()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming();
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => session.Lobby is not null, cancel);

            state.Rename("ANNA");
            state.Rename("ANNE");
            state.Rename("ANNI");

            await Until(() => server.Name == "ANNI" && Me(session) == "ANNI", cancel);
            // The middle one was never the saved name when a send was due, and the server took each of the others
            // once. How often the last was sent is not said here: the launcher sends it 1.1 s after the first by
            // its own clock and the server counts its second between the two arrivals, so on a loaded computer the
            // server can refuse it as too soon, and the launcher then sends it once more (seen at a landing).
            Assert.DoesNotContain("ANNE", server.Asked);
            Assert.Equal(["ANNA", "ANNI"], server.Taken);
            var asked = server.Asked.Count;
            await Task.Delay(1500, cancel);
            Assert.Equal((asked, 2), (server.Asked.Count, server.Taken.Count));   // once they agree, nothing more is sent
            Assert.Null(session.LastError);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>The server's word on a name just sent is the lobby that carries it, or the refusal. A lobby that
    /// comes meanwhile with the name before it (the answer to the rename before, late) is not: the launcher took it
    /// for the server's word, and sent the last name a second time to a server that had it.</summary>
    [Fact]
    public async Task ALateLobbyWithTheNameBeforeIsNoReasonToSendTheLastNameAgain()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming();
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => session.Lobby is not null, cancel);
            server.HoldLobbies = true;

            state.Rename("ANNA");
            await Until(() => server.Taken.Count == 1, cancel);          // taken; the lobby that says so is held back
            state.Rename("ANNI");
            await Until(() => server.Taken.Count == 2, cancel);
            await server.ReleaseAsync();                                 // the first rename's lobby, after the second was sent
            await Until(() => Me(session) == "ANNA", cancel);
            await Task.Delay(1500, cancel);                              // (longer than the launcher waits between two sends)
            Assert.Equal(["ANNA", "ANNI"], server.Taken);

            await server.ReleaseAsync();
            await Until(() => Me(session) == "ANNI", cancel);
            var asked = server.Asked.Count;
            await Task.Delay(1500, cancel);
            Assert.Equal((asked, 2), (server.Asked.Count, server.Taken.Count));
            Assert.Null(session.LastError);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A rename the server refuses as too soon is sent again a moment later, and is no trouble of the
    /// player's: nothing of it is shown.</summary>
    [Fact]
    public async Task ARenameRefusedAsTooSoonIsSentAgainAndNothingIsShown()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming { RefuseNext = "rename_too_soon" };
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            // until the status line says the lobby: the session takes a lobby in before it words it, and a look
            // between the two read "Queueing" here, which the rename then seemed to have changed
            await Until(() => session.Lobby is not null && session.Status.StartsWith("Lobby", StringComparison.Ordinal), cancel);
            var status = session.Status;

            state.Rename("ANNA");

            await Until(() => server.Asked.Count == 1, cancel);
            await Task.Delay(200, cancel);
            Assert.Equal(("ANA", status), (server.Name, session.Status));   // refused, and the status line is as it was
            await Until(() => server.Name == "ANNA" && Me(session) == "ANNA", cancel);
            Assert.Equal(["ANNA", "ANNA"], server.Asked);
            Assert.Null(session.LastError);
            Assert.Null(state.Problem);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A name the server refuses as a name is let be: not sent again and again, and nothing is shown.</summary>
    [Fact]
    public async Task ANameTheServerRefusesAsANameIsNotSentAgain()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming { RefuseNext = "bad_name" };
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => session.Lobby is not null, cancel);
            state.Rename("ANNA");
            await Until(() => server.Asked.Count == 1, cancel);
            await Task.Delay(2500, cancel);
            Assert.Equal(["ANNA"], server.Asked);
            Assert.Equal(("ANA", null), (server.Name, session.LastError));
            state.Rename("ANNE");                                        // another name is tried
            await Until(() => server.Name == "ANNE", cancel);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A name saved between RACE and the server's welcome: the hello had carried the old one, and nothing
    /// was sent for the new. It goes as soon as the welcome is in.</summary>
    [Fact]
    public async Task ANameSavedWhileTheConnectionIsMadeGoesAfterTheWelcome()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming(welcomeAtOnce: false);
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => server.Name == "ANA", cancel);             // the hello is in; the welcome is held back
            state.Rename("ANNA");
            await Task.Delay(300, cancel);
            Assert.Empty(server.Asked);                                  // not before the server has welcomed

            server.Welcome();

            await Until(() => server.Name == "ANNA" && session.Lobby is not null && Me(session) == "ANNA", cancel);
            Assert.Equal(["ANNA"], server.Asked);
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>A server that does not say it takes a new name knows no such message (an older one would answer
    /// "unreadable message"): nothing is sent, and the name is the next join's, as before.</summary>
    [Fact]
    public async Task AServerThatTakesNoNewNameIsToldNothing()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new Naming(renames: false);
        var state = State();
        try
        {
            var session = await state.JoinAsync(server.Address);
            await Until(() => session.Lobby is not null, cancel);
            state.Rename("ANNA");
            await Task.Delay(1500, cancel);
            Assert.Empty(server.Asked);
            Assert.Equal(("ANNA", "ANA"), (state.Settings.Racer.Name, server.Name));
        }
        finally
        {
            state.Shutdown();
        }
    }
}
