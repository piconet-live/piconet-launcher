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

        var best = new MachineSort(MachineStat.BoostRating).Order(table);        // A to E
        var worst = new MachineSort(MachineStat.BoostRating, Descending: true).Order(table);

        // boost is 1 + (3 * machine + 1) % 5: the As are 3, 8, 13.., the Es 1, 6, 11..
        Assert.Equal([3, 8, 13, 18, 23, 28, 33, 38], best.Take(8));
        Assert.Equal([1, 6, 11, 16, 21, 26, 31, 36], worst.Take(8));
        Assert.Equal([3, 8, 13, 18, 23, 28, 33, 38], worst.TakeLast(8));
    }

    /// <summary>The body letters and values of a few machines, as the disc's stand: the letters' ranges of the
    /// value overlap (machine 2 is a B tougher than the A machine 0). Every other machine is a C of 1.0.</summary>
    private static MachineStatTable BodyTable() => MachineStatTable.Of((machine, stat) => (stat, machine) switch
    {
        (MachineStat.BodyRating, 0 or 1) => 1,
        (MachineStat.BodyRating, 2 or 3) => 2,
        (MachineStat.BodyRating, 4) => 5,
        (MachineStat.BodyRating, _) => 3,
        (MachineStat.Body, 0) => 0.6f,
        (MachineStat.Body, 1) => 0.4f,
        (MachineStat.Body, 2) => 0.5f,
        (MachineStat.Body, 3) => 0.9f,
        (MachineStat.Body, 4) => 1.5f,
        (MachineStat.Body, _) => 1.0f,
        _ => MadeUpStats.Value(machine, stat),
    });

    private static readonly int[] TheCs = [.. Enumerable.Range(5, 36)];

    /// <summary>The body value multiplies the damage taken (handbook machines.md): the smaller, the tougher. So it is
    /// first shown smallest first, in words that say which end is tough; the other stats as before.</summary>
    [Fact]
    public void TheBodyValueIsFirstShownToughestFirst()
    {
        var table = BodyTable();

        var first = MachineSort.FirstWay(MachineStat.Body);

        Assert.Equal(new MachineSort(MachineStat.Body), first);                  // low to high
        Assert.Equal([1, 2, 0, 3, .. TheCs, 4], first.Order(table));
        Assert.Equal([4, .. TheCs, 3, 0, 2, 1], (first with { Descending = true }).Order(table));
        Assert.Equal(("toughest first", "weakest first"), MachineStats.Of(MachineStat.Body).Orders);
        Assert.Equal(new MachineSort(MachineStat.BodyRating), MachineSort.FirstWay(MachineStat.BodyRating));   // A to E
        Assert.Equal(new MachineSort(MachineStat.Weight, Descending: true), MachineSort.FirstWay(MachineStat.Weight));
        Assert.Equal(new MachineSort(MachineStat.Drag, Descending: true), MachineSort.FirstWay(MachineStat.Drag));
        Assert.Equal(new MachineSort(MachineStat.TurnDeceleration, Descending: true), MachineSort.FirstWay(MachineStat.TurnDeceleration));
        Assert.Equal(MachineSort.ByNumber, MachineSort.FirstWay(MachineStat.Number));
        Assert.Equal([MachineStat.Body], MachineStats.All.Where(s => s.SmallerIsBetter).Select(s => s.Stat));
    }

    /// <summary>The body letters are the disc's and their ranges of the value overlap: within a letter the machines
    /// stand by their body value, the toughest first from A to E (and the weakest first from E to A).</summary>
    [Fact]
    public void WithinABodyLetterTheToughestComesFirst()
    {
        var table = BodyTable();

        Assert.Equal([1, 0, 2, 3, .. TheCs, 4], new MachineSort(MachineStat.BodyRating).Order(table));
        Assert.Equal([4, .. TheCs, 3, 2, 0, 1], new MachineSort(MachineStat.BodyRating, Descending: true).Order(table));
        // a letter without a body value: after its letter's machines with one, by number
        var noValue = MachineStatTable.Of((machine, stat) => stat == MachineStat.Body && machine == 1 ? null : BodyTable().Value(machine, stat));
        Assert.Equal([0, 1, 2, 3], new MachineSort(MachineStat.BodyRating).Order(noValue).Take(4));
        Assert.Equal([3, 2, 0, 1], new MachineSort(MachineStat.BodyRating, Descending: true).Order(noValue).TakeLast(4));
    }

    /// <summary>A stat pack changes body values and not the letters (it does not carry them): the body value's order
    /// follows the pack, and within each letter the pack's toughest comes first, so a list by letter stays a list of
    /// toughness wherever the pack's values and the disc's letters disagree.</summary>
    [Fact]
    public void UnderAStatPackBothBodyOrdersFollowThePacksValues()
    {
        var pack = Online.Protocol.StatPack.Of("Made up",
            [Online.Protocol.StatEntry.Of(0, 0x44, 0.2f), Online.Protocol.StatEntry.Of(3, 0x44, 0.1f), Online.Protocol.StatEntry.Of(4, 0x44, 0.3f)]).Pack!;

        var packed = BodyTable().With(pack);

        Assert.Equal(("A", "B", "E"), (packed.Text(0, MachineStat.BodyRating), packed.Text(3, MachineStat.BodyRating),
            packed.Text(4, MachineStat.BodyRating)));                          // the disc's letters
        Assert.Equal([3, 0, 4, 1, 2, .. TheCs], MachineSort.FirstWay(MachineStat.Body).Order(packed));
        Assert.Equal([0, 1, 3, 2, .. TheCs, 4], MachineSort.FirstWay(MachineStat.BodyRating).Order(packed));
        Assert.Equal([4, .. TheCs, 2, 3, 1, 0], new MachineSort(MachineStat.BodyRating, Descending: true).Order(packed));
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
