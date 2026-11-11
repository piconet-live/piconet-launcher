using System.Net;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using LobbyView = Online.Protocol.LobbyView;
using RacerView = Online.Protocol.RacerView;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Play page's Quick Play lobbies are joined by pressing their row (the owner, 2026-11-11: "Users need to be able
/// to join whichever lobby they'd like from the main menu"), in the launcher's own window against a server the test
/// leads (<see cref="LedServer"/>) and a status it answers itself: the visit names the lobby instead of queueing, the
/// player's own row opens the lobby's page, and a lobby that does not take them is said where the last problem is.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class HomeJoinLobbyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Two Quick Play lobbies: BEN's l1 with room, CAL's l2 full.</summary>
    private static readonly ServiceStatus Status = new("test", DateTimeOffset.UnixEpoch, 2,
        [
            new LobbyView("l1", [new RacerView("BEN", 2, "#0909C8")], 30, false, 0),
            new LobbyView("l2", [new RacerView("CAL", 3, "#C80909")], 1, false, 0, LobbyState.Racing),
        ], [], [], []);

    [Fact]
    public Task ARowPressedJoinsThatLobbyAndThePlayersOwnRowOpensItsPage() => InTheWindow((state, main, server) =>
    {
        var home = main.Home;
        PumpTelling(state, () => home.Lobbies.Count == 2, "Home listed no lobbies", 15);
        Assert.Equal([("Lobby 1", LobbyRow.Join, true), ("Lobby 2", LobbyRow.Full, true)],
            home.Lobbies.Select(r => (r.Name, r.Tag, r.Joins)));
        var pressed = new List<string>();
        home.JoinLobby = id =>
        {
            pressed.Add(id);
            return state.JoinAsync(server.Address, lobby: id);
        };
        var joining = home.OpenRowCommand.ExecuteAsync(home.Lobbies[0]);
        Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
        server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
        Pump(() => server.Of<JoinLobby>().Count == 1, "the lobby was not named");
        Assert.Equal(["l1"], pressed);
        Assert.Equal(("l1", null, 6), (server.Of<JoinLobby>()[0].LobbyId, server.Of<JoinLobby>()[0].Password, server.Of<JoinLobby>()[0].Machine));
        Assert.Empty(server.Of<EnterQuickPlay>());   // no queue: the server would pick a lobby
        Assert.Equal(QuickPlaySession.JoiningLobbyText, state.Session?.Status);

        server.SendAsync(LedServer.Lobby(LobbyState.Forming)).GetAwaiter().GetResult();
        PumpTelling(state, () => joining.IsCompleted && home.Lobbies.Any(r => r.Mine), "the lobby joined is not the player's own row");
        var own = home.Lobbies.Single(r => r.Mine);
        Assert.Equal(("Lobby 1", false), (own.Name, own.Joins));
        main.Current = home;
        home.OpenRowCommand.Execute(own);
        Assert.Same(main.Lobby, main.Current);
        Assert.Single(server.Of<JoinLobby>());       // their own lobby is not joined again

        // from their lobby into another that is full: they stay, and it is said
        state.Session!.JoinLobbyAsync("l2").GetAwaiter().GetResult();
        Pump(() => server.Of<JoinLobby>().Count == 2, "the second lobby was not named");
        server.SendAsync(new ErrorMessage(CustomLobby.LobbyFull, "the lobby is full")).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Problem is not null, "the refusal was not said");
        Assert.Equal("Lobby full", state.Problem);
        Assert.Equal("l1", state.Session?.Lobby?.LobbyId);
    });

    [Fact]
    public Task ALobbyThatDoesNotTakeThePlayerEndsTheVisitAndSaysWhy() => InTheWindow((state, main, server) =>
    {
        var joining = state.JoinAsync(server.Address, lobby: "l2");
        Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
        server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
        Pump(() => server.Of<JoinLobby>().Count == 1, "the lobby was not named");
        server.SendAsync(new ErrorMessage(CustomLobby.LobbyClosed, "the lobby's race is starting; join once it is on")).GetAwaiter().GetResult();
        PumpTelling(state, () => joining.IsCompleted && state.Session is null, "the visit did not end");
        Assert.Equal("Lobby starting a race · join once it is on", state.Problem);
        Assert.Equal("Lobby gone", AppState.ChosenLobbyRefused(CustomLobby.NoSuchLobby));
    });

    /// <summary>A lobby whose match is starting takes the player (the owner, 2026-11-11: "Players shouldn't be placed
    /// in new lobbies there"): they are its member outside that match (<see cref="PlayerInfo.NextRace"/>), so the server
    /// sends them no vote and no machine choice, the page says they race the next race, and the ready count is the
    /// racers'. LOBBY_SHOTS=folder saves the pages (join-mid-*.png).</summary>
    [Fact]
    public Task ALobbyJoinedWhileItsMatchStartsIsWaitedIn() => InTheWindow((state, main, server) =>
    {
        var joining = state.JoinAsync(server.Address, lobby: "l1");
        Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
        server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
        Pump(() => server.Of<JoinLobby>().Count == 1, "the lobby was not named");
        server.SendAsync(Mid(LobbyState.Starting)).GetAwaiter().GetResult();
        PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby?.Stage == LobbyState.Starting, "the lobby did not come");
        Shot(state, "join-mid-vote-as-brought");
        main.Current = main.Lobby;
        Assert.Equal((QuickPlaySession.NextRaceText, false), (main.Lobby.StepNote, main.Lobby.VoteShown));
        Shot(state, "join-mid-vote-lobby");
        server.SendAsync(Mid(LobbyState.Choosing)).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Session?.Lobby?.Stage == LobbyState.Choosing && main.Lobby.ReadyCount.Length > 0, "the machine step did not come");
        Assert.Equal((QuickPlaySession.NextRaceText, "0 of 3"), (main.Lobby.StepNote, main.Lobby.ReadyCount));   // the racers' count
        Shot(state, "join-mid-machines-lobby");
        main.Current = main.Home;
        Shot(state, "join-mid-machines-play");
    });

    /// <summary>Lobby l1 with its match under way among BEN, CAL and DEE, and this player (p1, ANA) just in.</summary>
    private static LobbyState Mid(string stage) => new("l1",
        [
            new PlayerInfo("p2", "BEN", 2, new Rgb(9, 9, 200), 0, false, false),
            new PlayerInfo("p3", "CAL", 3, new Rgb(200, 9, 9), 0, false, false),
            new PlayerInfo("p4", "DEE", 9, new Rgb(9, 200, 9), 0, false, false),
            new PlayerInfo("p1", "ANA", 6, new Rgb(1, 2, 3), 0, false, false, NextRace: true),
        ], 30, 2, false, 0, 0, stage);

    private MainWindow? _window;

    private void Shot(AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder || _window is null)
        {
            return;
        }
        PumpFor(400, state);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        _window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private async Task InTheWindow(Action<AppState, MainViewModel, LedServer> test)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new LedServer();
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            "{\"server\": \"ws://127.0.0.1:9/ws\", \"recordReplays\": false, \"racer\": {\"name\": \"ANA\", \"machine\": 6, \"color\": {\"r\": 1, \"g\": 2, \"b\": 3}}, \"discPath\": " +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        using var answers = new StatusAnswers();
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state, answers);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                _window = window;
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                test(state, main, server);
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

    /// <summary>Answers every ask for the status with <see cref="Status"/>, without a network.</summary>
    private sealed class StatusAnswers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(Status, ControlProtocol.Json)),
            });
    }
}
