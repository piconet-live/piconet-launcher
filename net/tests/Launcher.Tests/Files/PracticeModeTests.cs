using System.Text.Json.Nodes;
using Launcher.App.ViewModels;
using Launcher.Core;
using Online.Protocol;
using Xunit;

namespace Launcher.Tests.Files;

/// <summary>Practice as a game mode: the file beside the program, its parameters the page's rows and the match's
/// <c>setup.mode.params</c> by their names, a parameter added to the file a row more with no launcher change, a rule the
/// file states the mode's, and settings saved before the rows were the mode's read into its parameters.</summary>
public sealed class PracticeModeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("practice-mode-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "quickplay");

    private static ModePackage Shipped() => PracticeMode.Read(Folder, out var problem) ?? throw new InvalidOperationException(problem);

    private static JsonNode Setup(PracticeSetup setup, ModePackage mode) =>
        JsonNode.Parse(PracticeMatch.Build(setup, "P", null, DateTimeOffset.UnixEpoch, mode: mode))!["match"]!["setup"]!;

    private static int Index(ModePackage mode, string name) => mode.Params.Select(p => p.Name).ToList().IndexOf(name);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void TheMatchCarriesTheModeAndItsParametersByName(bool restore, int value)
    {
        var mode = Shipped();
        var setup = PracticeSetup.Default with { Params = new() { ["restore_on"] = restore ? 1 : 0, ["lap_count"] = 3 } };
        var file = JsonNode.Parse(PracticeMatch.Build(setup, "P", null, DateTimeOffset.UnixEpoch, mode: mode))!;
        var set = file["match"]!["setup"]!;
        Assert.True((bool)file["practice"]!);
        Assert.Equal(3, (int)set["laps"]!);   // (the mode's laps word on its parameter)
        Assert.Equal(value, (int)set["mode"]!["params"]![Index(mode, "restore_on")]!);
        Assert.Equal(3, (int)set["mode"]!["params"]![Index(mode, "lap_count")]!);
        Assert.Equal(mode.Start().Hash, (string)set["mode"]!["hash"]!);
        Assert.False(set["rules"]?["restore"] is { } r && (bool)r);
        // none named: the parameters' defaults, laps 0 (unlimited); a value outside a parameter's range: its default
        Assert.Equal(0, (int)Setup(PracticeSetup.Default, mode)["laps"]!);
        Assert.Equal(0, (int)Setup(PracticeSetup.Default with { Params = new() { ["lap_count"] = 400 } }, mode)["laps"]!);
    }

    [Fact]
    public void AMissingFileIsAProblemNotAPlainPractice()
    {
        Assert.Null(PracticeMode.Read(Path.Combine(Folder, "nowhere"), out var problem));
        Assert.NotNull(problem);
        File.WriteAllText(Path.Combine(_dir, PracticeMode.FileName), "mode \"Broken\" {");
        Assert.Null(PracticeMode.Read(_dir, out problem));
        Assert.Equal("Practice mode file has an error", problem);
    }

    /// <summary>A copy of the shipped file with a parameter more, one that sets no rule, and one that sets the mines: the
    /// page shows both as rows (the lobby's rows for them), the mines' race-rules row goes, and the values set on the
    /// rows reach the match's mode and its rules.</summary>
    [Fact]
    public void AParameterAddedToTheFileIsARowAndReachesTheModeStart()
    {
        var text = File.ReadAllText(Path.Combine(Folder, PracticeMode.FileName)) + """

            param wobble "Wobble" number 1..9 default 4;
            param mines_back "Mines respawn" number 50..5000 step 50 or "Off" default 300;
            mine_regen mines_back;
            """;
        File.WriteAllText(Path.Combine(_dir, PracticeMode.FileName), text);
        var mode = PracticeMode.Read(_dir, out var problem) ?? throw new InvalidOperationException(problem);
        var page = new PracticeViewModel(() => new RacedMachine(0, null), () => { }, [], mode);

        var practice = page.Groups.Single(g => g.Heading == PracticeViewModel.ModeHeading);
        Assert.Equal([.. mode.Params.Select(p => p.Label), "Rewind", "Save · Load"], practice.Rows.Select(r => r.Label));
        Assert.Contains("Wobble", practice.Rows.Select(r => r.Label));
        var rules = page.Groups.Single(g => g.Heading == PracticeViewModel.RulesHeading);
        Assert.Equal([HostRules.BoostStart.Label], rules.Rows.Select(r => r.Label));   // (the mines are the mode's now)

        var wobble = Assert.IsType<StepperRule>(page.Param("wobble"));
        Assert.Equal(4, wobble.Value);
        wobble.Value = 7;
        var mines = Assert.IsType<NamedNumberRule>(page.Param("mines_back"));
        mines.Value = 450;
        Assert.Equal(7, page.Setup.Params!["wobble"]);

        var set = Setup(page.Setup, mode);
        Assert.Equal(7, (int)set["mode"]!["params"]![Index(mode, "wobble")]!);
        Assert.Equal(450, (int)set["mode"]!["params"]![Index(mode, "mines_back")]!);
        Assert.Equal(450, (int)set["rules"]!["mineRegenMs"]!);   // (the mode's word over the set-up's 200)
        // Off: the word as if not written, the mines stay gone
        mines.IsNamed = true;
        Assert.Equal(0, (int)Setup(page.Setup, mode)["rules"]!["mineRegenMs"]!);
        // the page made again on the set-up opens on its values
        var again = new PracticeViewModel(() => new RacedMachine(0, null), () => { }, [], mode, last: page.Setup);
        Assert.Equal(7, ((StepperRule)again.Param("wobble")).Value);
    }

    [Fact]
    public void SettingsSavedBeforeTheRowsWereTheModesKeepTheirLapsAndRestore()
    {
        var mode = Shipped();
        var old = PracticeSetup.Default with { OldLaps = 12, OldRestore = true };
        var values = PracticeMode.Values(mode, old);
        Assert.Equal(12, values[Index(mode, "lap_count")]);
        Assert.Equal(1, values[Index(mode, "restore_on")]);
        // a value saved by the parameter's name wins
        Assert.Equal(5, PracticeMode.Values(mode, old with { Params = new() { ["lap_count"] = 5 } })[Index(mode, "lap_count")]);
        // the page opens on them, and the next start saves them by name only
        var page = new PracticeViewModel(() => new RacedMachine(0, null), () => { }, [], mode, last: old);
        Assert.Equal(12, ((LapsRule)page.Param("lap_count")).Laps);
        Assert.True(((SwitchRule)page.Param("restore_on")).On);
        Assert.Null(page.Setup.OldLaps);
        Assert.Equal(12, page.Setup.Params!["lap_count"]);
    }
}
