using System.Buffers.Binary;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The machines' stats read from a disc image (<see cref="DiscArt.MachineStats()"/>,
/// <see cref="MachineStatFile"/>), on made-up images (<see cref="MadeUpStats"/>): the numbers from the car data, the
/// letters from the main module when it is the pinned one, and no stats, with nothing raised, from files that are
/// not the game's.</summary>
public sealed class MachineStatsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stat-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MachineStatTable? Read(SyntheticDisc disc, DiscPins? pins = null, string name = "stats.iso")
    {
        var path = disc.Write(_dir, name);
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));                    // the first look sees the header only
        using var art = DiscArt.Open(path);
        Assert.NotNull(art);
        return art.MachineStats(pins ?? disc.Pins());
    }

    private static long Allocated(Action act)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        act();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void TheStatsAreReadFromTheDiscsCarDataAndItsMainModule()
    {
        var table = Read(MadeUpStats.Disc(MadeUpStats.CarData(), MadeUpStats.MainModule()));

        Assert.NotNull(table);
        foreach (var machine in new[] { 0, 6, 29, 30, 40 })
        {
            Assert.Equal(machine + 1, table.Value(machine, MachineStat.Number));
            foreach (var stat in MachineStats.All.Where(s => s.At >= 0))
            {
                Assert.Equal(MadeUpStats.Value(machine, stat.Stat), table.Value(machine, stat.Stat));
            }
            Assert.Equal((MadeUpStats.Rating(machine, 0), MadeUpStats.Rating(machine, 1), MadeUpStats.Rating(machine, 2)),
                ((int)table.Value(machine, MachineStat.BodyRating)!, (int)table.Value(machine, MachineStat.BoostRating)!,
                    (int)table.Value(machine, MachineStat.GripRating)!));
        }
        Assert.All(MachineStats.All, s => Assert.True(table.Knows(s.Stat)));
        Assert.Null(table.Value(41, MachineStat.Weight));                      // no such machine
        Assert.Null(table.Value(0, (MachineStat)99));                          // no such stat
    }

    /// <summary>Each number at its own place in a machine's block, low byte first: the places written out here
    /// (the game's car data layout), each holding its own offset as its value.</summary>
    [Fact]
    public void EveryNumberIsReadFromItsPlaceInAMachinesBlock()
    {
        var places = new Dictionary<MachineStat, int>
        {
            [MachineStat.Weight] = 0x04, [MachineStat.Acceleration] = 0x08, [MachineStat.MaxSpeed] = 0x0C, [MachineStat.Grip1] = 0x10,
            [MachineStat.Grip3] = 0x14, [MachineStat.TurnTension] = 0x18, [MachineStat.DriftAcceleration] = 0x1C,
            [MachineStat.TurnMovement] = 0x20, [MachineStat.StrafeTurn] = 0x24, [MachineStat.Strafe] = 0x28,
            [MachineStat.TurnReaction] = 0x2C, [MachineStat.Grip2] = 0x30, [MachineStat.BoostStrength] = 0x34,
            [MachineStat.BoostDuration] = 0x38, [MachineStat.TurnDeceleration] = 0x3C, [MachineStat.Drag] = 0x40,
            [MachineStat.Body] = 0x44, [MachineStat.CameraReorienting] = 0x4C, [MachineStat.CameraRepositioning] = 0x50,
        };
        var data = new byte[41 * 0xB4];
        for (var machine = 0; machine < 41; machine++)
        {
            for (var at = 0; at < 0xB4; at += 4)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((machine * 0xB4) + at), (machine * 1000) + at);
            }
        }

        var numbers = MachineStatFile.Numbers(data);

        Assert.NotNull(numbers);
        Assert.Equal(places.Count, MachineStats.All.Count(s => s.At >= 0));
        foreach (var machine in new[] { 0, 1, 40 })
        {
            Assert.All(places, p => Assert.Equal((machine * 1000) + p.Value, numbers[machine][(int)p.Key]));
            Assert.Null(numbers[machine][(int)MachineStat.BodyRating]);        // the letters are not in this file
        }
    }

    /// <summary>The letters' place is known for one file, the pinned disc's main module: any other file there (none,
    /// one of another length, one of the same length and other bytes) gives the numbers without letters.</summary>
    [Theory]
    [InlineData("none")]
    [InlineData("the made-up disc's own")]
    [InlineData("the real game's pin")]
    [InlineData("a byte changed")]
    public void WithoutThePinnedMainModuleThereAreNumbersAndNoLetters(string which)
    {
        var disc = MadeUpStats.Disc(MadeUpStats.CarData(), which == "the made-up disc's own" ? null : MadeUpStats.MainModule());
        var pins = disc.Pins();
        switch (which)
        {
            case "none":
                disc.Files.Remove(MachineStatFile.MainModule);
                break;
            case "the real game's pin":
                pins = DiscPins.Game;
                break;
            case "a byte changed":
                disc.Files[MachineStatFile.MainModule]![^1] ^= 1;               // after the pins were worked out
                break;
        }

        var table = Read(disc, pins);

        Assert.NotNull(table);
        Assert.Equal(MadeUpStats.Value(40, MachineStat.Weight), table.Value(40, MachineStat.Weight));
        Assert.False(table.Knows(MachineStat.BodyRating) || table.Knows(MachineStat.BoostRating) || table.Knows(MachineStat.GripRating));
        Assert.Equal("", table.Text(0, MachineStat.GripRating));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(6u)]
    [InlineData(0xFFFFFFFFu)]
    public void AMainModuleWithAWordThatIsNoLetterGivesNoLetters(uint word)
    {
        var module = MadeUpStats.MainModule();
        BinaryPrimitives.WriteUInt32BigEndian(module.AsSpan(MachineStatFile.RatingsAt + (40 * 3 * 4) + 8), word);   // the last machine's grip

        var table = Read(MadeUpStats.Disc(MadeUpStats.CarData(), module));

        Assert.NotNull(table);
        Assert.False(table.Knows(MachineStat.BodyRating));
        Assert.True(table.Knows(MachineStat.Weight));
    }

    /// <summary>The pinned file itself can be anything on a made-up disc (its pin is worked out from it): one that
    /// ends before the letters, one that is no packed file, and one that says it unpacks to 2 GiB.</summary>
    [Theory]
    [InlineData("short")]
    [InlineData("noise")]
    [InlineData("2 GiB")]
    public void AMainModuleThatDoesNotHoldTheLettersGivesNoLettersAndCostsLittle(string which)
    {
        var disc = MadeUpStats.Disc(MadeUpStats.CarData(), null);
        disc.Files[MachineStatFile.MainModule] = which switch
        {
            "short" => MadeUpStats.Cipher(MadeUpStats.Packed(MadeUpStats.MainModule().AsSpan(0, MachineStatFile.RatingsAt + 100))),
            "noise" => SyntheticDisc.Bytes(77, 5000),
            _ => MadeUpStats.Cipher(MadeUpStats.Packed([1, 2, 3], int.MaxValue)),
        };
        var path = disc.Write(_dir, "module.iso");
        using var art = DiscArt.Open(path);
        var pins = disc.Pins();
        MachineStatTable? table = null;

        var allocated = Allocated(() => table = art!.MachineStats(pins));

        Assert.NotNull(table);
        Assert.False(table.Knows(MachineStat.GripRating));
        Assert.InRange(allocated, 0, 16 << 20);
    }

    /// <summary>A damaged car data file gives no stats, and nothing is raised: the Garage goes on without them.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("cut short")]
    [InlineData("not a number")]
    [InlineData("infinity")]
    [InlineData("no weight")]
    [InlineData("a weight below nothing")]
    [InlineData("a number beyond any")]
    [InlineData("noise")]
    [InlineData("empty")]
    public void CarDataThatIsNotTheGamesTableGivesNoStats(string which)
    {
        var data = MadeUpStats.CarData();
        void Put(int machine, MachineStat stat, float value) =>
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((machine * MachineStatFile.BlockSize) + MachineStats.Of(stat).At), value);
        switch (which)
        {
            case "cut short":
                data = data[..((41 * MachineStatFile.BlockSize) - 1)];
                break;
            case "not a number":
                Put(40, MachineStat.Drag, float.NaN);
                break;
            case "infinity":
                Put(0, MachineStat.MaxSpeed, float.PositiveInfinity);
                break;
            case "no weight":
                Put(17, MachineStat.Weight, 0);
                break;
            case "a weight below nothing":
                Put(17, MachineStat.Weight, -1200);
                break;
            case "a number beyond any":
                Put(3, MachineStat.Grip1, 1e30f);
                break;
            case "noise":
                data = SyntheticDisc.Bytes(31, data.Length);
                break;
            case "empty":
                data = [];
                break;
        }

        var table = Read(MadeUpStats.Disc(which == "missing" ? null : data, MadeUpStats.MainModule()));

        Assert.Null(table);
    }

    /// <summary>A packed car data file of eight bytes that says it unpacks to 2 GiB, and one cut off before it says
    /// anything: no stats, and no room made for what the file names.</summary>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(DiscArt.LargestUnpacked + 1)]
    [InlineData(-1)]
    [InlineData(null)]
    public void APackedCarDataFileThatNamesASizeNoFileHasIsNotUnpacked(int? says)
    {
        var disc = new SyntheticDisc();
        disc.Files[MachineStatFile.CarData + ".lz"] = says is { } size ? MadeUpStats.Packed([], size) : [1, 2, 3];
        var path = disc.Write(_dir, "packed.iso");
        using var art = DiscArt.Open(path);
        var pins = disc.Pins();
        MachineStatTable? table = null;

        var allocated = Allocated(() => table = art!.MachineStats(pins));

        Assert.Null(table);
        Assert.InRange(allocated, 0, 1 << 20);
    }

    [Fact]
    public void TheMainModulesCipherIsTheLoadersAndItsOwnInverse()
    {
        foreach (var length in new[] { 0, 1, 3, 4, 5, 8, 4099 })
        {
            var plain = SyntheticDisc.Bytes(50 + length, length);
            var enciphered = MachineStatFile.Decrypted(plain);
            Assert.Equal(MadeUpStats.Cipher(plain), enciphered);               // the test's own arithmetic agrees
            Assert.Equal(plain, MachineStatFile.Decrypted(enciphered));
        }
        // the first state, worked out by hand: 0xAF8910BE * 0x9B36BB94 + 0xCD8F3, its low 32 bits
        var first = unchecked((uint)((0xAF8910BEUL * 0x9B36BB94UL) + 0xCD8F3UL));
        Assert.Equal(first, BinaryPrimitives.ReadUInt32BigEndian(MachineStatFile.Decrypted(new byte[4])));
    }
}
