using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

public sealed class RaceStandingsTests
{
    private static RacerLive Racer(int seat, string state, float distance, int totalMs = 60_000, int rank = 1, int place = 0, float? score = null) =>
        new(seat, rank, 0, 0, totalMs, null, 0, 0, 100, 0, 0, state, 0, 0, 0, distance, place, score);

    [Fact]
    public void FinishedByTimeThenRacingByDistanceThenTheKnockedOut()
    {
        var racers = new[]
        {
            Racer(0, RacerLive.Racing, 9_000, rank: 1),     // the game's byte says first; it is a lap behind
            Racer(1, RacerLive.Racing, 16_500, rank: 2),
            Racer(2, RacerLive.Finished, 24_000, totalMs: 190_000),
            Racer(3, RacerLive.Finished, 24_000, totalMs: 185_000),
            Racer(4, RacerLive.Out, 3_000),
            Racer(5, RacerLive.Out, 12_000),
        };
        Assert.Equal([3, 2, 1, 0, 5, 4], RaceStandings.Order(racers).Select(r => r.Seat));
        var places = RaceStandings.Places(racers);
        Assert.Equal((1, 3, 4), (places[3], places[1], places[0]));
        Assert.False(places.ContainsKey(4));
    }

    /// <summary>The mode's places (the order the game's HUD shows: by points, cuts, distance) order the standings before
    /// times and distances, and every placed racer has a place, a knocked-out one too (a cut Knockout racer keeps its
    /// place; a Death Race racer out early with more points stands above one still racing).</summary>
    [Fact]
    public void TheModesPlacesOrderTheStandingsAndGiveTheKnockedOutTheirPlace()
    {
        var racers = new[]
        {
            Racer(0, RacerLive.Racing, 9_000, place: 3, score: 4),
            Racer(1, RacerLive.Out, 2_000, place: 1, score: 12),
            Racer(2, RacerLive.Finished, 24_000, totalMs: 185_000, place: 4, score: 1),
            Racer(3, RacerLive.Racing, 20_000, place: 2, score: 9),
        };
        Assert.Equal([1, 3, 0, 2], RaceStandings.Order(racers).Select(r => r.Seat));
        var places = RaceStandings.Places(racers);
        Assert.Equal((1, 2, 3, 4), (places[1], places[3], places[0], places[2]));
    }

    /// <summary>A racer's score in the standings: whole points; metres with their unit, in thousands; nothing for a racer
    /// the mode shows none, or a race without a score (its standings show times and laps).</summary>
    [Fact]
    public void TheScoreReadsAsPointsOrMetres()
    {
        var points = new RaceSnapshot(1, 0, [Racer(0, RacerLive.Racing, 1, score: 12), Racer(1, RacerLive.Racing, 1)], RaceSnapshot.Points, "POINTS");
        Assert.True(points.Scored);
        Assert.Equal("12", RaceStandings.ScoreText(points, points.Racers[0]));
        Assert.Equal("", RaceStandings.ScoreText(points, points.Racers[1]));
        var metres = new RaceSnapshot(1, 0, [Racer(0, RacerLive.Racing, 1, score: 12345.9f)], RaceSnapshot.Metres);
        Assert.Equal("12,345 m", RaceStandings.ScoreText(metres, metres.Racers[0]));
        var none = new RaceSnapshot(1, 0, [Racer(0, RacerLive.Racing, 1, score: 5)]);
        Assert.False(none.Scored);
        Assert.Equal("", RaceStandings.ScoreText(none, none.Racers[0]));
    }

    /// <summary>A standings row's phase: a player whose race is run says whether their game is still open (the lobby
    /// waits for it) or they are back in the lobby; a racer still racing, a CPU, or no lobby to tell says nothing.</summary>
    [Fact]
    public void APlayerWhoseRaceIsRunSaysWhetherTheirGameIsStillOpen()
    {
        static SeatSetup Seat(int seat, string? player) => new(seat, player, "X", 1, null);
        var open = new HashSet<string> { "p1" };
        Assert.Equal("Game open", RaceViewModel.Phase(Racer(0, RacerLive.Finished, 1), Seat(0, "p1"), open));
        Assert.Equal("Game open", RaceViewModel.Phase(Racer(0, RacerLive.Out, 1), Seat(0, "p1"), open));
        Assert.Equal("In lobby", RaceViewModel.Phase(Racer(1, RacerLive.Finished, 1), Seat(1, "p2"), open));
        Assert.Equal("", RaceViewModel.Phase(Racer(0, RacerLive.Racing, 1), Seat(0, "p1"), open));
        Assert.Equal("", RaceViewModel.Phase(Racer(2, RacerLive.Finished, 1), Seat(2, null), open));
        Assert.Equal("", RaceViewModel.Phase(Racer(0, RacerLive.Finished, 1), Seat(0, "p1"), null));
        Assert.True(new StandingRow(0, "1", "X", "", "#000000", "", "", false, false, false, "Game open").GameOpen);
    }

    /// <summary>A standings row's machine: the custom machine a seat races (whatever the disc's naming tables give it,
    /// never the stock machine under it), else the seat's stock machine.</summary>
    [Fact]
    public void ASeatsCustomMachineIsItsMachineInTheStandings()
    {
        var stock = new SeatSetup(0, "p1", "ANA", 2, null);
        Assert.Equal(Machines.Names[2], RaceViewModel.MachineName(stock));
        var custom = RaceViewModel.MachineName(stock with { Custom = new CustomMachine(15, 44, 58, 2) });
        Assert.NotEqual(Machines.Names[2], custom);
        Assert.NotEmpty(custom);
        Assert.Equal(Machines.Names[0], RaceViewModel.MachineName(null));
    }
}
