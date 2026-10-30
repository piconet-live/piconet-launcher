using System.Net;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using LobbyView = Online.Protocol.LobbyView;
using RacerView = Online.Protocol.RacerView;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// Home lists Quick Play's lobbies only, in the launcher's own window against a server the test leads
/// (<see cref="LedServer"/>) and a status it answers itself (the settings' server is another address, which only
/// the status is asked of): a player in a custom lobby sees Home as from outside
/// Quick Play (no "your lobby", no Quick Play lobby of theirs), and their own custom lobby is no row of the table,
/// though a server from before lists it in its status.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class HomeCustomLobbyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A server from before: ANA's custom lobby l1 in its status beside BEN's Quick Play lobby l2.</summary>
    private static readonly ServiceStatus Status = new("test", DateTimeOffset.UnixEpoch, 2,
        [
            new LobbyView("l1", [new RacerView("ANA", 6, "#010203")], 30, false, 0),
            new LobbyView("l2", [new RacerView("BEN", 2, "#0909C8")], 30, false, 0),
        ], [], [], []);

    [Fact]
    public async Task APlayerInACustomLobbySeesQuickPlaysLobbiesOnly()
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
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                var joining = state.JoinAsync(server.Address, custom: true);
                Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
                server.SendAsync(LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming) with { LobbyId = "l1", Custom = true, HostId = "p1",
                    Rules = LobbyRules.QuickPlay, Name = "ANA's lobby" }).GetAwaiter().GetResult();
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                var home = main.Home;
                PumpTelling(state, () => home.Lobbies.Count > 0, "Home listed no lobby", 15);

                var row = Assert.Single(home.Lobbies);
                Assert.Equal(("Lobby 2", "Quick Play", false), (row.Name, row.Rules, row.Mine));
                Assert.NotEqual("Your lobby", home.Over);
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
