using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The practice last started, kept in the settings file (Play's Practice opens on it): saved and read back
/// whole; a value that is none of a practice's costs that setting alone; numbers outside their ranges are kept to
/// them.</summary>
public sealed class PracticeSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsFile => Path.Combine(_dir, "settings.json");

    [Fact]
    public void ThePracticeIsSavedAndReadBack()
    {
        var practice = new PracticeSetup(13, 7, "GX Unleashed", 60, 12, Restore: true, BoostFromStart: true, MineRegenMs: 1250);
        var mine = new LauncherSettings { DiscPath = "C:/gx.iso", Practice = practice };
        new SettingsStore(SettingsFile).Save(mine);
        Assert.Contains("\"mineRegenMs\": 1250", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        Assert.DoesNotContain("\"rules\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);

        var store = new SettingsStore(SettingsFile);
        Assert.Equal(mine, store.Load());
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        // none saved: none read (the page then opens on Quick Play's rules)
        new SettingsStore(SettingsFile).Save(new LauncherSettings { DiscPath = "C:/gx.iso" });
        Assert.DoesNotContain("practice", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
        Assert.Null(new SettingsStore(SettingsFile).Load().Practice);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"laps\"")]
    [InlineData("[1, 2]")]
    [InlineData("{\"course\": \"x\"}")]
    [InlineData("{\"restore\": 1}")]
    public void APracticeOfAnotherKindCostsThatSettingAlone(string practice)
    {
        File.WriteAllText(SettingsFile, $$"""{"discPath": "C:/gx.iso", "practice": {{practice}}}""");
        var store = new SettingsStore(SettingsFile);

        var read = store.Load();

        Assert.Null(read.Practice);
        Assert.Equal("C:/gx.iso", read.DiscPath);
        Assert.False(read.NoServer);
    }

    [Fact]
    public void ItsNumbersAreKeptInsideTheirRanges()
    {
        File.WriteAllText(SettingsFile, """
            {"practice": {"course": 999, "machine": 77, "statPack": "", "machineSetting": 300, "laps": 400,
                          "mineRegenMs": 230}}
            """);
        var store = new SettingsStore(SettingsFile);

        var read = store.Load().Practice;

        Assert.Equal(new PracticeSetup(PracticeSetup.Default.Course, 40, null, 100, PracticeSetup.MostLaps, MineRegenMs: 250), read);
        Assert.Equal(SettingsRead.Repaired, store.LastRead);
        // a practice without the rules' keys (written by hand): no boost, no Restore, the mines stay gone
        File.WriteAllText(SettingsFile, """{"practice": {"course": 8, "machine": 1, "machineSetting": 100, "laps": 3}}""");
        Assert.Equal(new PracticeSetup(8, 1, null, 100, 3), new SettingsStore(SettingsFile).Load().Practice);
    }
}
