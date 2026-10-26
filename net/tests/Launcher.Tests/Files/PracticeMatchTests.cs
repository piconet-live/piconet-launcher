using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests.Files;

/// <summary>
/// A practice's match file (Play's Practice, START PRACTICE): one seat, the player's, no relay, the game's key
/// "practice"; and the launch file the race's start makes of it (the game's reader takes it: port/configs/practice.json
/// is the same shape, read by port/core/tests/test_launch.cpp).
/// </summary>
public sealed class PracticeMatchTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 18, 9, 30, 5, TimeSpan.Zero);

    [Fact]
    public void APracticeIsOneSeatWithNoRelay()
    {
        var file = JsonNode.Parse(PracticeMatch.Build(new PracticeSetup(8, 12, null, 30, 2), "PILOT", null, At))!.AsObject();
        Assert.Equal(["match", "practice"], file.Select(m => m.Key));
        Assert.True((bool)file["practice"]!);
        var match = file["match"]!.AsObject();
        Assert.Equal("practice-20261018-093005", (string?)match["matchId"]);
        Assert.Null(match["relay"]);
        Assert.Null(match["token"]);
        var setup = match["setup"]!.AsObject();
        Assert.Equal(8, (int)setup["course"]!);
        Assert.Equal(2, (int)setup["laps"]!);
        var seat = Assert.Single(setup["seats"]!.AsArray())!.AsObject();
        Assert.Equal(0, (int)seat["seat"]!);
        Assert.Equal(12, (int)seat["machine"]!);
        Assert.Equal(30, (int)seat["setting"]!);
        Assert.Equal("PILOT", (string?)seat["name"]);
        Assert.False(string.IsNullOrEmpty((string?)seat["playerId"]));
        Assert.Null(seat["stockColor"]);
        Assert.Null(seat["color"]);
    }

    [Fact]
    public void TheValuesStayInsideTheGamesRangesAndTheLookComesAlong()
    {
        var stock = JsonNode.Parse(PracticeMatch.Build(new PracticeSetup(999, 77, null, 300, 400), "P", new MachineLook(40, 2), At))!;
        var setup = stock["match"]!["setup"]!;
        Assert.Equal(PracticeSetup.Default.Course, (int)setup["course"]!);
        Assert.Equal(PracticeSetup.MostLaps, (int)setup["laps"]!);
        Assert.Equal(40, (int)setup["seats"]![0]!["machine"]!);
        Assert.Equal(100, (int)setup["seats"]![0]!["setting"]!);
        Assert.Equal(2, (int)setup["seats"]![0]!["stockColor"]!);
        var painted = JsonNode.Parse(PracticeMatch.Build(PracticeSetup.Default, "P", new MachineLook(0, null, new Rgb(1, 2, 3)), At))!;
        var color = painted["match"]!["setup"]!["seats"]![0]!["color"]!;
        Assert.Equal([1, 2, 3], new[] { (int)color["r"]!, (int)color["g"]!, (int)color["b"]! });
    }

    [Fact]
    public void TheRulesAreTheMatchStartsAndUnlimitedLapsAreLapsZero()
    {
        // Quick Play's rules unless the player changes them: mines come back after 200 ms, no Restore, no boost on
        // the first lap; in the match start's form (RaceRules), which the game's RulesIntoRace reads
        var plain = JsonNode.Parse(PracticeMatch.Build(PracticeSetup.Default, "P", null, At))!["match"]!["setup"]!;
        var rules = plain["rules"]!.AsObject();
        Assert.False((bool)rules["restore"]!);
        Assert.False((bool)rules["boostFromStart"]!);
        Assert.Equal(200, (int)rules["mineRegenMs"]!);
        Assert.Equal(0, (int)rules["finishLimitSeconds"]!);
        Assert.Equal(new RaceRules(MineRegenMs: 200), rules.Deserialize<RaceRules>(ControlProtocol.Json));
        var set = JsonNode.Parse(PracticeMatch.Build(PracticeSetup.Default with
        {
            Restore = true, BoostFromStart = true, MineRegenMs = 1250, Laps = PracticeSetup.Unlimited,
        }, "P", null, At))!["match"]!["setup"]!;
        Assert.Equal(new RaceRules(BoostFromStart: true, Restore: true, MineRegenMs: 1250), set["rules"].Deserialize<RaceRules>(ControlProtocol.Json));
        Assert.Equal(0, (int)set["laps"]!);
        // Off: the mines stay gone, the game's own way; a delay off its steps is the nearest step
        Assert.Equal(0, (int)Rules(PracticeSetup.Default with { MineRegenMs = 0 })["mineRegenMs"]!);
        Assert.Equal(250, (int)Rules(PracticeSetup.Default with { MineRegenMs = 230 })["mineRegenMs"]!);
        Assert.Equal(5000, (int)Rules(PracticeSetup.Default with { MineRegenMs = 99999 })["mineRegenMs"]!);
        Assert.Equal(0, (int)Rules(PracticeSetup.Default with { MineRegenMs = 20 })["mineRegenMs"]!);
    }

    private static JsonNode Rules(PracticeSetup setup) =>
        JsonNode.Parse(PracticeMatch.Build(setup, "P", null, At))!["match"]!["setup"]!["rules"]!;

    [Fact]
    public void ATypedNumberIsDigitsKeptInsideTheirRange()
    {
        Assert.Equal(12, TypedNumber.Whole("12", 1, 99));
        Assert.Equal(99, TypedNumber.Whole("250", 1, 99));
        Assert.Equal(99, TypedNumber.Whole("99999999999999", 1, 99));
        Assert.Equal(1, TypedNumber.Whole("0", 1, 99));
        Assert.Null(TypedNumber.Whole("-3", 1, 99));
        Assert.Null(TypedNumber.Whole(" 4", 1, 99));
        Assert.Null(TypedNumber.Whole("x", 1, 99));
        Assert.Null(TypedNumber.Whole("", 1, 99));
        Assert.Null(TypedNumber.Whole(null, 1, 99));
    }

    [Fact]
    public void ItsRaceStartRecordsAndCarriesThePracticeKey()
    {
        var text = PracticeMatch.Build(PracticeSetup.Default, "P", null, At);
        var launch = JsonNode.Parse(GameLaunchFile.Race("b", text, record: "practice.fzrp"))!.AsObject();
        Assert.Equal(["match", "practice", "record"], launch["match"]!.AsObject().Select(m => m.Key).Order());
        Assert.True((bool)launch["match"]!["practice"]!);
        Assert.Equal("practice.fzrp", (string?)launch["match"]!["record"]);
    }

    [Fact]
    public void TheLaunchersPacksAreTheRepositorysAndAChosenOneTravelsWhole()
    {
        // the packs beside the program: the build copies the repository's protocol/packs there
        var packs = PracticePacks.Read(PracticePacks.Folder);
        var unleashed = Assert.Single(packs, p => p.Name == "GX Unleashed");
        var file = JsonNode.Parse(PracticeMatch.Build(PracticeSetup.Default with { StatPack = unleashed.Name }, "P", null, At, unleashed))!;
        var stats = file["match"]!["setup"]!["stats"]!.AsObject();
        Assert.Equal(["fingerprint", "name", "values"], stats.Select(m => m.Key).Order());
        var back = StatPack.FromWire(new StatPackWire((string)stats["name"]!, (string)stats["fingerprint"]!, (string)stats["values"]!));
        Assert.Equal(unleashed.Fingerprint, Assert.IsType<StatPack>(back.Pack).Fingerprint);
        Assert.Null(JsonNode.Parse(PracticeMatch.Build(PracticeSetup.Default, "P", null, At))!["match"]!["setup"]!["stats"]);
    }

    [Fact]
    public void AFolderOfPacksLeavesOutWhatIsNoPack()
    {
        var folder = Directory.CreateTempSubdirectory("practice-packs-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "broken" + StatPackText.Extension), "fzstats 1\nname Broken\n");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a pack");
            Assert.Empty(PracticePacks.Read(folder));
            Assert.Empty(PracticePacks.Read(Path.Combine(folder, "missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
