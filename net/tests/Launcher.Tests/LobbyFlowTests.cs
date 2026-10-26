using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// The lobby's flow as the alpha's testers asked for it, the parts that need no window: the
/// draw shown as the weighted lottery it is (each option's chance, a strip whose parts are as wide as the chances, a
/// pointer that stops in the part drawn), the line of the lobby's steps, the start vote's count, and where the
/// Garage is open.
/// </summary>
public sealed class LobbyFlowTests
{
    [Fact]
    public void AnOptionsChanceIsItsShareOfTheVotesAndAllAreAlikeWhenNobodyVoted()
    {
        Assert.Equal([0, 0.75, 0, 0.25, 0, 0], CourseDraw.Chances([0, 3, 0, 1, 0, 0]));
        Assert.Equal(["0%", "75%", "0%", "25%", "0%", "0%"], CourseDraw.Chances([0, 3, 0, 1, 0, 0]).Select(CourseDraw.Percent));
        Assert.All(CourseDraw.Chances([0, 0, 0, 0, 0, 0]), c => Assert.Equal(1 / 6.0, c, 12));
        Assert.Equal("17%", CourseDraw.Percent(1 / 6.0));
        Assert.Equal(["33%", "67%"], CourseDraw.Chances([1, 2]).Select(CourseDraw.Percent));
        Assert.Empty(CourseDraw.Chances([]));
        Assert.Equal([0, 1], CourseDraw.Chances([-4, 2]));                 // (a count below nothing is no vote)
    }

    /// <summary>Three votes against one: the part is three times as wide, and over a whole run the pointer is over
    /// it three times as long.</summary>
    [Fact]
    public void TheStripsPartsAreAsWideAsTheChancesAndThePointerIsOverThemAsLong()
    {
        var parts = CourseDraw.Parts([0, 3, 0, 1, 0, 0]);
        Assert.Equal([1, 3], parts.Select(p => p.Option));
        Assert.Equal((0, 0.75), (parts[0].Start, parts[0].Width));
        Assert.Equal((0.75, 0.25), (parts[1].Start, parts[1].Width));
        Assert.Equal(3, parts[0].Width / parts[1].Width, 9);

        // one pass at an even pace: three quarters of it over the option with three votes
        var over = Enumerable.Range(0, 1000).Select(i => CourseDraw.Under(parts, (i + 0.5) / 1000)).ToList();
        Assert.Equal((750, 250), (over.Count(o => o == 1), over.Count(o => o == 3)));

        // nobody voted: six parts alike
        var alike = CourseDraw.Parts([0, 0, 0, 0, 0, 0]);
        Assert.Equal(Enumerable.Range(0, 6), alike.Select(p => p.Option));
        Assert.All(alike, p => Assert.Equal(1 / 6.0, p.Width, 12));
        Assert.Equal(1, alike[^1].End, 12);
        Assert.Null(CourseDraw.Under([], 0.3));
    }

    [Theory]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, 3)]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, 5)]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, 1)]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0 }, 4)]
    [InlineData(new[] { 0, 0, 5, 0, 0, 0 }, 2)]
    public void ThePointerPassesTheStripSlowsAndStopsInsideThePartDrawn(int[] votes, int winner)
    {
        var parts = CourseDraw.Parts(votes);
        foreach (var spin in new[] { 0, 1, 499, 998, 999, 1000, -7, int.MaxValue, int.MinValue })
        {
            var landing = CourseDraw.Landing(parts, winner, spin);
            var part = parts.Single(p => p.Option == winner);
            Assert.InRange(landing, part.Start, part.End);
            // inside one of its votes' slots, off the slot's edges
            var slot = part.Width / part.Slots;
            var within = (landing - part.Start) / slot % 1;
            Assert.InRange(within, 0.19, 0.81);
            Assert.Equal(winner, CourseDraw.Step(parts, landing)!.Option);
            Assert.Equal(winner, CourseDraw.Under(parts, CourseDraw.Position(landing, 1)));
            Assert.Equal(landing, CourseDraw.Position(landing, 1), 12);
            Assert.Equal(landing, CourseDraw.Position(landing, 7), 12);       // (past its end it stands)
            Assert.Equal(landing, CourseDraw.Position(landing, double.NaN), 12);

            // ever slower: each tenth of the run covers less of the strip than the one before; three passes in all
            double Behind(double t) => CourseDraw.Passes * Math.Pow(1 - t, 3);
            var steps = Enumerable.Range(0, 10).Select(i => Behind(i / 10.0) - Behind((i + 1) / 10.0)).ToList();
            Assert.All(steps.Zip(steps.Skip(1)), p => Assert.True(p.Second < p.First));
            Assert.Equal(CourseDraw.Passes, steps.Sum(), 9);
            Assert.All(Enumerable.Range(0, 101), i => Assert.InRange(CourseDraw.Position(landing, i / 100.0), 0, 0.9999999999));
            // every option that can be drawn is passed on the way, none that cannot
            var passed = Enumerable.Range(0, 2000).Select(i => CourseDraw.Under(parts, CourseDraw.Position(landing, i / 2000.0))).Distinct().ToList();
            Assert.Equal(parts.Select(p => (int?)p.Option).Order(), passed.Order());
        }
        Assert.InRange(CourseDraw.RunMs + CourseDraw.HoldMs, 2500, 3900);   // over before a server's 4 s are
    }

    /// <summary>Overwatch's way: the frame steps through the votes, not the cards: a card with three votes is three
    /// steps in a row, a card with none is passed by (unless nobody voted: then each card is one step).</summary>
    [Theory]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, new[] { 1, 1, 1, 3, 5, 5 })]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1 }, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0 }, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(new[] { 0, 0, 5, 0, 0, 0 }, new[] { 2, 2, 2, 2, 2 })]
    [InlineData(new[] { 2, 0, 0, 0, 0, 1 }, new[] { 0, 0, 5 })]   // (the last option: "Random course", one choice like any)
    public void TheDrawStepsThroughTheVotesInOrder(int[] votes, int[] options)
    {
        var parts = CourseDraw.Parts(votes);
        var sequence = CourseDraw.Sequence(parts);
        Assert.Equal(options, sequence.Select(s => s.Option));
        Assert.Equal(Enumerable.Range(0, options.Length), sequence.Select(s => s.Index));
        // each card's votes counted from 0, one after another
        Assert.All(sequence.GroupBy(s => s.Option), g => Assert.Equal(Enumerable.Range(0, g.Count()), g.Select(s => s.Vote)));
        // one pass at an even pace meets the steps in that order, each as long as the next
        var met = Enumerable.Range(0, 6000).Select(i => CourseDraw.Step(parts, (i + 0.5) / 6000)!).ToList();
        Assert.Equal(sequence, met.Distinct());
        Assert.All(met.GroupBy(s => s.Index), g => Assert.Equal(6000 / options.Length, g.Count(), 1.0));
        Assert.Null(CourseDraw.Step([], 0.4));
    }

    /// <summary>Every step of the run ticks once (the first included), two votes of one card two ticks; the run
    /// passes the strip three times from the landing to the landing, so its ticks are three passes of steps and one;
    /// it ends on the winner. Sampled at the screen's 60 a second, the fast start merges steps (one tick a frame at
    /// most) and the slow end ticks every step.</summary>
    [Theory]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, 3)]
    [InlineData(new[] { 0, 3, 0, 1, 0, 2 }, 1)]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0 }, 4)]
    [InlineData(new[] { 0, 0, 5, 0, 0, 0 }, 2)]
    [InlineData(new[] { 4, 0, 1, 0, 0, 3 }, 5)]
    public void EveryStepOfTheDrawTicksOnceAndItLandsOnTheCourseDrawn(int[] votes, int winner)
    {
        var parts = CourseDraw.Parts(votes);
        var steps = CourseDraw.Sequence(parts).Count;
        foreach (var spin in new[] { 0, 1, 2, 499, 77777, -7 })
        {
            var landing = CourseDraw.Landing(parts, winner, spin);
            (int Ticks, List<DrawStep> Visited) Run(int samples)
            {
                var frame = new DrawFrame();
                var ticks = 0;
                var visited = new List<DrawStep>();
                for (var i = 0; i <= samples; i++)
                {
                    if (frame.Move(CourseDraw.Step(parts, CourseDraw.Position(landing, i / (double)samples))))
                    {
                        ticks++;
                        visited.Add(frame.Step!);
                    }
                }
                return (ticks, visited);
            }
            var (fine, path) = Run(200_000);
            Assert.Equal((CourseDraw.Passes * steps) + 1, fine);
            // step after step along the strip, never skipping one, never standing twice
            Assert.All(path.Zip(path.Skip(1)), p => Assert.Equal((p.First.Index + 1) % steps, p.Second.Index));
            Assert.Equal(winner, path[^1].Option);
            Assert.Equal(CourseDraw.Step(parts, landing), path[^1]);
            var (frames, _) = Run(CourseDraw.RunMs * 60 / 1000);
            Assert.InRange(frames, 1, fine);
        }
    }

    [Fact]
    public void TheFrameTicksOnlyOnAStepItWasNotOn()
    {
        var frame = new DrawFrame();
        Assert.False(frame.Move(null));
        Assert.True(frame.Move(new DrawStep(1, 0, 0)));
        Assert.False(frame.Move(new DrawStep(1, 0, 0)));
        Assert.True(frame.Move(new DrawStep(1, 1, 1)));    // the same card, its next vote: a tick
        Assert.True(frame.Move(new DrawStep(3, 0, 2)));
        Assert.False(frame.Move(null));
        Assert.Null(frame.Step);
    }

    [Fact]
    public void AWinnerWithNoPartStopsThePointerAtTheStart() =>
        Assert.Equal(0, CourseDraw.Landing(CourseDraw.Parts([2, 0, 1]), 1, 5));

    private static LobbyState Lobby(string stage, string? waiting = null, params bool[] racing) =>
        new("l1", [.. racing.Select((r, i) => new PlayerInfo($"p{i}", $"P{i}", 0, new Rgb(1, 2, 3), Racing: r))], 30, 2, true, 0, 0, stage,
            Waiting: waiting);

    [Fact]
    public void TheLineOfStepsSaysWhereTheLobbyIs()
    {
        Assert.Equal(["Gathering", "Course vote", "Draw", "Machines", "Starting", "Race"], LobbyScreens.Steps(true).Select(LobbyScreens.Name));
        Assert.Equal(["Gathering", "Machines", "Starting", "Race"], LobbyScreens.Steps(false).Select(LobbyScreens.Name));

        LobbyStep Step(LobbyState? lobby, bool vote = true, bool drawn = false, bool running = false) =>
            LobbyScreens.Step(lobby, vote, drawn, running);
        Assert.Equal(LobbyStep.Gathering, Step(null));
        Assert.Equal(LobbyStep.Gathering, Step(Lobby(LobbyState.Forming)));
        Assert.Equal(LobbyStep.CourseVote, Step(Lobby(LobbyState.Starting)));
        Assert.Equal(LobbyStep.Draw, Step(Lobby(LobbyState.Starting), drawn: true));
        Assert.Equal(LobbyStep.Machines, Step(Lobby(LobbyState.Starting), vote: false));   // no vote: the machines are next
        Assert.Equal(LobbyStep.Machines, Step(Lobby(LobbyState.Choosing), drawn: true));
        Assert.Equal(LobbyStep.Starting, Step(Lobby(LobbyState.Choosing, LobbyState.WaitingForRelay), drawn: true));
        Assert.Equal(LobbyStep.Starting, Step(Lobby(LobbyState.Racing)));                  // the games are being started
        Assert.Equal(LobbyStep.Race, Step(Lobby(LobbyState.Racing), running: true));
        // every step a lobby can be at is on its server's line
        Assert.All(new[] { LobbyState.Forming, LobbyState.Starting, LobbyState.Choosing, LobbyState.Racing },
            stage => Assert.Contains(Step(Lobby(stage), vote: false), LobbyScreens.Steps(false)));
    }

    /// <summary>"2 of 4": more than half of the members present, as the server counts a start vote.</summary>
    [Theory]
    [InlineData(new[] { false }, 1)]
    [InlineData(new[] { false, false }, 2)]
    [InlineData(new[] { false, false, false }, 2)]
    [InlineData(new[] { false, false, false, false, false, false }, 4)]
    [InlineData(new[] { false, false, false, true, true, true }, 2)]        // three still out in the race before
    public void AStartNeedsMoreThanHalfOfTheMembersPresent(bool[] racing, int needed) =>
        Assert.Equal(needed, LobbyScreens.StartVotesNeeded(Lobby(LobbyState.Forming, null, racing)));

    /// <summary>The Garage is open all through the lobby, and only in one.</summary>
    [Fact]
    public void TheGarageIsOpenAllThroughTheLobby()
    {
        Assert.True(LobbyScreens.GarageOpen(inLobby: true));
        Assert.False(LobbyScreens.GarageOpen(inLobby: false));
    }
}
