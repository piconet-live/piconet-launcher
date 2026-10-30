using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Launcher.Core.Board;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>A mode's <c>board</c> lines on the race page, in the launcher's window: the race's as label and value, a
/// racer's as columns of the standings, a team's by team; a mode with none shows the page of before, and a player who
/// only waits in the lobby (no game, so no board) has the standings without them. A replay the game plays shows the same
/// parts over the Replays page, from the status board through the state, until its game ends. With LOBBY_SHOTS naming a
/// folder, the pages are kept there as PNGs.</summary>
[Collection(HeadlessApp.Name)]
public sealed class RaceModeBoardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static RacerLive Racer(int seat, string state, float distance) =>
        new(seat, seat, 2, 20_000, 60_000, 19_000, 900, 80, 100, 0, 1000, state, 0, 0, 0, distance);

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

    private static List<string?> Shown(MainWindow window, string name) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == name && t.IsEffectivelyVisible).Select(t => t.Text)];

    /// <summary>Until the standings are laid out, all four racers in them (a look at the page before that sees nothing,
    /// and would pass every "shows nothing").</summary>
    private static void LaidOut(MainWindow window, AppState state) =>
        PumpTelling(state, () => Shown(window, "StandingTeam").Count == 4, "the standings were not laid out", 15);

    private static List<string> Heads(MainWindow window) =>
        [.. window.GetVisualDescendants().OfType<ItemsControl>().Where(c => c.Name == "SeatHeads" && c.IsEffectivelyVisible)
            .SelectMany(c => c.Items).OfType<string>()];

    /// <summary>A mode like Knockout's and Shared tank's: every scope, a line with words among them.</summary>
    private static ModeBoardNow Mode(bool lines = true)
    {
        var head = new ModeBoardHead("Tank cut", "0102030405060708", !lines ? [] :
        [
            new(BoardModeBoardScope.Global, BoardModeBoardType.Int, 0, "Next cut", []),
            new(BoardModeBoardScope.Global, BoardModeBoardType.Seat, 1, "Leader", []),
            new(BoardModeBoardScope.Seat, BoardModeBoardType.Int, 0, "Lives", []),
            new(BoardModeBoardScope.Seat, BoardModeBoardType.Int, 1, "State", ["", "Safe", "Infected"]),
            new(BoardModeBoardScope.Team, BoardModeBoardType.Real, 0, "Tank", []),
        ]);
        int[] globals = new int[8], seats = new int[240], teams = new int[240];
        globals[0] = 12;
        globals[1] = 1;
        for (var s = 0; s < 4; s++)
        {
            seats[s * 8] = 3 - s;
            seats[(s * 8) + 1] = s;
        }
        teams[0] = BitConverter.SingleToInt32Bits(62.5f);
        teams[8] = BitConverter.SingleToInt32Bits(30f);
        return new ModeBoardNow(head, 600, globals, seats, teams);
    }

    private async Task WithWindow(Action<AppState, MainViewModel, MainWindow> test)
    {
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
                Dispatcher.UIThread.RunJobs();
                test(state, main, window);
            }, TestContext.Current.CancellationToken);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheRacePageDrawsAModesLinesOrNothing(bool lines)
    {
        await using var server = new LedServer();
        await WithWindow((state, main, window) =>
        {
            main.Current = main.Lobby;
            Dispatcher.UIThread.RunJobs();
            var joining = state.JoinAsync(server.Address, custom: true);
            Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
            SeatSetup Seat(int s, string? id, string name) => new(s, id, name, s + 2, new Rgb((byte)(40 * s), 200, 90));
            var seats = new[] { Seat(0, "p9", "MIRA"), Seat(1, "p2", "JODY"), Seat(2, null, "CPU"), Seat(3, null, "CPU") };
            var race = new LobbyRace("m1", 8, 3, seats, DateTimeOffset.UtcNow, Teams: [0, 1, 0, 1]);
            var lobby = LedServer.Lobby(LobbyState.Racing) with { LobbyId = "l1", Custom = true, HostId = "p9", Name = "Friday", Race = race, Rules = LobbyRules.QuickPlay };
            server.SendAsync(LedServer.Welcome(), lobby).GetAwaiter().GetResult();
            PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby?.Race is not null);
            main.Current = main.Lobby;   // (joining a custom lobby may have opened the custom lobbies' page meanwhile)

            // a player who waits in the lobby: the coordinator's report, no board
            server.SendAsync(new LiveRace("m1", new RaceSnapshot(300, 0,
                [Racer(0, RacerLive.Racing, 900), Racer(1, RacerLive.Racing, 800), Racer(2, RacerLive.Racing, 700), Racer(3, RacerLive.Out, 100)]))).GetAwaiter().GetResult();
            PumpTelling(state, () => main.Lobby.Race.Standings.Count == 4, "no standings came", 15);
            LaidOut(window, state);
            Assert.False(main.Lobby.Race.HasMode);
            Assert.Empty(Heads(window));

            // the game this launcher runs (a racer's or a spectator's): its board
            PumpFor(150, state);
            main.Lobby.Race.FromGame(new RaceSnapshot(600, 0,
                [Racer(0, RacerLive.Racing, 900), Racer(1, RacerLive.Racing, 800), Racer(2, RacerLive.Racing, 700), Racer(3, RacerLive.Out, 100)]), Mode(lines));
            PumpFor(200, state);
            LaidOut(window, state);
            if (!lines)
            {
                Assert.False(main.Lobby.Race.HasMode);
                Assert.Empty(Heads(window));
                Assert.Empty(Shown(window, "ModeCell"));
                Assert.Empty(Shown(window, "ModeGlobalValue"));
                Assert.Equal(268, window.GetVisualDescendants().OfType<StandingsTable>().Single(r => r.IsEffectivelyVisible).Bounds.Width);
                Shot(window, state, "race-mode-no-lines");
                return;
            }
            Assert.Equal(["Lives", "State"], Heads(window));
            Assert.Equal(["3", "", "2", "Safe", "1", "Infected", "0", "3"], ReadOrder(main, [.. Shown(window, "ModeCell").Select(t => t ?? "")]));
            Assert.Equal(["12", "JODY"], Shown(window, "ModeGlobalValue"));
            Assert.Equal(["62.5", "30"], Shown(window, "ModeTeamValue"));
            Assert.Equal(268 + 112, window.GetVisualDescendants().OfType<StandingsTable>().Single(r => r.IsEffectivelyVisible).Bounds.Width);
            var before = main.Lobby.Race.Standings.ToList();
            Shot(window, state, "race-mode-lines");

            // a line that did not move keeps its row
            PumpFor(150, state);
            main.Lobby.Race.FromGame(new RaceSnapshot(606, 0,
                [Racer(0, RacerLive.Racing, 900), Racer(1, RacerLive.Racing, 800), Racer(2, RacerLive.Racing, 700), Racer(3, RacerLive.Out, 100)]), Mode());
            PumpFor(150, state);
            Assert.Equal(before, main.Lobby.Race.Standings);

            // the game closed and the page goes on from the lobby's reports: what it last read stays for the race
            server.SendAsync(new LiveRace("m1", new RaceSnapshot(900, 0,
                [Racer(0, RacerLive.Finished, 1000), Racer(1, RacerLive.Finished, 1000), Racer(2, RacerLive.Finished, 1000), Racer(3, RacerLive.Out, 100)]))).GetAwaiter().GetResult();
            PumpTelling(state, () => main.Lobby.Race.Stage == "Results", "no results came", 15);
            PumpFor(200, state);
            Assert.Equal(["Lives", "State"], Heads(window));
            Shot(window, state, "results-mode-lines");
        });
    }

    /// <summary>The cells in the seats' order, whatever order the standings have them in.</summary>
    private static List<string> ReadOrder(MainViewModel main, List<string> shown)
    {
        var rows = main.Lobby.Race.Standings;
        var bySeat = new string[8];
        for (var i = 0; i < rows.Count; i++)
        {
            bySeat[rows[i].Seat * 2] = shown[i * 2];
            bySeat[(rows[i].Seat * 2) + 1] = shown[(i * 2) + 1];
        }
        return [.. bySeat];
    }

    [Fact]
    public async Task AReplayTheGamePlaysDrawsTheSameParts()
    {
        await WithWindow((state, main, window) =>
        {
            main.Current = main.Replays;
            Dispatcher.UIThread.RunJobs();
            Assert.False(main.Replays.Race.HasMode);
            using var board = GameBoard.Create(Path.Combine(_dir, "boards"));
            using var game = new BoardWriter(board.Path);
            var info = new ReplayInfo("r.fzrp", DateTimeOffset.UnixEpoch, 8, 3, ["MIRA", "JODY"], "MIRA", TimeSpan.FromMinutes(2),
                Teams: [0, 1, 0, 1], Racers: [new(0, "MIRA", 2, null, true), new(1, "JODY", 3, null, true), new(2, "CPU", 4, null, false), new(3, "CPU", 5, null, false)]);
            state.WatchPlayback(board, info);
            game.Write();
            board.LookNow();
            PumpTelling(state, () => main.Replays.Race.HasMode, "the replay's page showed no mode", 15);
            PumpFor(200, state);
            LaidOut(window, state);
            Assert.Equal(["Lives", "State"], Heads(window));
            Assert.Equal(["12"], Shown(window, "ModeGlobalValue"));
            Assert.Equal(["62.5", "30"], Shown(window, "ModeTeamValue"));
            Assert.Equal(4, main.Replays.Race.Standings.Count);
            Assert.Equal(["Team 1", "Team 2"], main.Replays.Race.TeamFigures.Select(t => t.Team));
            Shot(window, state, "replay-mode-lines");

            state.PlaybackOver();
            PumpTelling(state, () => !main.Replays.Race.HasMode, "the replay's page stayed", 15);
            PumpFor(100, state);
            Assert.Empty(Heads(window));
            Assert.Empty(Shown(window, "ModeGlobalValue"));
        });
    }

    /// <summary>The game's side of a board: a replay's picture with a mode.</summary>
    private sealed unsafe class BoardWriter : IDisposable
    {
        private readonly System.IO.MemoryMappedFiles.MemoryMappedFile _map;
        private readonly System.IO.MemoryMappedFiles.MemoryMappedViewAccessor _accessor;
        private readonly BoardView _view;

        public BoardWriter(string path)
        {
            _map = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete),
                null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            _accessor = _map.CreateViewAccessor();
            byte* at = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
            _view = new BoardView(at + _accessor.PointerOffset);
            _view.HeaderRunnerSize = BoardLayout.Size;
            _view.HeaderRunnerVersion = BoardLayout.Version;
        }

        public void Write()
        {
            var head = new BoardModeHead();
            head.Serial = 1;
            head.Count = 4;
            Line(head, 0, BoardModeBoardScope.Global, BoardModeBoardType.Int, 0, "Next cut");
            Line(head, 1, BoardModeBoardScope.Seat, BoardModeBoardType.Int, 0, "Lives");
            Line(head, 2, BoardModeBoardScope.Team, BoardModeBoardType.Real, 0, "Tank");
            Line(head, 3, BoardModeBoardScope.Seat, BoardModeBoardType.Int, 1, "State");
            head.Lines[3].WordCount = 2;
            head.Words[(3 * 16) + 1].Length = 4;
            "Safe"u8.CopyTo(head.Words[(3 * 16) + 1].Text);
            _view.WriteModeHead(head);
            var values = new BoardModeValues { Serial = 1, Frame = 600 };
            values.Globals[0].Value = 12;
            values.Seats[0].Value = 3;
            values.Seats[9].Value = 1;
            values.Teams[0].Value = BitConverter.SingleToInt32Bits(62.5f);
            values.Teams[8].Value = BitConverter.SingleToInt32Bits(30f);
            _view.WriteModeValues(values);
            var race = new BoardSnapshot { Serial = 1, Frame = 600, Count = 4 };
            for (var s = 0; s < 4; s++)
            {
                race.Racers[s] = new BoardRacer { Seat = (byte)s, Lap = 1, Distance = 900 - (s * 100), MaxEnergy = 100, Energy = 80 };
            }
            _view.WriteSnapshot(race);
        }

        private static void Line(BoardModeHead head, int i, BoardModeBoardScope scope, BoardModeBoardType type, ushort column, string label)
        {
            ref var line = ref head.Lines[i];
            line.Scope = scope;
            line.Type = type;
            line.Column = column;
            line.LabelLength = (uint)label.Length;
            Encoding.UTF8.GetBytes(label).CopyTo(line.Label, 0);
        }

        public void Dispose()
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _map.Dispose();
        }
    }

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
