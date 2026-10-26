using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The stat table and the order a list of machines is shown in (<see cref="MachineStatTable"/>,
/// <see cref="MachineSort"/>), on a made-up table.</summary>
public sealed class MachineSortTests
{
    /// <summary>A table of the made-up numbers; with <paramref name="letters"/> false it has no letters, as a disc
    /// whose main module is another gives it.</summary>
    private static MachineStatTable Table(bool letters = true) => MachineStatTable.Of((machine, stat) => stat switch
    {
        MachineStat.BodyRating or MachineStat.BoostRating or MachineStat.GripRating =>
            letters ? MadeUpStats.Rating(machine, stat - MachineStat.BodyRating) : null,
        _ => MadeUpStats.Value(machine, stat),
    });

    [Fact]
    public void ByNumberIsTheGamesOrderAndTheOrderWithoutAChoice()
    {
        Assert.Equal(new MachineSort(), MachineSort.ByNumber);
        Assert.Equal(Enumerable.Range(0, 41), MachineSort.ByNumber.Order(Table()));
        Assert.Equal(Enumerable.Range(0, 41), MachineSort.ByNumber.Order(null));
        Assert.Equal(Enumerable.Range(0, 41).Reverse(), new MachineSort(MachineStat.Number, Descending: true).Order(null));
    }

    [Theory]
    [InlineData(MachineStat.Weight)]
    [InlineData(MachineStat.MaxSpeed)]
    [InlineData(MachineStat.Drag)]
    public void ByAStatTheValuesRiseOrFallAndEveryMachineIsThereOnce(MachineStat stat)
    {
        var table = Table();

        var rising = new MachineSort(stat).Order(table);
        var falling = new MachineSort(stat, Descending: true).Order(table);

        Assert.Equal(Enumerable.Range(0, 41), rising.Order());
        Assert.NotEqual(Enumerable.Range(0, 41), rising);                      // the made-up values are not in the ids' order
        Assert.Equal(rising.Select(m => table.Value(m, stat)).Order(), rising.Select(m => table.Value(m, stat)));
        Assert.Equal(rising.Reverse(), falling);                               // no two machines have the same made-up value
        Assert.Equal(0, rising[0]);                                            // (0 * 7) % 41: the least
        Assert.Equal(35, falling[0]);                                          // (35 * 7) % 41 = 40: the most
    }

    [Fact]
    public void MachinesWithTheSameValueStandByNumberInBothOrders()
    {
        var table = Table();

        var best = new MachineSort(MachineStat.BodyRating).Order(table);         // A to E
        var worst = new MachineSort(MachineStat.BodyRating, Descending: true).Order(table);

        // body is 1 + (3 * machine) % 5: the As are 0, 5, 10.., the Es 3, 8, 13..
        Assert.Equal([0, 5, 10, 15, 20, 25, 30, 35, 40], best.Take(9));
        Assert.Equal([3, 8, 13, 18, 23, 28, 33, 38], worst.Take(8));
        Assert.Equal([0, 5, 10, 15, 20, 25, 30, 35, 40], worst.TakeLast(9));
    }

    [Fact]
    public void AMachineWhoseValueIsNotKnownComesLastEitherWayAndWithoutValuesTheOrderIsByNumber()
    {
        var table = MachineStatTable.Of((machine, stat) => machine is 2 or 9 ? null : MadeUpStats.Value(machine, stat));

        Assert.Equal([2, 9], new MachineSort(MachineStat.Weight).Order(table).TakeLast(2));
        Assert.Equal([2, 9], new MachineSort(MachineStat.Weight, Descending: true).Order(table).TakeLast(2));
        Assert.Equal(Enumerable.Range(0, 41), new MachineSort(MachineStat.GripRating, Descending: true).Order(Table(letters: false)));
        Assert.Equal(Enumerable.Range(0, 41), new MachineSort(MachineStat.Weight, Descending: true).Order(null));   // no disc
        Assert.Equal(Enumerable.Range(0, 41), new MachineSort((MachineStat)77, Descending: true).Order(Table()));   // no such stat
    }

    [Fact]
    public void AValueReadsAsTheGarageWritesIt()
    {
        var table = MachineStatTable.Of((machine, stat) => stat switch
        {
            MachineStat.BodyRating => 1,
            MachineStat.BoostRating => 5,
            MachineStat.GripRating => 9,                                       // no letter
            MachineStat.Weight => 1260,
            MachineStat.Acceleration => 0.45f,
            MachineStat.TurnTension => 0.008f,
            MachineStat.TurnMovement => 175,
            MachineStat.Drag => float.NaN,                                     // not a number: not known
            _ => null,
        });

        Assert.Equal(["7", "A", "E", "", "1260 kg", "0.45", "0.008", "175", "", ""],
            new[] { MachineStat.Number, MachineStat.BodyRating, MachineStat.BoostRating, MachineStat.GripRating, MachineStat.Weight,
                MachineStat.Acceleration, MachineStat.TurnTension, MachineStat.TurnMovement, MachineStat.Drag, MachineStat.Strafe }
                .Select(s => table.Text(6, s)));
        Assert.Null(table.Value(6, MachineStat.Drag));
        Assert.False(table.Knows(MachineStat.Strafe));
        Assert.Equal(("A to E", "E to A"), MachineStats.Of(MachineStat.GripRating).Orders);
        Assert.Equal(("low to high", "high to low"), MachineStats.Of(MachineStat.Weight).Orders);
        Assert.Equal(("1 to 41", "41 to 1"), MachineStats.Of(MachineStat.Number).Orders);
    }

    /// <summary>What a stat pack will do: change some machines' values in the table read from the disc. The table
    /// it is laid over stays as it was, and what sorts by the new one follows the new values.</summary>
    [Fact]
    public void AChangedValueIsLaidOverTheTableAndTheOrderFollowsIt()
    {
        var disc = Table();

        var packed = disc.With(0, MachineStat.Weight, 99_000).With(35, MachineStat.BodyRating, 5);

        Assert.Equal(MadeUpStats.Value(0, MachineStat.Weight), disc.Value(0, MachineStat.Weight));
        Assert.Equal(99_000, packed.Value(0, MachineStat.Weight));
        Assert.Equal(0, new MachineSort(MachineStat.Weight).Order(disc)[0]);
        Assert.Equal(0, new MachineSort(MachineStat.Weight).Order(packed)[^1]);
        Assert.Equal("E", packed.Text(35, MachineStat.BodyRating));
        Assert.Equal(MadeUpStats.Value(1, MachineStat.Weight), packed.Value(1, MachineStat.Weight));   // the rest as on the disc
        // what is no value of a machine changes nothing
        Assert.Same(disc, disc.With(41, MachineStat.Weight, 1));
        Assert.Same(disc, disc.With(0, MachineStat.Number, 5));
        Assert.Same(disc, disc.With(0, MachineStat.Weight, float.NaN));
    }

    [Fact]
    public void TheStatsNamesAreTheSettingsAndStayAsTheyAre()
    {
        // written into settings.json by name: a renamed stat would lose every player's choice
        Assert.Equal(
            ["Number", "BodyRating", "BoostRating", "GripRating", "Weight", "Acceleration", "MaxSpeed", "Body", "BoostStrength",
                "BoostDuration", "Grip1", "Grip2", "Grip3", "TurnTension", "TurnMovement", "TurnReaction", "TurnDeceleration",
                "DriftAcceleration", "Strafe", "StrafeTurn", "Drag", "CameraReorienting", "CameraRepositioning"],
            Enum.GetNames<MachineStat>());
        Assert.Equal(Enum.GetValues<MachineStat>(), MachineStats.All.Select(s => s.Stat));
        Assert.Equal(MachineStats.All.Count, MachineStats.All.Select(s => s.Label).Distinct().Count());
        Assert.Equal(21, MachineStats.Shown.Count);
    }
}
