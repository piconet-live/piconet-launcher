using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;
using RaceView = Launcher.App.Views.NextUi.RaceView;

namespace Launcher.Tests;

/// <summary>
/// The redesigned interface's Quick Play ("Experimental UI" on), in the launcher's own window against a server the
/// test leads message by message (<see cref="LedServer"/>): a page a phase, from the queue through the gathering, the
/// course vote and its draw, the machine stage, the start and the race to its result, each doing what the classic
/// page does (the same votes and choices reach the server); the lobby's panel and the race's racers open with Y and
/// close with B; the whole Garage opens from the machine stage and Back closes it without leaving the lobby. On the
/// stand-in service: the Quick Play page out of a lobby lists the lobbies and RACE joins. With <c>NEXT_SHOTS</c>
/// naming a folder, the pages there as PNGs (1280 x 720).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextQuickPlayTests : IDisposable
{
    private static readonly int[] Options = [1, 16, 26, 8, 5, CourseVote.RandomCourse];

    private static readonly string[] Grades = ["A", "B", "C", "D", "E", "", "S"];

    private static readonly double[] Shares = [1, 0.5, 0, -1, 2, double.NaN];

    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(bool standIn = false)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data, standIn: standIn);
    }

    private static void Send(LedServer server, params ServerMessage[] messages) => server.SendAsync(messages).GetAwaiter().GetResult();

    private static RacerLive Racer(int seat, string state, float distance, int total = 60_000) =>
        new(seat, seat, 2, 20_000, total, 19_000, 900, 80, 100, 0, 1000, state, 0, 0, 0, distance);

    [Fact]
    public async Task APageAPhaseFromTheQueueToTheResult()
    {
        await using var server = new LedServer();
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            var lobby = main.Lobby;
            var joining = state.JoinAsync(server.Address);
            Pump(() => server.Connections == 1 && server.Of<Hello>().Count == 1, "the launcher did not connect");
            Send(server, LedServer.Welcome());
            Pump(() => server.Of<EnterQuickPlay>().Count == 1, "the launcher did not queue");

            // the queue: that a race is being found, and the way out (Back says what it leaves)
            main.Current = lobby;
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Finding && main.Shell.BackText == "Leave the queue");
            var finding = Page<FindingView>(window);
            Assert.Equal("FINDING A RACE", Named<TextBlock>(finding, "Title").Text);
            Assert.True(Said(finding, "Leave the queue").IsEffectivelyVisible);
            Assert.Equal(["Leave the queue"], Hints(main));
            Shot(window, state, "next-finding");

            // the gathering: a tile a racer, the vote to start with how far it is
            Send(server, LedServer.Lobby(LobbyState.Forming));
            PumpTelling(state, () => joining.IsCompleted && lobby.Phase == LobbyPhase.Gather);
            var gather = Page<GatherView>(window);
            Assert.Empty(shell.GetVisualDescendants().OfType<FindingView>());
            Assert.Equal(["ANA", "BEN"], lobby.People.Select(p => p.Name));
            Assert.Equal(2, Named<ItemsControl>(gather, "People").GetVisualDescendants().OfType<Chamfer>().Count());
            Assert.Equal("Start votes 0 of 2", lobby.StartLine);
            Assert.Equal("RACERS 2 OF 30", Named<TextBlock>(gather, "Count").Text);
            Assert.Equal(("Leave", true), (main.Shell.BackText, main.Shell.BackLeaves));
            Assert.Equal(["Select", "Lobby", "Leave"], Hints(main));
            Click(window, Said(gather, "Vote to start"));
            Pump(() => server.Of<VoteStart>() is [{ Yes: true }], "the vote to start was not sent");
            Send(server, LedServer.Lobby(LobbyState.Forming) with { StartVotes = 1 });
            PumpTelling(state, () => lobby.StartLine == "Start votes 1 of 2");
            Assert.Equal(0.5, lobby.StartShare);
            Shot(window, state, "next-gather");

            // Y: the lobby's panel (its rules, its racers), closed by B; its "Leave lobby" asks, and Stay stays
            Press(window, Key.Y, PhysicalKey.Y);
            Pump(() => main.Shell.Sheet is LobbySheetViewModel, "Y did not open the lobby's panel");
            var sheet = Page<LobbySheetView>(window);
            Assert.Equal(["ANA", "BEN"], Named<ItemsControl>(sheet, "Racers").GetVisualDescendants().OfType<NameTagText>().Select(n => n.PlayerName));
            Assert.StartsWith("3 laps", Named<TextBlock>(sheet, "Rules").Text, StringComparison.Ordinal);
            Assert.Equal(["Select", "Close"], Hints(main));
            Shot(window, state, "next-lobby-panel");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(main.Shell.Sheet);
            Assert.Same(lobby, main.Current);
            Press(window, Key.Y, PhysicalKey.Y);
            Click(window, Said(Page<LobbySheetView>(window), "Leave lobby"));
            Assert.Null(main.Shell.Sheet);
            Pump(() => main.Shell.AskingConfirm, "Leave lobby did not ask");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.NotNull(state.Session?.Lobby);

            // the course vote: the courses as cards in one row, a press votes; the clock runs
            Send(server, new CourseVote("m1", Options, 30_000), LedServer.Lobby(LobbyState.Starting));
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Vote && lobby.CourseOptions.Count == 6);
            var vote = Page<VoteView>(window);
            Assert.Equal("VOTE FOR THE NEXT COURSE", Named<TextBlock>(vote, "Title").Text);
            var cards = vote.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("vote")).ToList();
            Assert.Equal(6, cards.Count);
            Assert.Single(cards.Select(c => Math.Round(c.TranslatePoint(default, window)!.Value.Y)).Distinct());   // one row
            Assert.True(lobby.VoteClockShown);
            Assert.InRange(lobby.ClockShare, 0.8, 1);
            Assert.Equal(["Vote", "Lobby", "Leave"], Hints(main));
            Click(window, cards[1]);
            Pump(() => server.Of<VoteCourse>() is [{ Option: 1 }], "the course vote was not sent");
            Shot(window, state, "next-vote");

            // the draw, and the course it lands on in the title
            Send(server, new CourseChosen("m1", Options, [0, 2, 1, 0, 0, 0], 1, Options[1]));
            PumpTelling(state, () => lobby.VoteTitle == "Drawing the course" || lobby.DrawFinished);
            PumpTelling(state, () => lobby.DrawFinished, "the draw did not land", 15);
            Assert.Equal(lobby.NextTrack, lobby.VoteTitle);
            Assert.False(lobby.VoteClockShown);
            Assert.Contains("won", cards[1].Classes);
            Assert.All(cards.Where((_, i) => i != 1), c => Assert.Contains("lost", c.Classes));
            Shot(window, state, "next-vote-drawn");

            // the machine stage: the machine selected, a strip of the machines offered, Ready
            Send(server, new ChooseMachine("m1", Options[1], [.. Enumerable.Range(0, 41)], 30_000), LedServer.Lobby(LobbyState.Choosing));
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Machine);
            var machine = Page<MachineView>(window);
            Assert.Equal("PICK YOUR MACHINE", Named<TextBlock>(machine, "Title").Text);
            Assert.Equal(["Select", "Ready", "Lobby", "Leave"], Hints(main));
            var tiles = Named<ItemsControl>(machine, "Machines").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(41, tiles.Count);
            Click(window, tiles[0]);
            Pump(() => lobby.Garage.MachineIndex == 0, "the tile did not pick its machine");
            Assert.Equal(0, state.Settings.Racer.Machine);
            Assert.Empty(server.Of<MachineChoice>());                    // (nothing is sent until Ready)
            Shot(window, state, "next-machine");
            // the whole Garage from the stage, and Back closes it without leaving the lobby
            Click(window, Said(machine, "Open the Garage"));
            Pump(() => lobby.FullGarage, "the Garage did not open");
            Assert.Single(shell.GetVisualDescendants().OfType<GaragePageView>(), g => g.IsEffectivelyVisible);
            Assert.Equal(("Back", false), (main.Shell.BackText, main.Shell.BackLeaves));
            Click(window, Named<Button>(shell, "Back"));
            Assert.False(lobby.FullGarage);
            Assert.False(main.Shell.AskingConfirm);
            machine = Page<MachineView>(window);
            // S is Start: Ready, with the machine selected
            Press(window, Key.S, PhysicalKey.S);
            Pump(() => server.Of<MachineChoice>() is [{ Machine: 0 }], "Ready sent no choice");
            Send(server, LedServer.Lobby(LobbyState.Choosing, machine: 0, chosen: true));
            PumpTelling(state, () => lobby.IsReady);
            Assert.True(Named<Border>(machine, "IsReady").IsEffectivelyVisible);
            Assert.False(Named<Button>(machine, "Ready").IsEffectivelyVisible);

            // the start: the course in large, what the lobby waits for
            Send(server, LedServer.Lobby(LobbyState.Choosing, machine: 0, chosen: true, waiting: LobbyState.WaitingForRelay, otherChosen: true));
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Starting);
            var starting = Page<StartingView>(window);
            Assert.Equal(lobby.NextTrack.ToUpperInvariant(), Named<TextBlock>(starting, "Course").Text);
            Assert.Equal("Starting race server", Named<TextBlock>(starting, "Line").Text);
            Assert.Equal(["Lobby", "Leave"], Hints(main));
            Shot(window, state, "next-starting");

            // the race, watched from the lobby (no game of this launcher runs): who leads, the first three, every racer in its panel
            SeatSetup Seat(int s, string? id, string name) => new(s, id, name, s + 2, new Rgb((byte)(40 * s), 200, 90));
            var seats = new[] { Seat(0, "p9", "MIRA"), Seat(1, "p2", "BEN"), Seat(2, null, "CPU"), Seat(3, null, "CPU") };
            Send(server, LedServer.Lobby(LobbyState.Racing) with { Race = new LobbyRace("m1", Options[1], 3, seats, DateTimeOffset.UtcNow) });
            Send(server, new LiveRace("m1", new RaceSnapshot(600, 0,
                [Racer(0, RacerLive.Racing, 900), Racer(1, RacerLive.Racing, 800), Racer(2, RacerLive.Racing, 700), Racer(3, RacerLive.Out, 100)])));
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Race && lobby.Race.Standings.Count == 4, "the race did not show", 15);
            var race = Page<RaceView>(window);
            Assert.Equal(3, lobby.Leaders.Count);
            Assert.Equal(lobby.Race.Standings[0].Name, lobby.LeaderName);
            Assert.Equal(lobby.LeaderName.ToUpperInvariant(), Named<TextBlock>(race, "Watched").Text);
            Assert.Equal(("Leave", true), (main.Shell.BackText, main.Shell.BackLeaves));   // (a watcher leaves the lobby, not a race of theirs)
            Assert.Equal(["Select", "All racers", "Leave"], Hints(main));
            Assert.False(Named<Chamfer>(race, "Map").IsEffectivelyVisible);
            Click(window, Said(race, "Whole course"));
            Assert.True(Named<Chamfer>(race, "Map").IsEffectivelyVisible);
            Shot(window, state, "next-race");
            Press(window, Key.Y, PhysicalKey.Y);
            Pump(() => main.Shell.Sheet is RacersSheetViewModel, "Y did not open the race's panel");
            var racers = Page<RacersSheetView>(window);
            Assert.Equal("ALL RACERS", Named<TextBlock>(racers, "Title").Text);
            Assert.Single(racers.GetVisualDescendants().OfType<StandingsTable>());
            Shot(window, state, "next-race-panel");
            Press(window, Key.Y, PhysicalKey.Y);                         // Y again closes it
            Assert.Null(main.Shell.Sheet);

            // its result: the same page, its parts where they were; the podium, and every result in the panel
            var leaders = Named<ItemsControl>(race, "Leaders").TranslatePoint(default, window);
            var headline = Named<TextBlock>(race, "Watched").TranslatePoint(default, window);
            var lap = Named<TextBlock>(race, "Lap").TranslatePoint(default, window);
            PumpFor(150, state);
            Send(server, new LiveRace("m1", new RaceSnapshot(900, 0,
                [Racer(0, RacerLive.Finished, 1000, 90_000), Racer(1, RacerLive.Finished, 1000, 91_500), Racer(2, RacerLive.Finished, 1000, 93_000), Racer(3, RacerLive.Out, 100)])));
            PumpTelling(state, () => lobby.Phase == LobbyPhase.Results, "no results came", 15);
            Assert.Same(race, Page<RaceView>(window));
            Assert.Equal(leaders, Named<ItemsControl>(race, "Leaders").TranslatePoint(default, window));
            Assert.Equal(headline, Named<TextBlock>(race, "Watched").TranslatePoint(default, window));
            Assert.Equal(lap, Named<TextBlock>(race, "Lap").TranslatePoint(default, window));
            Assert.Equal(["Select", "All results", "Leave"], Hints(main));
            Assert.True(main.Shell.HasBackdrop);                    // (the race stands on its course's picture)
            Assert.Equal(16, main.Shell.Backdrop);
            Click(window, Said(race, "All results"));
            Pump(() => main.Shell.Sheet is RacersSheetViewModel, "All results did not open the panel");
            Assert.Equal("ALL RESULTS", Named<TextBlock>(Page<RacersSheetView>(window), "Title").Text);
            Shot(window, state, "next-results");
        });
    }

    [Fact]
    public async Task TheQuickPlayPageListsTheLobbiesAndRaceJoins()
    {
        var state = State(standIn: true);
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            // Quick Play: the servers first (in dev mode the stand-in alone); its lobbies from the top bar's players online
            Click(window, Said(shell, "Quick Play"));
            var servers = Assert.IsType<ServersViewModel>(main.Current);
            Assert.Equal([StandInService.Name], servers.Rows.Select(r => r.Name));
            Assert.False(servers.Rows[0].Own);                       // (the stand-in cannot be removed)
            PumpTelling(state, () => servers.Rows[0].Ping.Length > 0, "the stand-in gave no ping", 15);
            Shot(window, state, "next-servers-standin");
            Click(window, Said(Page<ServersView>(window), StandInService.Name));
            Click(window, Said(Page<ServerSheetView>(window), "See its lobbies"));
            Assert.Same(main.Home, main.Current);
            var lobbies = Page<LobbiesView>(window);
            Assert.Empty(shell.GetVisualDescendants().OfType<HomeView>());
            Assert.Equal("LOBBIES", Named<TextBlock>(lobbies, "Title").Text);
            PumpTelling(state, () => main.Home.Lobbies.Count > 0, "the stand-in listed no lobby", 15);
            Dispatcher.UIThread.RunJobs();
            var rows = Named<ItemsControl>(lobbies, "Rows").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(main.Home.Lobbies.Select(l => l.Name), rows.Select(AutomationProperties.GetName));
            var go = Named<Button>(lobbies, "Race");
            Assert.Equal("RACE", go.Content);
            Shot(window, state, "next-lobbies");

            // RACE: Quick Play on the server, and the lobby's page (the queue, then the gathering)
            Click(window, go);
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Lobby.Phase == LobbyPhase.Gather, "RACE joined no lobby", 15);
            var gather = Page<GatherView>(window);
            Assert.Contains(main.Lobby.People, p => p.You);
            Assert.False(main.Shell.HasBackdrop);                    // (no course yet: the flag)
            // (before the vote to start: once it is in, the stand-in's lobby moves on to its course vote within
            // seconds) practice while the lobby waits: the Practice page, the seat kept (the lobby's pill), Back to the lobby
            Click(window, Said(gather, "Practice while you wait"));
            Assert.Same(main.Practice, main.Current);
            Assert.True(main.Shell.LobbyPillShown);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Same(main.Lobby, main.Current);
            // and from another page, Quick Play's entry leads back to the lobby
            Click(window, Named<StackPanel>(shell, "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Replays"));
            var pill = Named<Button>(shell, "LobbyPill");
            Pump(() => pill.IsEffectivelyVisible && pill.Bounds.Width > 0, "the lobby's pill did not show");
            Click(window, pill);
            Assert.Same(main.Lobby, main.Current);
            // the launcher's name (the main menu) from the lobby: asked first; Stay keeps the lobby's page, the main
            // menu keeps the seat (the lobby's pill brings the player back)
            Click(window, Named<Button>(shell, "Home"));
            Assert.True(main.Shell.AskingConfirm);
            Assert.Equal("Go to the main menu?", main.Shell.ConfirmTitle);
            Assert.Equal(HubViewModel.LobbyLine(LobbyPhase.Gather, gameRunning: false), main.Shell.ConfirmLine);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.Same(main.Lobby, main.Current);
            Click(window, Named<Button>(shell, "Home"));
            Click(window, Named<Button>(shell, "ConfirmDo"));
            Assert.IsType<HubViewModel>(main.Current);
            Assert.True(main.Shell.LobbyPillShown);
            Assert.NotNull(state.Session?.Lobby);
            Click(window, Named<Button>(shell, "LobbyPill"));
            Assert.Same(main.Lobby, main.Current);
            // the vote to start: the player's own tile says they want to start
            Assert.DoesNotContain(gather.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Wants" && t.IsEffectivelyVisible);
            Click(window, Named<Button>(gather, "VoteStart"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.Lobby.YouWantToStart);
            Assert.Single(gather.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Wants" && t.IsEffectivelyVisible);
            Shot(window, state, "next-gather-standin");
        });
    }

    /// <summary>What the question before the main menu says of a lobby in each phase.</summary>
    [Fact]
    public void TheMainMenuSaysWhatALobbyGoesOnDoing()
    {
        Assert.StartsWith("You stay in the queue.", HubViewModel.LobbyLine(LobbyPhase.Finding, false), StringComparison.Ordinal);
        Assert.Contains("the vote goes on without you", HubViewModel.LobbyLine(LobbyPhase.Vote, false), StringComparison.Ordinal);
        Assert.Contains("the machine choice goes on without you", HubViewModel.LobbyLine(LobbyPhase.Machine, false), StringComparison.Ordinal);
        Assert.StartsWith("Your race goes on in the game.", HubViewModel.LobbyLine(LobbyPhase.Race, true), StringComparison.Ordinal);
        Assert.StartsWith("Your race goes on in the game.", HubViewModel.LobbyLine(LobbyPhase.Starting, true), StringComparison.Ordinal);
        Assert.Contains("while its race runs", HubViewModel.LobbyLine(LobbyPhase.Race, false), StringComparison.Ordinal);
        Assert.All(new[] { LobbyPhase.Gather, LobbyPhase.Room, LobbyPhase.Results },
            phase => Assert.Equal("You stay in the lobby. The lobby button at the top brings you back.", HubViewModel.LobbyLine(phase, false)));
    }

    /// <summary>The top bar's count of the players online: its words and its tip.</summary>
    [Fact]
    public void ThePlayersOnlineInWords()
    {
        Assert.Equal("–", Launcher.App.Controls.NextUi.OnlineCount.Words(-1));
        Assert.Equal("0", Launcher.App.Controls.NextUi.OnlineCount.Words(0));
        Assert.Equal("1,284", Launcher.App.Controls.NextUi.OnlineCount.Words(1284));
        Assert.Equal("1 player online on Quick.", HubViewModel.OnlineText(1, 0, "Quick"));
        Assert.Equal("12 players online · 4 racing now.", HubViewModel.OnlineText(12, 4, " "));
    }

    /// <summary>The words and shares the pages show, by themselves.</summary>
    [Theory]
    [InlineData("Lap 2/3", "Lap 2 / 3", 2 / 3.0)]
    [InlineData("Lap 1/5", "Lap 1 / 5", 0.2)]
    [InlineData("Finished", "Finished", 1)]
    [InlineData("", "", 0)]
    [InlineData("Lap x/3", "Lap x/3", 0)]
    public void ALapInWordsAndAsAShare(string text, string words, double share)
    {
        var (said, part) = LobbyViewModel.LapOf(text);
        Assert.Equal(words, said);
        Assert.Equal(share, part, 6);
    }

    [Fact]
    public void AGradeLightsItsPartsAndAPhaseHasItsPage()
    {
        Assert.Equal([5, 4, 3, 2, 1, 0, 0], Grades.Select(GradeParts.Worth));
        Assert.Equal([true, true, true, false, false], Assert.IsType<List<bool>>(GradeParts.Instance.Convert("C", typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)));
        // the race and its result are one page; the machine stage's whole Garage is another than its own
        Assert.Equal(QuickPlayView.PageOf(LobbyPhase.Race, false), QuickPlayView.PageOf(LobbyPhase.Results, false));
        Assert.NotEqual(QuickPlayView.PageOf(LobbyPhase.Machine, false), QuickPlayView.PageOf(LobbyPhase.Machine, true));
        Assert.Equal(8, Enum.GetValues<LobbyPhase>().Select(p => QuickPlayView.PageOf(p, false)).Distinct().Count() + 1);
        // the clock's ring: all of it with all the time left, none at its end, and never more or less
        Assert.Equal([360, 180, 0, 0, 360, 0], Shares.Select(ClockRing.Sweep));
        // a panel at the window's edge keeps its bottom right corner
        Assert.Equal(5, Chamfer.Outline(new Rect(0, 0, 100, 50), 10, oneCut: true).Count);
    }
}
