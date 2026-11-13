using System.Net;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using LobbyView = Online.Protocol.LobbyView;
using RacerView = Online.Protocol.RacerView;

namespace Launcher.Tests;

// The launcher walked from its first screen as a new player meets it, one picture per state (WALK_SHOTS=folder; not
// set: nothing runs). WALK_DISC=a real disc image gives the pages the game's art once the launcher is open. Nothing
// is asserted beyond what the walk needs to get on: the pictures are read by eye.
public sealed partial class FirstScreenTests
{
    private static readonly int[] EveryMachine = [.. Enumerable.Range(0, GameMachines.Count)];

    private int _shot;

    private void Walk(Screen screen, AppState? state, string name)
    {
        var folder = Environment.GetEnvironmentVariable("WALK_SHOTS")!;
        Headless.PumpFor(700, state);
        Headless.Aim();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(folder);
        screen.Window.CaptureRenderedFrame()?.Save(Path.Combine(folder, $"{++_shot:00}-{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>The status every ask gets: what <see cref="Now"/> holds.</summary>
    private sealed class WalkStatus : HttpMessageHandler
    {
        public ServiceStatus Now { get; set; } = new("Piconet", DateTimeOffset.UnixEpoch, 0, [], [], [], []);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(Now with { Time = DateTimeOffset.UtcNow }, ControlProtocol.Json)),
            });
    }

    private static readonly (string Name, int Machine, Rgb Colour)[] Others =
    [
        ("BEN", 2, new Rgb(9, 9, 200)), ("CAL", 3, new Rgb(200, 9, 9)), ("DEE", 9, new Rgb(9, 200, 9)), ("EVE", 12, new Rgb(220, 120, 200)),
        ("FOX", 17, new Rgb(20, 200, 200)), ("GUS", 22, new Rgb(60, 80, 220)), ("HAL", 25, new Rgb(90, 160, 200)),
    ];

    private static ServiceStatus TwoLobbies() => new("Piconet", DateTimeOffset.UnixEpoch, 11,
        [
            new LobbyView("l1", [.. Others.Take(3).Select(o => new RacerView(o.Name, o.Machine, null))], 30, true, 1),
            new LobbyView("l2", [.. Others.Skip(3).Select(o => new RacerView(o.Name, o.Machine, null)), .. Others.Select(o => new RacerView(o.Name + "2", o.Machine, null))],
                30, false, 0, LobbyState.Racing, "m7", 8),
        ], [], [], []);

    /// <summary>Lobby l1 as the server tells it: this player and <paramref name="others"/> of the others.</summary>
    private static LobbyState WalkLobby(AppState state, string stage, int others, bool racing = false, int votes = 0, string? waiting = null, bool chosen = false) =>
        new("l1",
            [
                new PlayerInfo("p1", state.Settings.Racer.Name, state.Settings.Racer.Machine, new Rgb(200, 30, 30), 0, racing, false),
                .. Others.Take(others).Select((o, i) => new PlayerInfo($"p{i + 2}", o.Name, o.Machine, o.Colour, 0, racing, chosen)),
            ], 30, 2, stage == LobbyState.Forming && others > 0, votes, 0, stage, Waiting: waiting);

    private static RacerLive Live(int seat, string live, float distance, int total = 60_000) =>
        new(seat, seat, 2, 20_000, total, 19_000, 900, 80, 100, 0, 1000, live, 0, 0, 0, distance);

    [Fact]
    public async Task TheWalkFromTheFirstScreen()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("WALK_SHOTS") is { Length: > 0 }, "a walk for pictures: set WALK_SHOTS");
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the runner is a shell script here");
        var (hold, builds) = (Path.Combine(_dir, "hold"), Path.Combine(_dir, "builds.txt"));
        File.WriteAllText(hold, "");
        var image = _disc.Write(_dir);
        var notes = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(notes, "not a disc");
        await using var server = new LedServer();
        var state = State(stored: null, server: "ws://127.0.0.1:9/ws");     // (the status is answered here; the visit goes to the led server)
        BuildingRunner(state, hold, builds);
        using var status = new WalkStatus { Now = TwoLobbies() };
        void Send(params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();
        try
        {
            await OnTheWalk(state, status, screen =>
            {
                var main = screen.Main;
                void Shot(string name) => Walk(screen, state, name);

                // the first screen
                Shot("first-screen");
                screen.Type(Path.Combine(_dir, "F-Zero.iso"));                 // a file that is not there
                Shot("first-screen-no-such-file");
                if (screen.Continue.IsEffectivelyEnabled)
                {
                    screen.Press(screen.Continue);
                    Headless.PumpFor(1500, state);
                    Shot("first-screen-no-such-file-continue");
                }
                screen.Type(notes);                                            // a file that is no disc
                if (screen.Continue.IsEffectivelyEnabled)
                {
                    screen.Press(screen.Continue);
                    Headless.PumpFor(1500, state);
                }
                Shot("first-screen-not-a-disc");
                screen.Type(image);
                Shot("first-screen-disc-typed");
                _go.Reset();
                screen.Press(screen.Continue);
                Until(() => _asked.IsSet, "the preparation asks for the free space");
                Shot("first-screen-preparing");
                _go.Set();
                Until(() => screen.Opened, "the launcher opens");
                if (Environment.GetEnvironmentVariable("WALK_DISC") is { Length: > 0 } real)
                {
                    GameSkin.Load(real);
                }
                Until(() => state.Pipelines is { Phase: PipelinePhase.Building, Fraction: > 0.4 }, "the plain look's build starts");
                Headless.PumpTelling(state, () => main.Home.Lobbies.Count == 2, "Home listed no lobbies", 15);
                Shot("play-preparing-graphics");
                File.Delete(hold);
                Until(() => state.Pipelines.Phase == PipelinePhase.Ready, "the build ends");
                Shot("play");

                // the other places, before anything is joined
                main.Current = main.Lobbies;
                Shot("play-custom-lobbies");
                main.Current = main.Practice;
                Shot("play-practice");
                main.Current = main.Garage;
                Shot("garage");
                main.Current = main.Replays;
                Shot("replays");
                main.Current = main.Mods;
                Shot("mods");
                foreach (var tab in new[] { SettingsTab.Game, SettingsTab.Online, SettingsTab.Controller, SettingsTab.Launcher })
                {
                    main.ShowSettings(tab);
                    Shot("settings-" + tab.ToString().ToLowerInvariant());
                }
                main.Current = main.Home;
                main.Notices.ToggleListCommand.Execute(null);                  // the bell
                Shot("notices");
                main.Notices.ToggleListCommand.Execute(null);

                // RACE
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                var joining = state.JoinAsync(server.Address);                 // what RACE does, to the led server
                Headless.Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
                Shot("race-pressed");
                Send(LedServer.Welcome());
                Headless.Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
                Send(WalkLobby(state, LobbyState.Forming, 0));
                Headless.PumpTelling(state, () => state.Session?.Lobby is not null, "no lobby came");
                main.Current = main.Lobby;                                     // where RACE brings the player
                Shot("lobby-alone");
                Send(WalkLobby(state, LobbyState.Forming, 4, votes: 1));
                Headless.PumpTelling(state, () => state.Session?.Lobby?.Players.Count == 5, "nobody came");
                Shot("lobby-gathering");
                main.Current = main.Home;
                Shot("play-while-in-a-lobby");
                main.Current = main.Lobby;

                // the vote, the draw, the machines
                var vote = QuickPlayRotation.Read(TestSupport.RepositoryFolder.Protocol("protocol", "quickplay")).Vote(CoursePool.Default, new Random(4).Next);
                int[] options = [.. vote.Select(o => o.Course)];
                Send(WalkLobby(state, LobbyState.Starting, 4), new CourseVote("m1", options, 10_000, [.. vote.Select(o => o.Card)]));
                Headless.PumpTelling(state, () => main.Lobby.VoteShown && main.Lobby.CourseOptions.Count == 6, "no vote came");
                Shot("course-vote");
                Send(new CourseChosen("m1", options, [2, 1, 0, 1, 1, 0], 0, options[0], vote[0].Card));
                Headless.PumpFor(2500, state);
                Shot("course-draw");
                Send(new ChooseMachine("m1", options[0], EveryMachine, 40_000), WalkLobby(state, LobbyState.Choosing, 4, chosen: true));
                Headless.PumpTelling(state, () => main.Lobby.ChoiceShown, "no machine choice came");
                Shot("machines");
                Send(WalkLobby(state, LobbyState.Choosing, 4, chosen: true, waiting: LobbyState.WaitingForRelay));
                Headless.PumpFor(600, state);
                Shot("starting-race-server");

                // the race, as a member not in it sees it (the game is not started here)
                SeatSetup Seat(int s) => new(s, $"p{s + 2}", Others[s].Name, Others[s].Machine, Others[s].Colour);
                var race = new LobbyRace("m1", options[0], 3, [Seat(0), Seat(1), Seat(2), Seat(3)], DateTimeOffset.UtcNow);
                Send(new Requeued("m1", "latency"), WalkLobby(state, LobbyState.Racing, 4, racing: true) with
                {
                    Players = [new PlayerInfo("p1", state.Settings.Racer.Name, state.Settings.Racer.Machine, new Rgb(200, 30, 30)),
                        .. WalkLobby(state, LobbyState.Racing, 4, racing: true).Players.Skip(1)],
                    Race = race,
                });
                Headless.PumpTelling(state, () => state.Session?.Lobby?.Race is not null, "no race came");
                Send(new LiveRace("m1", new RaceSnapshot(600, 0,
                    [Live(0, RacerLive.Racing, 900), Live(1, RacerLive.Racing, 800), Live(2, RacerLive.Racing, 700), Live(3, RacerLive.Out, 100)])));
                Headless.PumpTelling(state, () => main.Lobby.Race.Standings.Count == 4, "no standings came", 15);
                Shot("too-slow-race-on");
                main.Current = main.Home;
                Shot("play-while-the-lobby-races");
                main.Current = main.Lobby;
                Headless.PumpFor(150, state);
                Send(new LiveRace("m1", new RaceSnapshot(900, 0,
                    [Live(0, RacerLive.Finished, 1000, 90_000), Live(1, RacerLive.Finished, 1000, 91_500), Live(2, RacerLive.Finished, 1000, 93_000), Live(3, RacerLive.Out, 100)])));
                Headless.PumpTelling(state, () => main.Lobby.Race.Stage == "Results", "no results came", 15);
                Shot("results");
                Send(WalkLobby(state, LobbyState.Forming, 4));
                Headless.PumpTelling(state, () => state.Session?.Lobby?.Stage == LobbyState.Forming, "the lobby did not gather again");
                Shot("lobby-after-the-race");

                // and out
                main.Control.LeaveCommand.Execute(null);
                Headless.PumpTelling(state, () => state.Session is null, "the visit did not end");
                Shot("after-leave");
            });
        }
        finally
        {
            File.Delete(hold);
        }
    }

    /// <summary>As <see cref="OnTheLauncher"/>, with a status that is answered.</summary>
    private static async Task OnTheWalk(AppState state, HttpMessageHandler status, Action<Screen> steps)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainViewModel? main = null;
        MainWindow? window = null;
        try
        {
            await session.Dispatch(() =>
            {
                GameSkin.Load(state.Settings.DiscPath);
                main = new MainViewModel(state, status);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                steps(new Screen(window, main));
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await session.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
                GameSkin.Load(null);
            }, CancellationToken.None);
            await session.DisposeAsync();
        }
    }
}
