using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;
using TestSupport;
using static Launcher.Tests.Headless;
using LobbyView = Launcher.App.Views.LobbyView;

namespace Launcher.Tests;

public sealed partial class LobbyStageRulesTests
{
    /// <summary>The protocol's rotation of today's words (protocol/tests/vectors/quickplay/: a three-lap normal
    /// mode, Overdrive the spicy one on Loop Cross).</summary>
    private static QuickPlayRotation Rotation() =>
        QuickPlayRotation.Read(RepositoryFolder.Protocol("protocol", "tests", "vectors", "quickplay"));

    private static void Shot(Window window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(400, state);   // (the transitions eased in)
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>The spicy card's tip (<see cref="ToolTip.GetTip"/>) is its line and rules; with LOBBY_SHOTS it is drawn in a
    /// window of its own (no system windows here for its popup) as NAME.png, and the card's lower left corner kept in
    /// NAME.at, for a picture of the two together.</summary>
    private static void TipShot(Window window, LobbyView view, AppState state, int index, string? about, string name)
    {
        var card = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("votecard")).ElementAt(index);
        Assert.Equal(about, ToolTip.GetTip(card));
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        var tipWindow = new Window { Content = new ToolTip { Content = about }, SizeToContent = SizeToContent.WidthAndHeight };
        tipWindow.Show();
        PumpFor(200, state);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        tipWindow.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        tipWindow.Close();
        var at = Avalonia.VisualExtensions.TranslatePoint(card, new Avalonia.Point(0, card.Bounds.Height), window)!.Value;
        File.WriteAllText(Path.Combine(folder, name + ".at"), FormattableString.Invariant($"{at.X:0} {at.Y:0}"));
    }

    /// <summary>On every card that shows its mark (no picture) the spicy badge and the mark never overlap, whatever
    /// the card's width: the mark keeps to the room right of the badge, cut where it is too long.</summary>
    private static void BadgesClearOfMarks(Window window, LobbyView view)
    {
        var checkedOne = false;
        foreach (var card in view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("votecard")))
        {
            var badge = card.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Classes.Contains("modebadge") && b.Parent is Canvas && b.IsEffectivelyVisible);
            var mark = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Mark");
            if (badge is null || !mark.IsEffectivelyVisible)
            {
                continue;
            }
            Rect On(Control c) => new(Avalonia.VisualExtensions.TranslatePoint(c, default, window)!.Value, c.Bounds.Size);
            Assert.False(On(badge).Intersects(On(mark)), $"the badge {On(badge)} overlaps the mark {On(mark)} ({mark.Text})");
            checkedOne = true;
        }
        Assert.True(checkedOne, "no card showed a badge beside its mark");
    }

    /// <summary>The player's game left a race of a mode without CPUs: Watch stands where Rejoin would, the lobby's own
    /// Watch gives way, and a rejoin asked for anyway says plainly why not. LOBBY_SHOTS keeps both.</summary>
    [Fact]
    public async Task AModeWithoutCpusShowsWatchWhereRejoinWouldBe()
    {
        await using var server = new LedServer();
        var seat = new SeatSetup(0, "p1", "ANA", 6, new Rgb(1, 2, 3));
        await InALobby(server, LedServer.Welcome() with { Spectators = true }, LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var session = state.Session!;
            // (no game runs here, so no game can leave: what RaceOverAsync keeps, RejoinTests has that step whole)
            var start = new MatchStart("m1", new RelayEndpoint("r1", "", 47811), "1f2e3d4c5b6a7988", 0, new RaceSetup(21, 3, [seat], 1, 2), NoRejoin: true);
            typeof(Launcher.Core.QuickPlaySession).GetProperty(nameof(session.LeftForGood))!.SetValue(session, new Launcher.Core.ForkRace("", null, start));
            Send(server, LedServer.Lobby(LobbyState.Racing) with { Race = new LobbyRace("m1", 21, 3, [seat], DateTimeOffset.UtcNow) });
            PumpTelling(state, () => main.Lobby.CanWatchLeftRace, "Watch was not offered");
            Assert.False(main.Lobby.CanRejoin);
            Assert.False(main.Lobby.CanWatch);
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            Assert.True(Named<Button>(view, "WatchLeftRace").IsEffectivelyVisible);
            Assert.False(Named<Button>(view, "Rejoin").IsEffectivelyVisible);
            Shot(window, state, "no-rejoin-watch");

            state.Rejoin();
            PumpTelling(state, () => main.Lobby.Notice == "Can't rejoin", "the refusal was not said");
            Shot(window, state, "no-rejoin-refused");
        });
    }

    /// <summary>Quick Play's vote of game modes (<see cref="CourseVote.Cards"/>): each card says its laps beside its
    /// venue, the spicy card its mode's badge (and its line when hovered), "Random course" nothing; the draw's card
    /// (<see cref="CourseChosen.Card"/>) is what the course drawn races. LOBBY_SHOTS keeps the vote and the draw.</summary>
    [Fact]
    public async Task TheVoteOfModesShowsLapsAndTheSpicyBadge()
    {
        var vote = Rotation().Vote(CoursePool.Default, new Random(4).Next);
        int[] options = [.. vote.Select(o => o.Course)];
        var spicy = vote.ToList().FindIndex(o => o.Spicy);
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", options, 60_000, [.. vote.Select(o => o.Card)]));
            PumpTelling(state, () => lobby.VoteShown && lobby.CourseOptions.Count == 6);
            var cards = lobby.CourseOptions;
            Assert.Equal("OVERDRIVE", cards[spicy].Badge);
            Assert.StartsWith("Speed locked and rising. Last one alive wins.\n", cards[spicy].About, StringComparison.Ordinal);
            Assert.Contains("Pedals locked", cards[spicy].About, StringComparison.Ordinal);
            Assert.Equal(cards[spicy].Place, cards[spicy].PlaceLine);     // unlimited laps: nothing said
            Assert.All(cards.Where(c => c.Index != spicy && c.Course != CourseVote.RandomCourse), c =>
            {
                Assert.False(c.HasBadge);
                Assert.Null(c.About);
                Assert.Equal(c.Place + " · 3 laps", c.PlaceLine);
            });
            Assert.Equal("Any venue", cards[^1].PlaceLine);                 // Random: no laps before the draw
            Assert.DoesNotContain(cards, c => c.PlaceLine.Contains("normally", StringComparison.OrdinalIgnoreCase));
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            var badges = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("modebadge") && b.Parent is Canvas).ToList();
            Assert.Equal(6, badges.Count);
            var shown = Assert.Single(badges, b => b.IsEffectivelyVisible);
            Assert.Equal("OVERDRIVE", ((TextBlock)shown.Child!).Text);
            Assert.True(shown.Bounds.Width > 40, $"the badge was {shown.Bounds.Width} px wide");
            Shot(window, state, "mode-vote");

            // held or hovered, the spicy card lists only what it changes (design 6): its line, then its rules
            TipShot(window, view, state, spicy, cards[spicy].About, "mode-vote-hover-tip");

            // the vote's step is Vote, and no race's line stands under the steps while it runs
            Assert.Equal(["Gathering", "Vote", "Machines", "Starting", "Race"], lobby.Steps.Select(s => s.Name));
            // (its room is kept, so that nothing moves when it eases in)
            Assert.Equal((Launcher.Core.LobbyStep.Vote, false), (lobby.Step, lobby.ModeLineShown));
            Assert.DoesNotContain("on", Named<StackPanel>(view, "ModeLine").Classes);

            // Random drew the spicy option: its card turns into it, badge and all; the draw is still the Vote step,
            // and the race's line waits for it to land (it would tell the draw's end)
            Send(server, new CourseChosen("m1", options, [0, 0, 0, 0, 0, 2], 5, 8, vote[spicy].Card));
            PumpTelling(state, () => !double.IsNaN(lobby.DrawPointer), "the draw did not start");
            Assert.Equal((Launcher.Core.LobbyStep.Vote, null), (lobby.Step, lobby.ModeLine));
            Shot(window, state, "mode-vote-drawing");
            PumpTelling(state, () => lobby.DrawFinished, "the draw did not run");
            var won = Assert.Single(cards, c => c.IsWon);
            Assert.Equal((8, "OVERDRIVE"), (won.Course, won.Badge));
            PumpFor(600, state);   // (the drawn card widened, the others narrowed)
            BadgesClearOfMarks(window, view);
            // drawn: the mode, the course and (unlimited) no laps, under the steps; pointed at, the card's own tip
            Assert.Equal("OVERDRIVE · " + Launcher.Core.Courses.Name(8), lobby.ModeLine?.Text);
            Assert.Equal(cards[spicy].About, lobby.ModeLine!.Tip);
            Assert.Equal(lobby.ModeLine.Tip, ToolTip.GetTip(Named<StackPanel>(view, "ModeLine")));
            Assert.True(Named<StackPanel>(view, "ModeLine").IsEffectivelyVisible);
            Assert.Contains("on", Named<StackPanel>(view, "ModeLine").Classes);
            Assert.Equal("OVERDRIVE", Named<TextBlock>(view, "ModeTitle").Text);
            Assert.DoesNotContain("plain", Named<Border>(view, "ModeBadge").Classes);
            Shot(window, state, "mode-vote-drawn");

            // kept through the machines, the start and the race
            var seat = new SeatSetup(0, "p1", "ANA", 6, new Rgb(1, 2, 3));
            Send(server, LedServer.Lobby(LobbyState.Choosing), new ChooseMachine("m1", 8, [.. Enumerable.Range(0, 41)], 30_000));
            PumpTelling(state, () => lobby.Step == Launcher.Core.LobbyStep.Machines && lobby.CourseShown, "the machine stage did not open");
            Assert.Equal("OVERDRIVE · " + Launcher.Core.Courses.Name(8), lobby.ModeLine?.Text);
            Shot(window, state, "mode-line-machines");
            Send(server, LedServer.Lobby(LobbyState.Choosing, waiting: LobbyState.WaitingForRelay));
            PumpTelling(state, () => lobby.Step == Launcher.Core.LobbyStep.Starting, "the start did not come");
            Assert.Equal("OVERDRIVE · " + Launcher.Core.Courses.Name(8), lobby.ModeLine?.Text);
            Shot(window, state, "mode-line-starting");
            Send(server, LedServer.Lobby(LobbyState.Racing) with { Race = new LobbyRace("m1", 8, 0, [seat], DateTimeOffset.UtcNow) });
            PumpTelling(state, () => lobby.Racing, "the race did not show");
            Assert.Equal("OVERDRIVE · " + Launcher.Core.Courses.Name(8), lobby.ModeLine?.Text);
            Assert.True(Named<StackPanel>(view, "ModeLine").IsEffectivelyVisible);
            Shot(window, state, "mode-line-race");

            // the next gathering: no line
            Send(server, LedServer.Lobby(LobbyState.Forming));
            PumpTelling(state, () => lobby.Gathering, "the lobby did not gather again");
            Assert.Null(lobby.ModeLine);
        });
    }

    /// <summary>A custom lobby on a fixed course has no vote: from the machine stage on, the race's line names its mode
    /// (by its badge), the course and the mode's laps; pointed at, the mode's line and the lobby's rules that differ
    /// from Quick Play's. LOBBY_SHOTS keeps it.</summary>
    [Fact]
    public async Task AFixedCourseLobbyNamesItsModeUnderTheSteps()
    {
        var mode = new LobbyMode(new string('a', 64), "DR-0001", "Death Race", 1, "DEATH RACE", "Three lives each; the last wreck is final.",
            new Dictionary<string, int> { ["laps"] = 2 });
        var rules = new LobbyRules(new Dictionary<string, int>
        {
            ["racers"] = 10, [HostRules.CourseSelect.Name] = (int)CourseSelectMode.Fixed, [LobbyRules.FixedCourseKey] = 16,
        }, Mode: mode);
        LobbyState Custom(string stage, string? waiting = null) =>
            LedServer.Lobby(stage, waiting: waiting) with { Custom = true, HostId = "p2", Rules = rules, Name = "BEN's lobby" };
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), Custom(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            PumpTelling(state, () => lobby.CustomBarShown, "the custom lobby's bar did not show");
            Assert.Null(lobby.ModeLine);
            Send(server, Custom(LobbyState.Starting));
            PumpTelling(state, () => lobby.Starting);
            Send(server, Custom(LobbyState.Choosing), new ChooseMachine("m1", 16, [.. Enumerable.Range(0, 41)], 30_000));
            PumpTelling(state, () => lobby.Step == Launcher.Core.LobbyStep.Machines && lobby.CourseShown, "the machine stage did not open");
            Assert.Equal(["Gathering", "Machines", "Starting", "Race"], lobby.Steps.Select(s => s.Name));
            Assert.Equal("DEATH RACE · " + Launcher.Core.Courses.Name(16) + " · 2 laps", lobby.ModeLine?.Text);
            Assert.Equal("Three lives each; the last wreck is final.\n10 racers", lobby.ModeLine!.Tip);
            Assert.Equal("2 laps", lobby.LapsText);   // (the machine stage's band says the same laps)
            Assert.True(Named<StackPanel>(view, "ModeLine").IsEffectivelyVisible);
            Shot(window, state, "mode-line-fixed-course");
        });
    }

    /// <summary>Quick Play's own rotation (protocol/quickplay: the competitive lap table; Knockout, Ghost race,
    /// Overdrive, Death Race and Crown the spicy options, two of them a vote): each normal card says its course's laps; a spicy card its badge,
    /// Ghost race its course's laps and the others none (they race unlimited), whichever two are drawn.
    /// LOBBY_SHOTS keeps it, to set beside the vote's mockup (design 6).</summary>
    [Fact]
    public async Task QuickPlaysOwnRotationShowsEachCoursesLaps()
    {
        var rotation = QuickPlayRotation.Read(RepositoryFolder.Protocol("protocol", "quickplay"));
        Assert.Equal(["CROWN", "DEATH RACE", "GHOST RACE", "KNOCKOUT", "OVERDRIVE"], rotation.Spicy.Select(s => s.Mode.Package.Badge).Order(StringComparer.Ordinal));
        var vote = rotation.Vote(CoursePool.Default, new Random(4).Next);
        int[] options = [.. vote.Select(o => o.Course)];
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            Send(server, LedServer.Lobby(LobbyState.Starting), new CourseVote("m1", options, 60_000, [.. vote.Select(o => o.Card)]));
            PumpTelling(state, () => lobby.VoteShown && lobby.CourseOptions.Count == 6);
            var cards = lobby.CourseOptions;
            var spicy = cards.Where(c => c.HasBadge).ToList();
            Assert.Equal(2, spicy.Count);
            Assert.Equal(2, spicy.Select(c => c.Badge).Distinct().Count());   // two different spicy options
            foreach (var card in spicy)
            {
                var mode = Assert.Single(rotation.Spicy, s => s.Mode.Package.Badge == card.Badge).Mode;
                if (card.Badge == "GHOST RACE")
                {
                    Assert.Matches(@" · \d+ laps?$", card.PlaceLine);   // Quick Play's laps on its course
                }
                else
                {
                    Assert.Equal(card.Place, card.PlaceLine);   // laps unlimited: none said
                }
                Assert.StartsWith(mode.Package.About, card.About, StringComparison.Ordinal);
                Assert.DoesNotContain("normally", card.About, StringComparison.OrdinalIgnoreCase);
            }
            Assert.All(cards.Where(c => !c.HasBadge && c.Course != CourseVote.RandomCourse), c =>
                Assert.Matches(@" · \d+ laps?$", c.PlaceLine));
            // the cards say the laps: the lobby's line says none of its own
            Assert.DoesNotContain("lap", lobby.RulesText, StringComparison.Ordinal);
            Shot(window, state, "mode-vote-quickplay");
            var view = window.GetVisualDescendants().OfType<LobbyView>().Single();
            TipShot(window, view, state, spicy[0].Index, spicy[0].About, "mode-vote-quickplay-tip");

            // a normal card drawn: the race's line says QUICK PLAY, its course and laps, and has nothing to list
            var normal = cards.First(c => !c.HasBadge && c.Course != CourseVote.RandomCourse);
            var votes = new int[options.Length];
            votes[normal.Index] = 1;
            Send(server, new CourseChosen("m1", options, votes, normal.Index, normal.Course, vote[normal.Index].Card));
            PumpTelling(state, () => lobby.DrawFinished, "the draw did not run");
            Assert.Equal("QUICK PLAY · " + Launcher.Core.Courses.Name(normal.Course) + " · " + vote[normal.Index].Card.Laps + " laps", lobby.ModeLine?.Text);
            Assert.Null(lobby.ModeLine!.Tip);
            Assert.Contains("plain", Named<Border>(view, "ModeBadge").Classes);
            PumpFor(600, state);   // (the spicy cards narrowed: a long story name beside its badge is cut, never under it)
            BadgesClearOfMarks(window, view);
            Shot(window, state, "mode-line-quickplay-drawn");
        });
    }

    /// <summary>A lobby whose modes allow no CPUs (<see cref="LobbyState.NoCpuVote"/>) has no CPU vote, and its open
    /// seats are not CPUs'; a member whose launcher cannot run the lobby's mode (<see cref="LobbyState.CannotRace"/>)
    /// is said on their seat, and to that player in the lobby's notice.</summary>
    [Fact]
    public async Task NoCpuVoteWithoutCpusAndWhoCannotRaceIsSaid()
    {
        var welcome = LedServer.Welcome() with
        {
            Rules = LedServer.Welcome().Rules! with { CpuFill = CpuFillMode.Vote },
        };
        await using var server = new LedServer();
        await InALobby(server, welcome, LedServer.Lobby(LobbyState.Forming), (window, main, state) =>
        {
            var lobby = main.Lobby;
            PumpTelling(state, () => lobby.CpuVoteShown, "the CPU vote was not shown at first");
            Send(server, LedServer.Lobby(LobbyState.Forming) with { NoCpuVote = true });
            PumpTelling(state, () => !lobby.CpuVoteShown, "the CPU vote stayed");
            Assert.DoesNotContain(lobby.SeatColumns.SelectMany(c => c.Seats), s => s.Open && s.Name == "CPU");

            Send(server, LedServer.Lobby(LobbyState.Forming) with { CannotRace = ["p2"] });
            PumpTelling(state, () => lobby.SeatColumns.SelectMany(c => c.Seats).Any(s => s.Name == "BEN" && s.Machine == LobbyViewModel.CannotRaceText));
            Assert.NotEqual("Can't race this mode", lobby.Notice);
            Send(server, LedServer.Lobby(LobbyState.Forming) with { CannotRace = ["p1"] });
            PumpTelling(state, () => lobby.Notice == "Can't race this mode", $"the notice said {lobby.Notice}");
            Shot(window, state, "mode-cannot-race");
        });
    }

    /// <summary>A custom lobby whose mode has CPU racers Always: no CPU vote (there is nothing to vote on), and its
    /// open seats say CPU. At Off: no vote, and the open seats are empty.</summary>
    [Fact]
    public async Task CpuRacersAlwaysHasNoVoteAndCpusOnTheOpenSeats()
    {
        var compiled = ModeCompiler.Compile(File.ReadAllText(TestSupport.RepositoryFolder.Protocol("protocol", "quickplay", "competitive.fzmode")));
        LobbyState Hosted(int cpus)
        {
            var mode = new RotationMode("competitive", compiled.Mode!, compiled.Hash!, [.. compiled.Mode!.Params.Select(p => p.Name == "cpu_racers" ? cpus : p.Default)]);
            return LedServer.Lobby(LobbyState.Forming) with
            {
                Custom = true, HostId = "p2", Rules = QuickPlayDefaults.Whole() with { Mode = mode.Named() }, NoCpuVote = !mode.CpuVote,
            };
        }
        await using var server = new LedServer();
        await InALobby(server, LedServer.Welcome(), Hosted(1), (window, main, state) =>
        {
            var lobby = main.Lobby;
            bool OpenCpus() => lobby.SeatColumns.SelectMany(c => c.Seats).Any(s => s.Open && s.Name == "CPU");
            PumpTelling(state, () => lobby.CpuVoteShown && OpenCpus(), "Vote: the CPU vote was not shown");
            Send(server, Hosted(2));
            PumpTelling(state, () => !lobby.CpuVoteShown, "Always: the CPU vote stayed");
            Assert.True(OpenCpus());
            Assert.Contains("CPU racers Always", lobby.RulesSummary, StringComparison.Ordinal);
            Send(server, Hosted(0));
            PumpTelling(state, () => !OpenCpus(), "Off: the open seats stayed CPUs'");
            Assert.False(lobby.CpuVoteShown);
            Assert.Contains("CPU racers Off", lobby.RulesSummary, StringComparison.Ordinal);
        });
    }
}
