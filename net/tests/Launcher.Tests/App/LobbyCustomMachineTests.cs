using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// Another racer's garage-built machine in the lobby's list, in the launcher's own window against a server the test
/// leads (<see cref="LedServer"/>): a member the lobby's state names with a custom machine
/// (<see cref="PlayerInfo.Custom"/>) is listed with it, never with the stock machine under it; one the game cannot
/// build is the stock machine, as their race seat would be; a member with none, the stock machine, as before. (No
/// disc's naming tables here: a custom machine is <see cref="RacedMachine.UnnamedCustom"/>, the page's name before
/// they are read; the names by the game's rule are CustomBuilderTests'.)
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class LobbyCustomMachineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static LobbyState Lobby(CustomMachine? bens, CustomMachine? cals) =>
        LedServer.Lobby(LobbyState.Forming) with
        {
            Players =
            [
                new PlayerInfo("p1", "ANA", 6, new Rgb(1, 2, 3), 0),
                new PlayerInfo("p2", "BEN", 2, new Rgb(9, 9, 200), 0, Custom: bens),
                new PlayerInfo("p3", "CAL", 3, new Rgb(200, 9, 9), 0, Custom: cals),
            ],
        };

    private static Dictionary<string, string> MachineColumn(LobbyViewModel lobby) =>
        lobby.SeatColumns.SelectMany(c => c.Seats).Where(s => !s.Open).ToDictionary(s => s.Name, s => s.Machine);

    [Fact]
    public async Task AnotherRacersCustomMachineIsListedInPlaceOfTheirStockMachine()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var server = new LedServer();
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}}""");
        var state = AppState.Load(data);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        void Send(params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();
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
                Send(LedServer.Welcome());
                Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                // BEN races a custom machine; CAL names one with a cockpit in the booster's place: no machine
                Send(Lobby(new CustomMachine(15, 44, 58, 2), new CustomMachine(15, 44, 30, 3)));
                PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
                main.Current = main.Lobby;
                var lobby = main.Lobby;
                PumpTelling(state, () => MachineColumn(lobby).Count == 3);

                Assert.Null(state.Session!.Lobby!.Players.Single(p => p.Name == "CAL").Custom);
                Assert.Equal(new Dictionary<string, string>
                {
                    ["ANA"] = Machines.Names[6], ["BEN"] = RacedMachine.UnnamedCustom, ["CAL"] = Machines.Names[3],
                }, MachineColumn(lobby));

                // BEN goes back to his stock machine: his row says so
                Send(Lobby(null, null));
                PumpTelling(state, () => MachineColumn(lobby)["BEN"] == Machines.Names[2]);
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
}
