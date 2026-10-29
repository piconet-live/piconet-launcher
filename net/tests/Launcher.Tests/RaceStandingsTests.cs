using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

public sealed class RaceStandingsTests
{
    private static RacerLive Racer(int seat, string state, float distance, int totalMs = 60_000, int rank = 1) =>
        new(seat, rank, 0, 0, totalMs, null, 0, 0, 100, 0, 0, state, 0, 0, 0, distance);

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
