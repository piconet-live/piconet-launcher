using Avalonia.Headless;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The race page has one source at a time: the game this launcher runs once it reports, else the coordinator's
/// report of a racer's game. A spectator gets both (the server sends its reports to everyone not racing, and a
/// spectator's game is not a racer's), and the page used to show each as it came: the player's own numbers came and
/// went, and the chat under them jumped. The game is a script that stands for the runner and stays open until told;
/// what its status board would say is handed to the page as the state hands it.</summary>
[Collection(HeadlessApp.Name)]
public sealed class RaceSourceTests : IDisposable
{
    private const int GameLap = 2;
    private const int ReportLap = 0;

    private static readonly IReadOnlyList<SeatSetup> Seats =
        [new SeatSetup(0, "p2", "BEN", 2, null), new SeatSetup(1, "p1", "ANA", 3, null), new SeatSetup(2, null, "CPU", 4, null)];

    private static readonly RaceSetup Setup = new(21, 3, Seats, 1, 2);
    private static readonly RelayEndpoint Relay = new("r1", "", 47811);
    private static readonly WatchStart Watch = new("m1", Relay, "1f2e3d4c5b6a7988", 0, Setup);
    private static readonly MatchStart Own = new("m1", Relay, "1f2e3d4c5b6a7989", 1, Setup);

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    private string Stay => Path.Combine(_dir, "stay");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The race in three racers, all on one lap: the game's and the report's are told apart by the lap.</summary>
    private static RaceSnapshot Snapshot(int lap, int localSeat, string seatOne = RacerLive.Racing) => new(600, localSeat,
    [
        new RacerLive(0, 0, lap, 20_000, 60_000, 19_000, 900, 80, 100, 0, 1000, RacerLive.Racing, 0, 0, 0, 900),
        new RacerLive(1, 1, lap, 20_000, 60_000, 19_000, 900, 80, 100, 0, 1000, seatOne, 10, 0, 0, 800),
        new RacerLive(2, 2, lap, 20_000, 60_000, 19_000, 900, 80, 100, 0, 1000, RacerLive.Racing, 20, 0, 0, 700),
    ]);

    private static LobbyState Lobby(bool racing) => LedServer.Lobby(LobbyState.Racing, racing: racing) with
    {
        Race = new LobbyRace("m1", 21, 3, Seats, DateTimeOffset.UtcNow),
    };

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            "{\"recordReplays\": false, \"racer\": {\"name\": \"ANA\", \"machine\": 6, \"color\": {\"r\": 1, \"g\": 2, \"b\": 3}}, \"discPath\": " +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        var state = AppState.Load(data);
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, $"#!/bin/sh\n[ \"$1\" = --controllers ] && exit 0\nwhile [ -e '{Stay}' ]; do sleep 0.05; done\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        var bundle = Path.Combine(_dir, "fzgx");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "mod.bin"), "");
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game"), Bundle: bundle) });
        return state;
    }

    private async Task WithPage(Action<AppState, LedServer, RaceViewModel> test)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;   // (the game is a shell script)
        }
        await using var server = new LedServer();
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var page = new RaceViewModel(state);
                var joining = state.JoinAsync(server.Address);
                Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
                test(state, server, page);
                Assert.True(joining.IsCompleted);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(Stay);
            await state.LeaveAsync();
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    /// <summary>Into the lobby's race as a spectator, the spectator's game open.</summary>
    private void Watching(AppState state, LedServer server)
    {
        server.SendAsync(LedServer.Welcome() with { Spectators = true }, Lobby(racing: false)).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Session is { CanWatch: true }, "no lobby to watch");
        state.Session!.WatchAsync(true).GetAwaiter().GetResult();
        File.WriteAllText(Stay, "");
        server.SendAsync(Watch).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Racing && state.Session is { Race.Spectate: true }, "the spectator's game did not start");
    }

    /// <summary>A coordinator's report, seen by the session and told to the page.</summary>
    private static void Report(AppState state, LedServer server, int frame)
    {
        PumpFor(120);   // (past the page's tenth of a second between refreshes; nothing told meanwhile: a change would show the last report again)
        var snapshot = Snapshot(ReportLap, 0) with { Frame = frame };
        server.SendAsync(new LiveRace("m1", snapshot)).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Session?.Live?.Snapshot.Frame == frame, "the report did not come");
        state.Update(s => s);
        PumpFor(20);
    }

    private static void FromTheGame(AppState state, RaceViewModel page, int localSeat)
    {
        PumpFor(120);
        page.FromGame(Snapshot(GameLap, localSeat), null);
        PumpFor(20);
    }

    /// <summary>A game's first reports: the first only takes the page over, as it may come within the tenth of a second
    /// of the coordinator's last, and the numbers follow with the next.</summary>
    private static void GameTakesOver(AppState state, RaceViewModel page, int localSeat)
    {
        FromTheGame(state, page, localSeat);
        FromTheGame(state, page, localSeat);
    }

    private void GameCloses(AppState state)
    {
        File.Delete(Stay);
        PumpTelling(state, () => !state.Racing && state.Session is { Race: null }, "the game did not close");
    }

    [Fact]
    public async Task ASpectatorsPageTakesTheGameAloneAndHoldsStill() => await WithPage((state, server, page) =>
    {
        Watching(state, server);

        // the game still loads, up to half a minute: the coordinator's report carries the page meanwhile
        Report(state, server, 1);
        Assert.Equal("Lap 1/3", page.LapText);
        Assert.Equal("Watching", page.Stage);

        // the game reports, and the coordinator goes on: the page stays on the game
        var hasYou = new List<bool>();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(page.HasYou))
            {
                hasYou.Add(page.HasYou);
            }
        };
        GameTakesOver(state, page, localSeat: 0);    // (the server gives a spectator's game the first player's seat)
        var shown = new List<(bool HasYou, string Stage, string Lap)>();
        var turned = new List<string>();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(page.HasYou) or nameof(page.Stage) or nameof(page.LapText))
            {
                turned.Add(e.PropertyName!);
            }
        };
        for (var i = 0; i < 4; i++)
        {
            FromTheGame(state, page, localSeat: 0);
            shown.Add((page.HasYou, page.Stage, page.LapText));
            Report(state, server, 10 + i);
            shown.Add((page.HasYou, page.Stage, page.LapText));
        }
        Assert.Empty(hasYou);   // not once, between any two looks
        Assert.All(shown, s => Assert.Equal((false, "Watching", "Lap 3/3"), s));
        Assert.Empty(turned);
        Assert.DoesNotContain(page.Standings, r => r.You);
        Assert.DoesNotContain(page.MapRacers, r => r.You);

        // a racer pressed stays followed through both
        page.FollowCommand.Execute(page.Standings.Single(r => r.Seat == 2));
        FromTheGame(state, page, localSeat: 0);
        Report(state, server, 20);
        Assert.Equal(2, page.Followed);
        Assert.True(page.Standings.Single(r => r.Seat == 2).Followed);

        // its game closes: the coordinator's report at once
        GameCloses(state);
        Report(state, server, 30);
        Assert.Equal((false, "Watching", "Lap 1/3"), (page.HasYou, page.Stage, page.LapText));
        Assert.Equal(2, page.Followed);

        // a snapshot of the closed game that was still on its way moves nothing
        page.FromGame(Snapshot(GameLap, 0), null);
        Assert.Equal((false, "Lap 1/3"), (page.HasYou, page.LapText));
    });

    /// <summary>The player's own game: their numbers whatever the server says of them (it takes a desynced player out
    /// of the race while the game is still open) and whatever it reports meanwhile. When the game has closed they
    /// watch as anyone (Watch on the race left for good); starting again (Rejoin) their numbers are back.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APlayersOwnGameKeepsTheirNumbersUntilItCloses(bool serverSaysRacing) => await WithPage((state, server, page) =>
    {
        server.SendAsync(LedServer.Welcome() with { Spectators = true }, Lobby(serverSaysRacing)).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Session is { CanWatch: true }, "no lobby");
        File.WriteAllText(Stay, "");
        server.SendAsync(Own).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Racing && state.Session is { Race.Spectate: false }, "the game did not start");

        GameTakesOver(state, page, localSeat: 1);
        for (var i = 0; i < 3; i++)
        {
            FromTheGame(state, page, localSeat: 1);
            Assert.Equal((true, "Racing", "Lap 3/3"), (page.HasYou, page.Stage, page.LapText));
            Report(state, server, 10 + i);
            Assert.Equal((true, "Racing", "Lap 3/3"), (page.HasYou, page.Stage, page.LapText));
        }
        Assert.Contains(page.Standings, r => r.You && r.Seat == 1);

        // the game closed; the server no longer has them racing: the lobby's reports
        GameCloses(state);
        server.SendAsync(Lobby(racing: false)).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Session is { Racing: false }, "the lobby did not say so");
        Report(state, server, 20);
        Assert.Equal((false, "Watching", "Lap 1/3"), (page.HasYou, page.Stage, page.LapText));

        // they watch the race they left
        state.Session!.WatchAsync(true).GetAwaiter().GetResult();
        File.WriteAllText(Stay, "");
        server.SendAsync(Watch).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Racing && state.Session is { Race.Spectate: true }, "the spectator's game did not start");
        GameTakesOver(state, page, localSeat: 0);
        Report(state, server, 21);
        Assert.Equal((false, "Watching", "Lap 3/3"), (page.HasYou, page.Stage, page.LapText));
        GameCloses(state);

        // and race it again in a game of their own
        File.WriteAllText(Stay, "");
        server.SendAsync(Own).GetAwaiter().GetResult();
        PumpTelling(state, () => state.Racing && state.Session is { Race.Spectate: false }, "the game did not start again");
        GameTakesOver(state, page, localSeat: 1);
        Assert.Equal((true, "Racing", "Lap 3/3"), (page.HasYou, page.Stage, page.LapText));
    });
}
