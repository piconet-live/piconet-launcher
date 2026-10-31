using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
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
            var badges = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("modebadge")).ToList();
            Assert.Equal(6, badges.Count);
            var shown = Assert.Single(badges, b => b.IsEffectivelyVisible);
            Assert.Equal("OVERDRIVE", ((TextBlock)shown.Child!).Text);
            Assert.True(shown.Bounds.Width > 40, $"the badge was {shown.Bounds.Width} px wide");
            Shot(window, state, "mode-vote");

            // Random drew the spicy option: its card turns into it, badge and all
            Send(server, new CourseChosen("m1", options, [0, 0, 0, 0, 0, 2], 5, 8, vote[spicy].Card));
            PumpTelling(state, () => lobby.DrawFinished, "the draw did not run");
            var won = Assert.Single(cards, c => c.IsWon);
            Assert.Equal((8, "OVERDRIVE"), (won.Course, won.Badge));
            Shot(window, state, "mode-vote-drawn");
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
}
