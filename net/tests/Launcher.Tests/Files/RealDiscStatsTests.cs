using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The one test here that reads the game itself: the machines' stats from your own disc image, where this
/// computer has it, and skipped where it does not. Every other test of the reader makes its files up, and so holds
/// the reader only to the test's own idea of the layout: where the letters stand in the main module, the main
/// module's cipher, the places in a machine's block. This one holds them to the game, by what its own machine select
/// shows of three machines (the first, the Blue Falcon and the last): things anybody with the game can read off
/// the screen, written here as such. Nothing is taken from the disc into the repository.
/// </summary>
public sealed class RealDiscStatsTests
{
    /// <summary>Your disc image as the runner names it for its own tools: the setting <c>DISC_DEFAULT</c>, a row of
    /// <c>~/.config/piconet/pins.tsv</c> over the runner's <c>port/setup/pins.tsv</c>, whose row is empty (no default;
    /// net/tools/DiscPinsWriter reads the same row), the file the build names
    /// (<c>AssemblyMetadata</c> "RunnerPins", from <c>RunnerPins</c> in net/Directory.Build.props: this repository's
    /// own today, the runner's checkout beside it after the split). A named table that is not there fails: a disc test
    /// is never skipped because the runner's checkout is missing, only because this computer has no disc
    /// (<c>-p:RunnerPins=</c>, named empty on purpose, skips them, and the reason says so). A PC's own row, in
    /// <c>~/.config/piconet/pins.tsv</c> outside every checkout, is read over the table's as the runner's lib.sh reads
    /// it. Null when neither names one.</summary>
    internal static string? PlayersDisc()
    {
        var pins = typeof(RealDiscStatsTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "RunnerPins").Value;
        Assert.SkipWhen(string.IsNullOrEmpty(pins), "the build names no runner checkout (RunnerPins is empty): no disc is looked for");
        Assert.True(File.Exists(pins), $"the runner's pins table is not there ({pins}): RunnerPins in net/Directory.Build.props names its checkout");
        static string? Row(string table) => File.ReadLines(table).Select(line => line.Split('\t'))
            .FirstOrDefault(cells => cells.Length >= 2 && cells[0] == "DISC_DEFAULT" && cells[1].Length > 0)?[1];
        var own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "piconet", "pins.tsv");
        return (File.Exists(own) ? Row(own) : null) ?? Row(pins!);
    }

    [Theory]
    [InlineData(0, "Red Gazelle", "E", "A", "C", "1330 kg")]
    [InlineData(6, "Blue Falcon", "B", "C", "B", "1260 kg")]
    [InlineData(40, "Rainbow Phoenix", "B", "B", "C", "1080 kg")]
    public void TheRealDiscGivesWhatTheGamesMachineSelectShows(int machine, string name, string body, string boost, string grip, string weight)
    {
        var image = PlayersDisc();
        Assert.SkipWhen(image is null || !File.Exists(image), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(image));
        using var art = DiscArt.Open(image);
        Assert.NotNull(art);

        var table = art.MachineStats();                                        // with the real pins: the letters too

        Assert.NotNull(table);
        Assert.Equal(name, Machines.Names[machine]);
        Assert.Equal((body, boost, grip, weight),
            (table.Text(machine, MachineStat.BodyRating), table.Text(machine, MachineStat.BoostRating),
                table.Text(machine, MachineStat.GripRating), table.Text(machine, MachineStat.Weight)));
        // and every machine has every value: no place of the layout ran off the table
        Assert.All(Enumerable.Range(0, 41), m => Assert.All(MachineStats.All, s => Assert.NotNull(table.Value(m, s.Stat))));
    }

    /// <summary>A parts machine's letters from your disc, as main.rel's <c>fn_1_1373B0</c> was read to give them:
    /// body from the body, boost from the booster, grip from the cockpit. The cases are the
    /// reading's own worked examples; the game's screen was not compared.</summary>
    [Theory]
    [InlineData(0, 25, 50, "D", "E", "D")]
    [InlineData(3, 27, 56, "A", "A", "A")]
    [InlineData(23, 36, 60, "E", "A", "A")]
    public void APartsMachinesLettersComeFromItsParts(int body, int cockpit, int booster, string bodyLetter, string boostLetter, string gripLetter)
    {
        var image = PlayersDisc();
        Assert.SkipWhen(image is null || !File.Exists(image), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(image);
        var letters = art!.CustomGrades();
        Assert.NotNull(letters);
        var row = new float?[MachineStats.All.Count];
        CustomPartFile.Rate(row, letters, body, cockpit, booster);
        Assert.Equal((bodyLetter, boostLetter, gripLetter), (MachineStats.Text(MachineStat.BodyRating, row[(int)MachineStat.BodyRating]),
            MachineStats.Text(MachineStat.BoostRating, row[(int)MachineStat.BoostRating]), MachineStats.Text(MachineStat.GripRating, row[(int)MachineStat.GripRating])));
    }

    /// <summary>Chapter 8's card takes the story mode's own picture of its place: read from
    /// bmp_e/sstg, as wide as the cup venues' and not Fire Field's.</summary>
    [Fact]
    public void ChapterEightsCardHasTheStorysOwnPicture()
    {
        var image = PlayersDisc();
        Assert.SkipWhen(image is null || !File.Exists(image), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(image);
        Assert.NotNull(art);

        var chapter8 = art.Venue(Online.Protocol.CourseNames.FirstStoryStage + 7);
        var fireField = art.Venue(17);

        Assert.NotNull(chapter8);
        Assert.NotNull(fireField);
        Assert.Equal((fireField.Width, fireField.Height), (chapter8.Width, chapter8.Height));
        Assert.False(chapter8.Rgba.AsSpan().SequenceEqual(fireField.Rgba));
    }
}
