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
/// Spectator seats in the lobby's list, in the launcher's own
/// window against a server the test leads message by message (<see cref="LedServer"/>): where the server takes
/// spectators, a watcher has no seat but a row in the watchers' list beside the seats, with no number and no tick,
/// and the title counts both ("Racers 1 of 30 · Watching 1"); the player's own row moves to the watchers when they
/// press Watch and back when they press Race, and while they watch the course cards take no press and send no vote.
/// A server that takes no spectators shows nothing new.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class LobbyWatchingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>ANA (this player) and BEN, either watching.</summary>
    private static LobbyState Lobby(string stage, bool anaWatches, bool benWatches) =>
        LedServer.Lobby(stage) with
        {
            Players =
            [
                new PlayerInfo("p1", "ANA", 6, new Rgb(1, 2, 3), 0, Watching: anaWatches),
                new PlayerInfo("p2", "BEN", 2, new Rgb(9, 9, 200), 0, Watching: benWatches),
            ],
        };

    private static List<SeatRow> Seated(LobbyViewModel lobby) => [.. lobby.SeatColumns.SelectMany(c => c.Seats).Where(s => !s.Open)];

    [Fact]
    public async Task AWatcherHasARowBesideTheSeatsAndThePlayersOwnRowMovesBetweenTheTwo()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome() with { Spectators = true }, Lobby(LobbyState.Forming, false, true), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            PumpTelling(state, () => lobby.WatchingShown);

            // BEN watches: no seat, a row in the watchers' list (no number, no tick); the title counts both
            Assert.Equal("Racers 1 of 30 · Watching 1", lobby.RosterTitle);
            Assert.Equal(["ANA"], Seated(lobby).Select(s => s.Name));
            Assert.Equal("1", Seated(lobby)[0].Number);
            var ben = Assert.Single(lobby.WatchingRows);
            Assert.Equal(("BEN", "", false, false, "#0909C8"), (ben.Name, ben.Number, ben.Ticked, ben.You, ben.ColorHex));
            Assert.Equal(30, lobby.SeatColumns.Sum(c => c.Seats.Count));          // the seats are the race's 30 still
            Assert.True(Named<StackPanel>(view, "WatchingList").IsEffectivelyVisible);

            // ANA presses Watch: the launcher says so, and with the server's next word her row moves to the watchers
            Click(window, Named<Button>(view, "WatchButton"));
            Pump(() => server.Of<WatchRace>() is [{ Yes: true }], "Watch sent nothing");
            Send(server, Lobby(LobbyState.Forming, true, true));
            PumpTelling(state, () => lobby.WatchingRows.Count == 2);
            Assert.Equal("Racers 0 of 30 · Watching 2", lobby.RosterTitle);
            Assert.Empty(Seated(lobby));
            Assert.Equal([("ANA", true), ("BEN", false)], lobby.WatchingRows.Select(r => (r.Name, r.You)));
            Assert.Equal("Race", lobby.WatchText);
            Assert.False(lobby.CanVoteStart);                                       // (a watcher has no vote)

            // and Race: back to a seat
            Click(window, Named<Button>(view, "WatchButton"));
            Pump(() => server.Of<WatchRace>().Count == 2 && !server.Of<WatchRace>()[1].Yes, "Race sent nothing");
            Send(server, Lobby(LobbyState.Forming, false, true));
            PumpTelling(state, () => lobby.WatchingRows.Count == 1);
            Assert.Equal(["ANA"], Seated(lobby).Select(s => s.Name));
            Assert.True(Seated(lobby)[0].You);
            Assert.Equal("Racers 1 of 30 · Watching 1", lobby.RosterTitle);
        });
    }

    [Fact]
    public async Task AWatcherHasNoVoteOnTheCourses()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome() with { Spectators = true }, Lobby(LobbyState.Forming, false, false), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            Send(server, new CourseVote("m1", [1, 16, 26, 8, 5, CourseVote.RandomCourse], 30_000), Lobby(LobbyState.Starting, false, false));
            PumpTelling(state, () => lobby.VoteShown && lobby.CourseOptions.Count == 6);
            state.Session!.WatchAsync(true).GetAwaiter().GetResult();
            Send(server, Lobby(LobbyState.Starting, true, false));
            PumpTelling(state, () => lobby.Watching && lobby.WatchingRows.Count == 1);
            Dispatcher.UIThread.RunJobs();
            // the cards take no press, and the pad passes them by
            var cards = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("votecard")).ToList();
            Assert.Equal(6, cards.Count);
            Assert.All(cards, c => Assert.False(c.IsHitTestVisible || Launcher.App.Controls.PadNav.IsTarget(c)));
            lobby.VoteCourseCommand.Execute(lobby.CourseOptions[1]);
            PumpFor(300, state);
            Assert.Empty(server.Of<VoteCourse>());
        });
    }

    [Fact]
    public async Task AServerThatTakesNoSpectatorsShowsNothingNew()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), Lobby(LobbyState.Forming, false, false), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            PumpTelling(state, () => lobby.RosterTitle.Length > 0);
            Assert.Equal("Racers 2 of 30", lobby.RosterTitle);
            Assert.Empty(lobby.WatchingRows);
            Assert.False(Named<StackPanel>(view, "WatchingList").IsEffectivelyVisible);
            Assert.False(Named<Button>(view, "WatchButton").IsEffectivelyVisible);
        });
    }

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data);
    }

    /// <summary>The launcher's window, joined to the led server and welcomed into a lobby; then the test's steps, on
    /// the window's thread (as LobbyStageRulesTests, whose order of putting things away this keeps).</summary>
    private async Task InALobby(LedServer server, Welcome welcome, LobbyState first, Action<MainWindow, MainViewModel, AppState> steps)
    {
        var cancel = TestContext.Current.CancellationToken;
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
                Dispatcher.UIThread.RunJobs();
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Connections == 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
                Send(server, welcome);
                Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                Send(server, first);
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = main.Lobby;
                Dispatcher.UIThread.RunJobs();
                steps(window, main, state);
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

    private static void Send(LedServer server, params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();
}
