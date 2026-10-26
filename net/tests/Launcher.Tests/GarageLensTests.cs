using System.Buffers.Binary;
using Launcher.App.Theme;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>
/// The Garage rework's logic on made-up tables (no disc needed): the shelves' search
/// and chips (<see cref="MachineFilter"/>); a parts machine's three letters as the game's <c>fn_1_1373B0</c> gives them
/// (body from the body, boost from the booster, grip from the cockpit; grade / 2 + 1); a stat pack laid over the car
/// data as the game lays it, and a custom machine summed from the parts as the pack leaves them, beside its stock
/// sum; and a custom machine's favourite mark through the settings file.
/// </summary>
public sealed class GarageLensTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Func<MachineStat, float?> Values(float? weight, int body = 3, int boost = 3, int grip = 3) => stat => stat switch
    {
        MachineStat.Weight => weight,
        MachineStat.BodyRating => body,
        MachineStat.BoostRating => boost,
        MachineStat.GripRating => grip,
        _ => null,
    };

    [Fact]
    public void TheSearchTakesEveryWordAndTheLettersTakeAnyOfThem()
    {
        var open = new MachineFilter();
        Assert.True(open.Open);
        Assert.True(open.Matches("Blue Falcon", Values(null)));

        // every word typed, any case, any order, anywhere in the name
        Assert.True((open with { Query = "falc" }).Matches("Blue Falcon", Values(1260)));
        Assert.True((open with { Query = "  FALCON blue " }).Matches("Blue Falcon", Values(1260)));
        Assert.False((open with { Query = "blue fox" }).Matches("Blue Falcon", Values(1260)));

        // the letters: a stat with letters chosen takes any of them; every stat with letters chosen must match
        var bodyA = open.Toggled(MachineStat.BodyRating, 0);
        Assert.True(bodyA.Matches("x", Values(1000, body: 1)));
        Assert.False(bodyA.Matches("x", Values(1000, body: 2)));
        var bodyAB = bodyA.Toggled(MachineStat.BodyRating, 1);
        Assert.True(bodyAB.Matches("x", Values(1000, body: 2)));
        Assert.False(bodyAB.Matches("x", Values(1000, body: 3)));
        var bodyAndGrip = bodyA.Toggled(MachineStat.GripRating, 0);
        Assert.False(bodyAndGrip.Matches("x", Values(1000, body: 1, grip: 2)));
        Assert.True(bodyAndGrip.Matches("x", Values(1000, body: 1, grip: 1, boost: 5)));
        Assert.Equal(2, bodyAndGrip.LetterCount);
        Assert.Equal(open, bodyA.Toggled(MachineStat.BodyRating, 0));                     // pressed again: off
        Assert.False(bodyA.Matches("x", _ => null));                                        // a letter not known: no match
    }

    /// <summary>A main module with the parts' rows at their place: id, kind, and the grade at +8.</summary>
    private static byte[] Module(Func<int, int> grade)
    {
        var module = new byte[CustomPartFile.GradesAt + (75 * 16)];
        for (var part = 0; part < 75; part++)
        {
            var row = module.AsSpan(CustomPartFile.GradesAt + (part * 16));
            BinaryPrimitives.WriteInt16BigEndian(row, (short)part);
            BinaryPrimitives.WriteInt16BigEndian(row[2..], (short)(part / 25));
            BinaryPrimitives.WriteInt16BigEndian(row[8..], (short)grade(part));
        }
        return module;
    }

    [Fact]
    public void APartsMachinesLettersAreItsBodysBoostersAndCockpits()
    {
        // grades 0 to 9 by part: the letters 1 to 5 as grade / 2 + 1
        var letters = CustomPartFile.Grades(Module(part => part % 10))!;
        Assert.Equal([1, 1, 2, 2, 3, 3, 4, 4, 5, 5], letters.Take(10));
        // body 3 (grade 3: B), cockpit 27 (grade 7: D), booster 56 (grade 6: D)... and booster 59 (grade 9: E)
        var row = new float?[MachineStats.All.Count];
        CustomPartFile.Rate(row, letters, 3, 27, 59);
        Assert.Equal((2f, 5f, 4f), (row[(int)MachineStat.BodyRating], row[(int)MachineStat.BoostRating], row[(int)MachineStat.GripRating]));
        Assert.Equal("B", MachineStats.Text(MachineStat.BodyRating, row[(int)MachineStat.BodyRating]));

        // not the table: a grade beyond 9, a row out of its place, too few bytes
        Assert.Null(CustomPartFile.Grades(Module(part => part == 40 ? 10 : 1)));
        var shifted = Module(_ => 1);
        BinaryPrimitives.WriteInt16BigEndian(shifted.AsSpan(CustomPartFile.GradesAt + (30 * 16)), 31);
        Assert.Null(CustomPartFile.Grades(shifted));
        Assert.Null(CustomPartFile.Grades(shifted.AsSpan(0, shifted.Length - 1)));
        var none = new float?[MachineStats.All.Count];
        CustomPartFile.Rate(none, null, 3, 27, 59);
        Assert.All(none, Assert.Null);
    }

    private static byte[] CarData()
    {
        var car = new byte[0x549A];
        void Part(int id, float weight, float acceleration)
        {
            var at = 0x1F04 + (id * 0xB4);
            BinaryPrimitives.WriteUInt32LittleEndian(car.AsSpan(at), 0x238053C0);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x04), weight);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x08), acceleration);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x44), 1);
        }
        Part(3, 400, 0);
        Part(27, 300, 0);
        Part(59, 500, 0.25f);
        return car;
    }

    [Fact]
    public void APackOverTheCarDataChangesACustomMachinesSumAsARaceOfItSumsIt()
    {
        var car = CarData();
        // the pack: the booster's weight (block 41 + 59) and a stock machine's weight (block 6), and the byte word at 0x48
        var (pack, _) = StatPack.Of("Heavy Boosters", [StatEntry.Of(41 + 59, 0x04, 900), StatEntry.Of(6, 0x04, 2000), new StatEntry(41 + 27, 0x48, 0x05000000)]);
        Assert.NotNull(pack);
        var packed = CustomPartFile.WithPack(car, pack);
        Assert.Equal(900f, BinaryPrimitives.ReadSingleLittleEndian(packed.AsSpan(0x1F04 + (59 * 0xB4) + 4)));
        Assert.Equal(2000f, BinaryPrimitives.ReadSingleLittleEndian(packed.AsSpan((6 * 0xB4) + 4)));
        Assert.Equal(5, packed[0x1F04 + (27 * 0xB4) + 0x48]);                   // the byte word as it stands, high byte first
        Assert.Equal(500f, BinaryPrimitives.ReadSingleLittleEndian(car.AsSpan(0x1F04 + (59 * 0xB4) + 4)));   // the disc's untouched

        // through the builder's parts: the sum with the pack, and without it
        var parts = new CustomParts(new CustomPartTables([.. Enumerable.Range(0, 75).Select(p => p < 50 ? "A B C D" : "Bst -X -Y")],
            new uint[75], [], []), car, null, CustomPartFile.Grades(Module(part => part % 10)));
        var build = new CustomBuild(3, 27, 59, 6);
        var stock = parts.Stats(build)!;
        var lens = parts.Stats(build, pack)!;
        Assert.Equal(1200f, stock[(int)MachineStat.Weight]);
        Assert.Equal(1600f, lens[(int)MachineStat.Weight]);
        Assert.Equal(stock[(int)MachineStat.Acceleration], lens[(int)MachineStat.Acceleration]);   // what the pack leaves
        Assert.Equal((2f, 5f, 4f), (lens[(int)MachineStat.BodyRating], lens[(int)MachineStat.BoostRating], lens[(int)MachineStat.GripRating]));
        Assert.Equal(1200f, parts.Stats(build)![(int)MachineStat.Weight]);        // asked again without: the disc's
    }

    /// <summary>A custom machine's heart goes through the settings file; it is no part of the machine.</summary>
    [Fact]
    public void ACustomMachinesHeartIsKeptAndIsNoPartOfTheMachine()
    {
        var path = Path.Combine(_dir, "settings.json");
        var marked = new CustomBuild(15, 44, 58, 29, Favourite: true);
        new SettingsStore(path).Save(new LauncherSettings { Racer = RacerProfile.Default with { Customs = [marked, new CustomBuild(0, 27, 50, 6)] } });
        var back = new SettingsStore(path).Load().Racer.Customs!;
        Assert.Equal([true, false], back.Select(b => b.Favourite));
        Assert.Equal(marked with { Favourite = false }, back[0]);                  // the same machine, marked or not
    }
}
