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
using MachineChoice = Online.Protocol.MachineChoice;

namespace Launcher.Tests;

/// <summary>
/// What the page shows is what races, in the launcher's own window against a server the
/// test leads message by message (<see cref="LedServer"/>), which keeps what the launcher sends: a player who is
/// ready stays ready with every pick; a stage the server has closed is closed on the page, nothing more is sent,
/// and the machine shown is the one the server has; the last send leaves room for a message that came late;
/// nothing of one session's matches stands for the next session's, whose server names them alike; a connection
/// that ends takes the server's machine list with it; and the machine picked in the Garage is the player's
/// everywhere.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed partial class LobbyStageRulesTests : IDisposable
{
    private static readonly int[] All = [.. Enumerable.Range(0, GameMachines.Count)];

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        // (with a disc image that passes the first look: the launcher's pages show, not its first screen)
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data);
    }

    private static void ClickCell(Window window, Control view, int machine)
    {
        Pump(() => CardOf(view, machine) is not null, $"machine {machine} has no cell");
        var cell = CardOf(view, machine)!;
        cell.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Click(window, cell);
    }

    /// <summary>The launcher's window, joined to the led server and welcomed into a lobby; then the test's steps,
    /// on the window's thread.</summary>
    private async Task InALobby(LedServer server, Welcome welcome, LobbyState first, Action<MainWindow, MainViewModel, AppState> steps,
        Action<MainWindow, MainViewModel, AppState>? before = null)
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
                before?.Invoke(window, main, state);
                Join(server, state, welcome, first);
                main.Current = main.Lobby;
                Dispatcher.UIThread.RunJobs();
                steps(window, main, state);
            }, cancel);
        }
        finally
        {
            // The sessions end first, while the window's thread is still there to take what they say as they
            // end (a page brings it to "the application's thread"): said after the application is put away, it
            // would meet the next test's application as that is made ("a different thread owns it", seen in
            // about one run in three).
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

    private static void Join(LedServer server, AppState state, Welcome welcome, LobbyState first)
    {
        var connections = server.Connections;
        var joining = state.JoinAsync(server.Address);
        Pump(() => server.Connections == connections + 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
        server.SendAsync(welcome).GetAwaiter().GetResult();
        Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");
        server.SendAsync(first).GetAwaiter().GetResult();
        PumpTelling(state, () => joining.IsCompleted && state.Session?.Lobby is not null);
    }

    private static void Send(LedServer server, params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();

    /// <summary>The machine choice the launcher sent last; null: none yet.</summary>
    private static MachineChoice? LastChoice(LedServer server) => server.Of<MachineChoice>() is { Count: > 0 } sent ? sent[^1] : null;

    /// <summary>Ready with one machine, then another picked with no second Ready: it is sent at once, and so is a
    /// colour changed after that. Then the server closes the stage with the machine it had (a pick that crossed the
    /// last racer's Ready): Ready is gone from the page, the Garage and the left column show the machine the server
    /// races, and a pick made after that sends nothing.</summary>
    [Fact]
    public async Task AReadyPlayerStaysReadyWithEveryPickAndAClosedStageShowsWhatTheServerRaces()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(setting: SettingRange.Free), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            Send(server, new ChooseMachine("m1", 21, All, 40_000), LedServer.Lobby(LobbyState.Choosing));
            PumpTelling(state, () => lobby.ChoiceShown && lobby.GarageShown);
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            var ready = Named<Button>(view, "Ready");
            Assert.Equal(6, lobby.Garage.MachineIndex);
            var confirms = 0;
            void Hear(LauncherSound sound, int volume) => confirms += sound == LauncherSound.Confirm ? 1 : 0;
            Launcher.App.Attention.Sounded += Hear;
            try
            {
                Click(window, ready);                                                  // READY: the game's confirm
            }
            finally
            {
                Launcher.App.Attention.Sounded -= Hear;
            }
            Assert.Equal(1, confirms);
            Pump(() => server.Of<MachineChoice>().Count == 1);
            Assert.Equal(6, server.Of<MachineChoice>()[0].Machine);
            PumpTelling(state, () => lobby.IsReady && Named<Border>(view, "IsReady").IsEffectivelyVisible);

            // another machine: its PREVIEW shows READY again (ready = this machine); pressed, it is sent at once
            ClickCell(window, view, 17);
            Pump(() => lobby.CanReady && ready.IsEffectivelyVisible);
            Click(window, ready);
            Pump(() => server.Of<MachineChoice>().Count == 2, "a machine readied after Ready was not sent");
            Assert.Equal(17, server.Of<MachineChoice>()[1].Machine);
            PumpTelling(state, () => lobby.IsReady && lobby.ReadyMachine == 17);
            Assert.False(ready.IsEffectivelyVisible);
            // its second stock colour, and its setting (in its PREVIEW): sent too, with no Ready and no wait for the
            // settings to be kept
            lobby.Garage.PreviewMachineCommand.Execute(17);
            PumpFor(150, state);
            Assert.True(lobby.IsReady);
            var swatch = view.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is StockSwatch { Index: 1 });
            swatch.IsEnabled = true;                                                   // (no disc here to read the colours from)
            Click(window, swatch);
            Pump(() => LastChoice(server) is { Machine: 17, StockColor: 1 }, "a colour changed after Ready was not sent");
            lobby.Garage.Setting = 35;
            Pump(() => LastChoice(server) is { Machine: 17, StockColor: 1, Setting: 35 }, "a setting changed after Ready was not sent");
            var sent = server.Of<MachineChoice>().Count;

            // the server closes the stage: everyone is ready, the lobby waits for its race server, and what it has
            // for ANA is the Blue Falcon (as when the pick crossed the last racer's Ready on its way)
            Send(server, LedServer.Lobby(LobbyState.Choosing, machine: 6, chosen: true, waiting: LobbyState.WaitingForRelay, otherChosen: true));
            PumpTelling(state, () => !lobby.ChoiceShown && lobby.Raced is not null);
            Assert.False(ready.IsEffectivelyVisible);
            Assert.False(Named<Border>(view, "IsReady").IsEffectivelyVisible);         // no "Ready ✓" over a choice that is closed
            Assert.False(lobby.GarageShown);
            Assert.Equal("", Named<TextBlock>(view, "Clock").Text);
            Assert.Equal((6, 6, 6, "Blue Falcon"), (lobby.Garage.MachineIndex, state.Settings.Racer.Machine, main.ShownMachine, main.Account.Machine));

            // a pick now is for the next race: nothing is sent, and the left column keeps the machine that races
            Click(window, Named<Button>(view, "Face"));
            Assert.True(lobby.GarageShown);
            ClickCell(window, view, 20);
            state.Session!.ChooseMachineAsync(20).GetAwaiter().GetResult();
            PumpFor(300, state);
            Assert.Equal(sent, server.Of<MachineChoice>().Count);
            Assert.Equal((20, 6, "Blue Falcon"), (lobby.Garage.MachineIndex, main.ShownMachine, main.Account.Machine));
            Assert.DoesNotContain(Named<Grid>(view, "Band").GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "READY"));
        });
    }

    /// <summary>A player who never presses Ready: the machine selected is sent while there is still time when the
    /// server's message came a second late (the launcher's clock then runs a second behind the server's): three
    /// seconds before the launcher's clock ends, not one and a half.</summary>
    [Fact]
    public async Task TheLastSendLeavesRoomForAMessageThatCameLate()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            // six seconds, says the message; it was a second on its way, so the server's stage ends in five
            var told = DateTimeOffset.UtcNow;
            Send(server, new ChooseMachine("m1", 21, All, 6000), LedServer.Lobby(LobbyState.Choosing));
            PumpTelling(state, () => server.Of<MachineChoice>().Count == 1, "the machine selected was never sent");
            var after = (server.Received.Last(r => r.Message is MachineChoice).At - told).TotalSeconds;
            Assert.InRange(after, 2.0, 4.0);                                           // a second to spare at the server, at the least
            Assert.Equal(6, server.Of<MachineChoice>()[0].Machine);
        });
    }

    /// <summary>A server restarted names its first match as the one before named its first. With the launcher left
    /// open: the second session's vote shows its own courses, none drawn; a vote goes to the course shown; its draw
    /// runs; and the machine selected is sent as its clock runs out, as in the first.</summary>
    [Fact]
    public async Task ASecondSessionsMatchOfTheSameNameIsItsOwn()
    {
        await using var server = new LedServer();
        int[] first = [1, 3, 5, CourseVote.RandomCourse], second = [7, 8, 9, CourseVote.RandomCourse];
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            void AVisit(int[] options, int winner)
            {
                Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", options, 60_000));
                PumpTelling(state, () => lobby.VoteShown && lobby.CourseOptions.Count == 4);
                Assert.Equal(options.Select(c => c == CourseVote.RandomCourse ? "Random course" : Courses.Name(c)), lobby.CourseOptions.Select(o => o.Name));
                Assert.DoesNotContain(lobby.CourseOptions, o => o.IsWon || o.IsLit);
                Assert.False(lobby.DrawFinished);
                // a vote, with the mouse: for the course the card shows
                var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
                Pump(() => view.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is CourseOption) == 4);
                Click(window, view.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is CourseOption { Index: 2 }));
                Pump(() => server.Of<VoteCourse>().Count == 1);
                Assert.Equal(("m1", 2), (server.Of<VoteCourse>()[0].MatchId, server.Of<VoteCourse>()[0].Option));
                int[] votes = [.. options.Select((_, i) => i == winner ? 1 : 0)];
                Send(server, new CourseChosen("m1", options, votes, winner, options[winner]));
                PumpTelling(state, () => lobby.DrawFinished, "the draw did not run");
                Assert.Equal(winner, lobby.CourseOptions.Single(o => o.IsWon).Index);
                Send(server, new ChooseMachine("m1", options[winner], All, 4000), LedServer.Lobby(LobbyState.Choosing));
                PumpTelling(state, () => server.Of<MachineChoice>().Count == 1, "the machine selected was not sent as the clock ran out");
            }
            AVisit(first, 0);

            // the server is stopped and started again; the launcher stays open, and RACE is pressed again
            state.LeaveAsync().GetAwaiter().GetResult();
            PumpTelling(state, () => !lobby.VoteShown && lobby.CourseOptions.Count == 0 && !lobby.ChoiceShown);
            Join(server, state, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming));
            Assert.Equal(2, server.Connections);
            AVisit(second, 1);
        });
    }

    /// <summary>The draw's sounds (<see cref="DrawSounds"/>): the blip on every step of the frame, a step a vote
    /// (Overwatch's way: the card with two votes is two steps, its pips lit in turn), at the steps' own pace, and the
    /// chime once, when it stops on the course drawn; through the launcher's sounds (not muted here).</summary>
    [Fact]
    public async Task TheDrawBlipsOnEveryStepAVoteAndChimesOnTheCourseDrawn()
    {
        await using var server = new LedServer();
        int[] options = [1, 3, 5, 7, 8, CourseVote.RandomCourse];
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var time = new DrawTime();   // every step of the draw, however slow the PC (DrawTime)
            lobby.DrawClock = time;
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", options, 60_000));
            PumpTelling(state, () => lobby.VoteShown && lobby.CourseOptions.Count == options.Length);
            var heard = new List<LauncherSound>();
            void Hear(LauncherSound sound, int volume)
            {
                if (sound is LauncherSound.DrawHop or LauncherSound.CourseDrawn)
                {
                    heard.Add(sound);
                }
            }
            var hops = 0;      // cards the frame hopped to
            var steps = 0;     // votes it stepped on: a pip lit that was not
            var twice = false; // a step from the first vote of a card to its second
            (int Option, int Vote)? on = null;
            void Lit(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(CourseOption.IsLit) && sender is CourseOption { IsLit: true })
                {
                    hops++;
                }
                if (e.PropertyName == nameof(CourseOption.Pips) && sender is CourseOption o
                    && o.Pips.Select((p, k) => (p, k)).FirstOrDefault(x => x.p.Lit) is { p: not null } lit)
                {
                    twice |= on == (o.Index, lit.k - 1);
                    on = (o.Index, lit.k);
                    steps++;
                }
            }
            foreach (var o in lobby.CourseOptions)
            {
                o.PropertyChanged += Lit;
            }
            Attention.Sounded += Hear;
            try
            {
                Send(server, new CourseChosen("m1", options, [2, 1, 0, 1, 1, 1], 0, options[0]));
                time.Run(state, () => lobby.DrawFinished);
                Assert.True(hops > 5, $"the frame hopped {hops} times");
                Assert.True(steps > hops, $"the frame stepped {steps} times on {hops} cards");
                Assert.True(twice, "the frame never stepped from a card's first vote to its second");
                Assert.Equal([.. Enumerable.Repeat(LauncherSound.DrawHop, steps), LauncherSound.CourseDrawn], heard);
            }
            finally
            {
                Attention.Sounded -= Hear;
            }
        });
    }

    /// <summary>The machine picked in the Garage is the player's, in a lobby or out of one: kept, shown in the left
    /// column, queued with. On a server that does not allow it, the nearest allowed one is queued with and shown,
    /// and the player's own is kept; when the connection ends, the Garage has every machine again, the setting is
    /// the player's to move, and their machine is back.</summary>
    [Fact]
    public async Task TheMachinePickedIsThePlayersEverywhereAndAServersListGoesWithItsConnection()
    {
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(machines: [3, 9], setting: SettingRange.Only(100)), LedServer.Lobby(LobbyState.Forming, machine: 9), (window, main, state) =>
        {
            // queued with the nearest machine this server allows, and that is the one shown; the player's own is kept
            Assert.Equal(9, server.Of<EnterQuickPlay>()[0].Machine);
            var garage = main.Garage;
            Pump(() => garage.Choices.Count == 2);
            Assert.Equal((9, 9, Machines.Names[9], 12), (garage.MachineIndex, main.ShownMachine, main.Account.Machine, state.Settings.Racer.Machine));
            Assert.True(garage.SettingFixed);

            // the connection ends (the server stopped): every machine again, the setting free, the player's machine back
            server.Drop();
            PumpTelling(state, () => garage.Choices.Count == 41 && !garage.SettingFixed, "the Garage kept the gone server's machines");
            Assert.Equal((12, 12, Machines.Names[12]), (garage.MachineIndex, main.ShownMachine, main.Account.Machine));
        }, before: (window, main, state) =>
        {
            // out of any lobby, in the Garage tab: a machine pressed and used is the player's machine (only pressed, the
            // left column keeps the one raced)
            main.Current = main.Garage;
            Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<GarageView>().Single();
            Assert.Equal((6, 6, "Blue Falcon"), (state.Settings.Racer.Machine, main.ShownMachine, main.Account.Machine));
            ClickCell(window, view, 12);
            Assert.Equal((6, 6, "Blue Falcon"), (state.Settings.Racer.Machine, main.ShownMachine, main.Account.Machine));
            Click(window, Named<Button>(view, "Use"));
            Assert.Equal((12, 12, Machines.Names[12]), (state.Settings.Racer.Machine, main.ShownMachine, main.Account.Machine));
        });
    }

    /// <summary>A server whose machine setting is a range: the Garage's slider has its ends
    /// and the range beside it, the player's own setting is shown and queued brought into it and kept as theirs; the
    /// operator's change to one setting fixes the slider; with the connection gone the slider is 0 to 100 again.</summary>
    [Fact]
    public async Task ARangeLimitsTheGaragesSliderAndAChangeOfItFollows()
    {
        await using var server = new LedServer();
        var range = new SettingRange(80, 100);
        await InALobby(server, LedServer.Welcome(setting: range), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var garage = main.Garage;
            Assert.Equal(80, server.Of<EnterQuickPlay>()[0].Setting);                 // the player's 50, brought in
            main.Current = main.Garage;
            Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<GarageView>().Single();
            garage.PreviewMachineCommand.Execute(garage.MachineIndex);                // (the setting is PREVIEW's)
            PumpFor(100, state);
            var slider = Named<Slider>(view, "Setting");
            var limit = Named<TextBlock>(view, "SettingLimit");
            Assert.Equal((80.0, 100.0, 80.0, true), (slider.Minimum, slider.Maximum, slider.Value, slider.IsEnabled));
            Assert.Equal((true, "80-100", 50), (limit.IsEffectivelyVisible, limit.Text, garage.Setting));

            // the operator makes it one setting: fixed, the player's own still kept
            Send(server, new RulesChanged(LedServer.Welcome().Rules! with { MachineSetting = SettingRange.Only(90) }));
            PumpTelling(state, () => garage.SettingFixed, "the changed rule did not reach the Garage");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((90.0, false, "Fixed", 50), (slider.Value, slider.IsEnabled, limit.Text, garage.Setting));

            server.Drop();
            PumpTelling(state, () => !garage.SettingLimited, "the gone server's range stayed");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((0.0, 100.0, 50.0, true, false), (slider.Minimum, slider.Maximum, slider.Value, slider.IsEnabled, limit.IsEffectivelyVisible));
        }, before: (window, main, state) =>
        {
            state.Update(s => s with { Racer = s.Racer with { Setting = 50 } });
            main.Garage.Setting = 50;
        });
    }
}
