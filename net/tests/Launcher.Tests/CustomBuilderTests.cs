using System.Buffers.Binary;
using System.Text;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The custom machine builder's core on made-up tables and files (no disc needed): the game's naming rule
/// against worked cases, the stat sum against fn_1_14F6F8's reading,
/// and the saved machines' way through the settings file.</summary>
public sealed class CustomBuilderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // the strings and flags the cases need, as the player's main module holds them; every other part a filler
    private static readonly Dictionary<int, string> Strings = new()
    {
        [0] = "Brave Eagle Strike Condor", [12] = "Big Tyrant Jumbo Emperor", [14] = "Fire Wolf Burning Coyote",
        [15] = "Dread Hammer Gallant Gantlet", [17] = "Rage Knight Stormy Bouncer",
        [25] = "Wonder Worm Miracle Insect", [27] = "Combat Cannon Tornado Master", [28] = "Muscle Gorilla Spicy Tusk",
        [42] = "Red Rex Crimson King", [44] = "Maximum Star Omega Wizard", [47] = "Scud Viper Misty Serpent",
        [50] = "Euros -01", [51] = "Triangle -GT -GT", [53] = "Sunrise 140", [58] = "Titan -G4 -G4",
        [60] = "Thunderbolt -V2 -V2", [67] = "Bazooka -YS",
    };

    private static readonly Dictionary<int, uint> Flags = new()
    {
        [50] = 0x80000009, [51] = 0x8000001A, [53] = 0x80000001, [58] = 0x8000000A, [60] = 0x8000001B, [67] = 0x80000000,
    };

    private static string PartString(int id) =>
        Strings.TryGetValue(id, out var s) ? s : id < 50 ? $"P{id} Q{id} R{id} S{id}" : $"B{id} -X{id} -X{id}";

    private static CustomPartTables Tables() => new(
        [.. Enumerable.Range(0, 75).Select(PartString)], [.. Enumerable.Range(0, 75).Select(i => Flags.GetValueOrDefault(i))],
        [0x3868], ["Beat The Beast"]);

    [Theory]
    [InlineData(15, 44, 58, "Gallant Star-G4")]      // Dread Hammer + Maximum Star + Titan (0x0A)
    [InlineData(0, 27, 50, "Combat Condor")]         // Brave Eagle + Combat Cannon + Euros (0x09)
    [InlineData(17, 47, 51, "Stormy Serpent-GT")]    // Rage Knight + Scud Viper + Triangle (0x1A)
    [InlineData(0, 25, 67, "Brave Worm")]            // Brave Eagle + Wonder Worm + Bazooka (no bit)
    [InlineData(12, 42, 53, "Red Tyrant")]           // Big Tyrant + Red Rex + Sunrise (0x01, swap only)
    [InlineData(17, 47, 60, "Misty Bouncer-V2")]     // Rage Knight + Scud Viper + Thunderbolt (0x1B)
    [InlineData(14, 28, 58, "Beat The Beast")]       // Fire Wolf + Muscle Gorilla + Titan: listed, key 0x3868
    public void AMachineIsNamedByTheGamesRule(int body, int cockpit, int booster, string name) =>
        Assert.Equal(name, Tables().NameOf(body, cockpit, booster));

    [Fact]
    public void PartsAreNamedAsTheirListsShowThemAndKindsAreChecked()
    {
        var tables = Tables();
        Assert.Equal(("Dread Hammer", "Maximum Star", "Titan"), (tables.PartName(15), tables.PartName(44), tables.PartName(58)));
        Assert.Equal("", tables.NameOf(25, 44, 58));     // a cockpit in the body's place: no name, never a guess
        Assert.Equal("", tables.NameOf(15, 58, 44));
        Assert.Equal("", tables.PartName(75));
        Assert.Equal(41, Pilots.Names.Count);
        Assert.Equal(("Captain Falcon", "Black Shadow", "Phoenix"), (Pilots.NameOf(6), Pilots.NameOf(29), Pilots.NameOf(40)));
    }

    /// <summary>The tables are read from the main module's places, and bytes that are not them are no tables.</summary>
    [Fact]
    public void TheTablesAreReadFromTheMainModulesPlaces()
    {
        var module = new byte[CustomPartFile.PartsAt + 0x2000];
        for (var i = 0; i < 75; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(module.AsSpan(CustomPartFile.FlagsAt + (i * 4)), Flags.GetValueOrDefault(i));
        }
        var keys = Enumerable.Range(0, CustomPartFile.Listed).Select(i => i == 40 ? 0x3868 : i < 40 ? i : 0x4000 + i).ToArray();
        for (var i = 0; i < keys.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(module.AsSpan(CustomPartFile.ListedKeysAt + (i * 4)), keys[i]);
        }
        var at = CustomPartFile.PartsAt;
        foreach (var s in Enumerable.Range(0, 75).SelectMany(i => new[] { PartString(i), "jp" }))
        {
            at = Put(module, at, s);
        }
        at = CustomPartFile.ListedAt;
        for (var i = 0; i < keys.Length; i++)
        {
            at = Put(module, at, i == 40 ? "Beat The Beast" : $"Listed {i}");
            at = i == 0 ? Put(module, at, "") : at;
            at = Put(module, at, "jp");
        }
        var tables = CustomPartFile.Tables(module);
        Assert.NotNull(tables);
        Assert.Equal("Gallant Star-G4", tables.NameOf(15, 44, 58));
        Assert.Equal("Beat The Beast", tables.NameOf(14, 28, 58));
        Assert.Equal("Listed 0", tables.NameOf(0, 25, 50));   // key 0
        Assert.Null(CustomPartFile.Tables(module.AsSpan(0, CustomPartFile.FlagsAt)));
        BinaryPrimitives.WriteInt32BigEndian(module.AsSpan(CustomPartFile.ListedKeysAt + 4), -5);   // out of order
        Assert.Null(CustomPartFile.Tables(module));
    }

    private static int Put(byte[] module, int at, string s)
    {
        Encoding.Latin1.GetBytes(s).CopyTo(module, at);
        return (at + s.Length + 1 + 3) & ~3;
    }

    /// <summary>The sum as the game makes it: most values the three parts' sum in the game's order, the grips 1 and 3
    /// the body's, the body value body times cockpit, the cameras the cockpit's, and the boost the sum times a
    /// factor of the summed weight that is 1 at 1200 kg (the slopes .rodata 0xCCCC above, 0xCCD0 below).</summary>
    [Fact]
    public void TheStatsAreSummedAsTheGameSumsThem()
    {
        var car = new byte[0x549A];
        void Part(int id, float weight, float boost, float grip1, float body, float camera)
        {
            var at = (id < 25 ? 0x1F04 : id < 50 ? 0x3098 : 0x422C) + ((id % 25) * 0xB4);
            BinaryPrimitives.WriteUInt32LittleEndian(car.AsSpan(at), 0x238053C0);   // every part's name word: one string
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x04), weight);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x08), 0.1f * (1 + (id / 25)));
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x10), grip1);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x34), boost);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x44), body);
            BinaryPrimitives.WriteSingleLittleEndian(car.AsSpan(at + 0x4C), camera);
        }
        Part(3, 1600, 0, 0.9f, 1.2f, 0);
        Part(44, 680, 0, 0.5f, 0.9f, 1.0f);
        Part(60, 860, 21, 0, 0, 0);
        var heavy = CustomPartFile.Sum(car, 3, 44, 60)!;
        Assert.Equal(3140f, heavy[(int)MachineStat.Weight]);
        Assert.Equal((0.1f + 0.2f) + 0.3f, heavy[(int)MachineStat.Acceleration]);
        Assert.Equal(0.9f, heavy[(int)MachineStat.Grip1]);
        Assert.Equal(1.2f * 0.9f, heavy[(int)MachineStat.Body]);
        Assert.Equal(1.0f, heavy[(int)MachineStat.CameraReorienting]);
        var above = BitConverter.Int32BitsToSingle(0x39690453);
        Assert.Equal((((3140f - 1200f) * above) + 1f) * 21f, heavy[(int)MachineStat.BoostStrength]);
        Part(3, 300, 0, 0.9f, 1.2f, 0);
        Part(44, 200, 0, 0.5f, 0.9f, 1.0f);
        Part(60, 300, 10, 0, 0, 0);
        var below = BitConverter.Int32BitsToSingle(0x3960B1E3);
        Assert.Equal((((800f - 1200f) * below) + 1f) * 10f, CustomPartFile.Sum(car, 3, 44, 60)![(int)MachineStat.BoostStrength]);
        Assert.Null(CustomPartFile.Sum(car, 44, 3, 60));                 // a part not of its kind
        Assert.Null(CustomPartFile.Sum(car.AsSpan(0, 0x4000), 3, 44, 60));   // no table
    }

    /// <summary>The saved machines and the one raced go through the settings file and back; one the game cannot
    /// build is dropped on the way in, and a change of either is a change of the racer.</summary>
    [Fact]
    public void TheSavedMachinesGoThroughTheSettingsFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        var painted = new CustomBuild(15, 44, 58, 29).WithColour(1, new PartColour(null, new Rgb(10, 200, 30)));
        var plain = new CustomBuild(0, 27, 50, 6);
        var racer = RacerProfile.Default with { Customs = [painted, plain], Custom = plain.AsRaced(NoColourWords.Instance) };
        var store = new SettingsStore(path);
        store.Save(new LauncherSettings { Racer = racer });

        var back = new SettingsStore(path).Load().Racer;

        Assert.Equal(racer, back);
        Assert.Equal([painted, plain], back.Customs);
        Assert.Equal(new CustomMachine(0, 27, 50, 6), back.Custom);
        Assert.Equal(new PartColour(null, new Rgb(10, 200, 30)), back.Customs![0].ColourOf(1));
        Assert.NotEqual(racer, racer with { Custom = null });
        Assert.NotEqual(racer, racer with { Customs = [plain] });

        var text = File.ReadAllText(path).Replace("\"booster\": 50", "\"booster\": 12", StringComparison.Ordinal);
        File.WriteAllText(path, text);
        var repaired = new SettingsStore(path).Load().Racer;
        Assert.Equal([painted], repaired.Customs);   // the machine with a body in the booster's place is gone
        Assert.Null(repaired.Custom);                // and so is the raced one, which no game could build
    }

    /// <summary>A custom machine is its three parts in one model: each moved by its point, each mesh naming its
    /// part, and the second part's own textures numbered on after the first's.</summary>
    [Fact]
    public void ThePartsAreAssembledByTheirPoints()
    {
        var own = GarageFiles.Tpl(GarageFiles.Cmpr(8, 8, (_, _) => new Rgb(200, 0, 0)));
        var part = MachineModel.Read(GarageFiles.Gma(0, 7), own, null);
        var whole = MachineModel.Assemble([(part, System.Numerics.Vector3.Zero), (part, new System.Numerics.Vector3(0, 1, 2))]);
        Assert.Equal(4, whole.Meshes.Count);
        Assert.Equal([0, 0, 1, 1], whole.Meshes.Select(m => m.Material.Part));
        Assert.Equal(part.Meshes[0].Positions[0] + new System.Numerics.Vector3(0, 1, 2), whole.Meshes[2].Positions[0]);
        Assert.Equal(part.Textures.Count + part.Meshes[0].Material.Texture, whole.Meshes[2].Material.Texture);
        Assert.Equal(-1, whole.Meshes[3].Material.Texture);   // none stays none
        Assert.Empty(MachineModel.Read(GarageFiles.Gma(0, 7), [], null).Textures);   // a part has no texture file of its own
    }

    /// <summary>Until the colour model is read, no part's colour reaches a race: every part races in its own.</summary>
    [Fact]
    public void ColoursReachNoRaceUntilTheirModelIsKnown()
    {
        var build = CustomBuild.First.WithColour(0, new PartColour(null, new Rgb(1, 2, 3)));
        Assert.Null(build.AsRaced(NoColourWords.Instance).Colors);
        Assert.Null(NoColourWords.Instance.StockColours(0));
        Assert.True(build.Sound);
        // the game's rule: a paint is its RGB over slot 1, stock 0 is 0, a stock colour its word on slot 1
        var words = new PartColourWords(part => part == 58 ? [0xFA323201, 0x11223301, 0x44556602, 0x77889901] : null);
        Assert.Equal(0x01020301u, words.Word(15, new PartColour(null, new Rgb(1, 2, 3))));
        Assert.Equal(0u, words.Word(15, PartColour.Own));
        Assert.Equal(0x44556601u, words.Word(58, new PartColour(2)));
        Assert.Null(words.Word(15, new PartColour(2)));   // its stock colours not known
        Assert.Equal(new Rgb(0x11, 0x22, 0x33), words.StockColours(58)![1]);
        var raced = new CustomBuild(15, 44, 58, 29).WithColour(0, new PartColour(null, new Rgb(1, 2, 3))).AsRaced(words);
        Assert.Equal([0x01020301u, 0u, 0u], raced.Colors);
        Assert.False((build with { Pilot = 41 }).Sound);
    }
}
