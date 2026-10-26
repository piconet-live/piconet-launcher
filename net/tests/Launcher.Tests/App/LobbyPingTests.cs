using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;
using LobbyView = Launcher.App.Views.LobbyView;

namespace Launcher.Tests;

/// <summary>
/// Each player's ping in the lobby's racers list: a number and its unit beside the racer, nothing for a seat without a racer or a racer the server has
/// no number for. In the launcher's own window against a server the test leads (<see cref="LedServer"/>); nothing
/// is pressed, the rows are read. And the launcher's part in the measuring: it says in its hello that it answers,
/// and answers the server's ping at once with the same number, also while it measures a match's relays.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class LobbyPingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data);
    }

    /// <summary>A seat's row as it is read: the racer's name, where the name begins, the ping's text and where it
    /// ends, the room the ping has and the room its text takes as it is drawn.</summary>
    private sealed record Seat(string Name, double NameLeft, string Ping, double PingRight, double PingRoom, double PingTakes);

    private static List<Seat> Seats(LobbyView view)
    {
        Dispatcher.UIThread.RunJobs();
        var seats = new List<Seat>();
        foreach (var row in view.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("seat")))
        {
            var texts = row.Children.OfType<TextBlock>().ToList();
            var ping = texts.Single(t => t.Classes.Contains("ping"));
            // the name and tag, with the machine after them in what room is left
            var name = row.GetVisualDescendants().OfType<Launcher.App.Controls.NameTagText>().Single();
            seats.Add(new Seat(name.PlayerName ?? "", name.TranslatePoint(default, view)!.Value.X, ping.Text ?? "",
                ping.TranslatePoint(new Point(ping.Bounds.Width, 0), view)!.Value.X, ping.Bounds.Width,
                ping.TextLayout.WidthIncludingTrailingWhitespace));
        }
        return seats;
    }

    [Fact]
    public async Task EachRacersPingStandsInTheListAndASeatWithoutARacerHasNone()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new LedServer();
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
                Assert.True(server.Of<Hello>().Single().Pings);                      // it says that it answers pings
                // CPU racers fill the seats nobody has: their rows are there, and have no ping
                var welcome = LedServer.Welcome() with { Rules = LedServer.Welcome().Rules! with { CpuFill = CpuFillMode.Always } };
                server.SendAsync(welcome).GetAwaiter().GetResult();
                Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                server.SendAsync(LedServer.Lobby(LobbyState.Forming)).GetAwaiter().GetResult();
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = main.Lobby;
                var view = window.GetVisualDescendants().OfType<LobbyView>().Single();

                // the server's question is answered at once, with its number
                server.SendAsync(new LinkPing(77)).GetAwaiter().GetResult();
                Pump(() => server.Of<LinkPong>().Count == 1, "the launcher did not answer the ping");
                Assert.Equal(77u, server.Of<LinkPong>().Single().Id);

                // no number yet: no row says anything, not "0 ms"
                var before = Seats(view);
                Assert.Equal(["ANA", "BEN", "CPU"], before.Take(3).Select(s => s.Name));
                Assert.All(before, s => Assert.Equal("", s.Ping));

                server.SendAsync(new LobbyPings("l1", [new PlayerPing("p1", 42), new PlayerPing("p2", 137)])).GetAwaiter().GetResult();
                PumpTelling(state, () => state.Session!.Pings.Count == 2);
                var shown = Seats(view);
                Assert.Equal(["42 ms", "137 ms"], shown.Take(2).Select(s => s.Ping));
                Assert.All(shown.Skip(2), s => Assert.Equal("", s.Ping));                // a CPU's seat, an empty one
                Assert.Equal(shown.Count, before.Count);
                // a value in a column of its own: the names stand where they stood, and the numbers end on one line
                Assert.Equal(before.Select(s => s.NameLeft), shown.Select(s => s.NameLeft));
                var column = shown.Take(shown.Count / 2).ToList();                       // (the seats run down two columns)
                Assert.Single(column.Select(s => Math.Round(s.PingRight, 1)).Distinct());

                // the next list: a number over three digits is shown as three, a player it leaves out has none again
                server.SendAsync(new LobbyPings("l1", [new PlayerPing("p2", 5000)])).GetAwaiter().GetResult();
                PumpTelling(state, () => state.Session!.Pings.Count == 1);
                var widest = Seats(view)[1];
                Assert.Equal(["", "999 ms"], Seats(view).Take(2).Select(s => s.Ping));
                // the widest there is (every digit is as wide as the next: tabular) stands whole in its column
                Assert.True(widest.PingTakes > 20 && widest.PingTakes <= widest.PingRoom,
                    $"\"999 ms\" takes {widest.PingTakes:0.0} of {widest.PingRoom:0.0}");

                // another lobby's list is not this lobby's
                server.SendAsync(new LobbyPings("l9", [new PlayerPing("p1", 5)])).GetAwaiter().GetResult();
                server.SendAsync(new ChatMessage(ControlProtocol.LobbyChannel, "p2", "BEN", "hi", DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
                PumpTelling(state, () => state.Session!.Chat.Count == 1);                // (the list before it was taken in)
                Assert.Equal(["", "999 ms"], Seats(view).Take(2).Select(s => s.Ping));
            }, cancel);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }

    /// <summary>The server times the answer to its ping, so nothing may hold it up: a relay that says nothing takes
    /// two and a half seconds to measure, and a ping that comes meanwhile is answered at once. Everything else
    /// keeps its order: what came behind the measurement is taken in after it, as before.</summary>
    [Fact]
    public async Task APingIsAnsweredAtOnceAlsoWhileARelayIsMeasuredAndNothingElseOvertakes()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new LedServer();
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));   // a relay that answers nothing
        var relay = new RelayEndpoint("r1", "", ((IPEndPoint)silent.Client.LocalEndPoint!).Port);
        await using var client = await CoordinatorClient.ConnectAsync(server.Address, "ANA", cancel: cancel);
        var got = new List<(ServerMessage Message, DateTimeOffset At)>();
        client.Received += m =>
        {
            lock (got)
            {
                got.Add((m, DateTimeOffset.UtcNow));
            }
        };
        await server.SendAsync(new MeasureRelays("m1", [relay], 1500), new LinkPing(7),
            new ChatMessage(ControlProtocol.LobbyChannel, "p2", "BEN", "hi", DateTimeOffset.UtcNow), new LinkPing(8));
        for (var i = 0; i < 500 && server.Of<RelayLatency>().Count == 0; i++)
        {
            await Task.Delay(20, cancel);
        }
        for (var i = 0; i < 500 && Chats().Count == 0; i++)
        {
            await Task.Delay(20, cancel);
        }

        var sent = server.Received;
        Assert.Equal([typeof(Hello), typeof(LinkPong), typeof(LinkPong), typeof(RelayLatency)], sent.Select(r => r.Message.GetType()));
        Assert.Equal([7u, 8u], server.Of<LinkPong>().Select(p => p.Id));
        Assert.Null(Assert.Single(server.Of<RelayLatency>().Single().Results).RttMs);
        // the chat line stood behind the measurement and stayed there: taken in once, over a second after the pings
        var chat = Assert.Single(Chats());
        Assert.True(chat.At - sent[2].At > TimeSpan.FromSeconds(1), $"the chat line came {(chat.At - sent[2].At).TotalMilliseconds:0} ms after the ping's answer");
        Assert.True(sent[3].At - sent[2].At > TimeSpan.FromSeconds(1), $"the measurement came {(sent[3].At - sent[2].At).TotalMilliseconds:0} ms after the ping's answer");

        List<(ServerMessage Message, DateTimeOffset At)> Chats()
        {
            lock (got)
            {
                return [.. got.Where(g => g.Message is ChatMessage)];
            }
        }
    }

    /// <summary>What a server sends is checked before it is kept: a list no longer than a lobby, every number within
    /// three digits, and a list that names no lobby is none.</summary>
    [Fact]
    public void AListOfPingsIsCheckedLikeAnyMessageOfAServer()
    {
        var many = string.Join(",", Enumerable.Range(0, 500).Select(i => $$"""{"id":"p{{i}}","ms":{{i - 5}}}"""));
        var read = Assert.IsType<LobbyPings>(ServerMessages.Checked($$"""{"type":"pings","lobbyId":"l1","pings":[{{many}},{"ms":7},null]}"""));
        Assert.Equal(60, read.Pings.Count);
        Assert.Equal((0, 54), (read.Pings[0].Ms, read.Pings[59].Ms));
        Assert.All(read.Pings, p => Assert.InRange(p.Ms, 0, 999));
        Assert.Null(ServerMessages.Checked("""{"type":"pings","pings":[]}"""));
        Assert.Null(ServerMessages.Checked("""{"type":"pings","lobbyId":"l1"}"""));
        Assert.IsType<LinkPing>(ServerMessages.Checked("""{"type":"ping","id":4000000000}"""));
    }
}
