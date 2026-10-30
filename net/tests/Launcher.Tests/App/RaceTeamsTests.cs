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

namespace Launcher.Tests;

/// <summary>A racer's team on the race page and in its results, in Home's standings model, from the lobby's race; a race
/// without teams says none. With LOBBY_SHOTS naming a folder, the pages are kept there as PNGs.</summary>
[Collection(HeadlessApp.Name)]
public sealed class RaceTeamsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly string[] Teamed = ["Team 2", "Team 1", "Team 2", "Team 1"];

    private static readonly string[] Unteamed = ["", "", "", ""];

    private static RacerLive Racer(int seat, string state, float distance, int total = 60_000) =>
        new(seat, seat, 2, 20_000, total, 19_000, 900, 80, 100, 0, 1000, state, 0, 0, 0, distance);

    private static void Shot(MainWindow window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(400, state);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EachRacersTeamShowsWhileRacingAndInTheResults(bool teams)
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
                window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                window.Show();
                main.Current = main.Lobby;
                Dispatcher.UIThread.RunJobs();
                var joining = state.JoinAsync(server.Address, custom: true);
                Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
                SeatSetup Seat(int s, string? id, string name) => new(s, id, name, s + 2, new Rgb((byte)(40 * s), 200, 90));
                var seats = new[] { Seat(0, "p9", "MIRA"), Seat(1, "p2", "JODY"), Seat(2, null, "CPU"), Seat(3, null, "CPU") };
                var race = new LobbyRace("m1", 8, 3, seats, DateTimeOffset.UtcNow, Teams: teams ? [1, 0, 1, 0] : null);
                var lobby = LedServer.Lobby(LobbyState.Racing) with { LobbyId = "l1", Custom = true, HostId = "p9", Name = "Friday", Race = race, Rules = LobbyRules.QuickPlay };
                server.SendAsync(LedServer.Welcome(), lobby).GetAwaiter().GetResult();
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby?.Race is not null);
                server.SendAsync(new LiveRace("m1", new RaceSnapshot(600, 0,
                    [Racer(0, RacerLive.Racing, 900), Racer(1, RacerLive.Racing, 800), Racer(2, RacerLive.Racing, 700), Racer(3, RacerLive.Out, 100)]))).GetAwaiter().GetResult();
                PumpTelling(state, () => main.Lobby.Race.Standings.Count == 4, "no standings came", 15);
                Assert.Equal(teams ? Teamed : Unteamed, main.Lobby.Race.Standings.Select(s => s.Team));
                PumpFor(200, state);
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "StandingTeam" && t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                Assert.Equal(teams ? Teamed : [], shown);
                Shot(window, state, teams ? "race-teams" : "race-no-teams");

                PumpFor(150, state);   // (the standings change at 10 a second)
                server.SendAsync(new LiveRace("m1", new RaceSnapshot(900, 0,
                    [Racer(0, RacerLive.Finished, 1000, 90_000), Racer(1, RacerLive.Finished, 1000, 91_500), Racer(2, RacerLive.Finished, 1000, 93_000), Racer(3, RacerLive.Out, 100)]))).GetAwaiter().GetResult();
                PumpTelling(state, () => main.Lobby.Race.Stage == "Results", "no results came", 15);
                Assert.Equal(teams ? Teamed : Unteamed, main.Lobby.Race.Standings.Select(s => s.Team));
                Shot(window, state, teams ? "results-teams" : "results-no-teams");

                if (teams)
                {
                    main.Current = main.Home;
                    PumpTelling(state, () => main.Home.Standings.Count > 0, "Home showed no standings", 15);
                    Assert.Equal(Teamed, main.Home.Standings.Select(s => s.Team));
                }
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

    /// <summary>Answers every ask for the status, without a network.</summary>
    private sealed class StatusAnswers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(
                    new ServiceStatus("test", DateTimeOffset.UnixEpoch, 1, [], [], [], []), ControlProtocol.Json)),
            });
    }
}
