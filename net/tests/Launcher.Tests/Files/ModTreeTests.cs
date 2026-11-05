using System.IO.Compression;
using Launcher.Core;
using TestSupport;

namespace Launcher.Tests;

/// <summary>
/// A mod's parts as a tree (<see cref="ModTree"/>), on the listings of two real mods as they ship (ModListings: each
/// file's size and path in GXtreme V5's and GX Unleashed 5.0.4's archives, nothing of their contents): the rows are
/// the mod's own folders down to the parts, a part's "root" folder no row; the ticks a first look gives
/// (<see cref="ModImport.Defaults"/>, unchanged); Explorer's order; lettered alternatives one of a kind.
/// </summary>
public sealed class ModTreeTests
{
    /// <summary>The top folders and files of the disc's <c>files/</c> (GFZE01), one made-up file in each folder.</summary>
    internal static Dictionary<string, string> Disc()
    {
        string[] folders = ["bg", "bmp_e", "cam", "chara", "dlg", "emblem", "enemy_line", "font", "game", "init", "jpeg", "lip", "memcard",
            "motion", "movie", "snd", "staff_ghost", "stage", "story_ghost", "test_model", "vehicle", "vehicle_parts"];
        string[] files = ["fze.car_colchg.rel", "fze.customize.rel", "fze.sample.rel", "fze.sel.rel", "fze.str", "opening.bnr"];
        var disc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in folders.Select(f => f + "/x.bin").Concat(files))
        {
            disc[path] = path;
        }
        return disc;
    }

    internal static List<ModEntry> Listing(string file)
    {
        var path = RepositoryFolder.Of("net", "tests", "Launcher.Tests", "Files", "ModListings", file);
        using var stream = File.OpenRead(path);
        using var text = new StreamReader(file.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(stream, CompressionMode.Decompress) : stream);
        var entries = new List<ModEntry>();
        while (text.ReadLine() is { Length: > 0 } line)
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            entries.Add(new ModEntry(line[(tab + 1)..], long.Parse(line[..tab], System.Globalization.CultureInfo.InvariantCulture)));
        }
        return entries;
    }

    private static ModSurvey Survey(string source, string listing)
    {
        var (survey, refusal) = ModImport.Survey(source, Listing(listing), Disc());
        Assert.True(survey is not null, refusal);
        return survey;
    }

    /// <summary>The rows shown fully open, each as its depth in dots and its name, and its tick.</summary>
    private static List<string> Rows(ModTree tree, IReadOnlySet<string> on) =>
        [.. tree.Rows.Select(n => new string('.', n.Depth) + n.Name + " " + ModTree.TickOf(n, on))];

    [Fact]
    public void GXtremesTreeIsItsThreeFoldersInExplorersOrder()
    {
        var survey = Survey("gxtreme_v5.zip", "gxtreme_v5.tsv");
        var tree = survey.Tree;
        Assert.Equal(["_XTREMEv5/root", "Japanese fixes", "Story Course Replacements/root"], tree.Parts);
        // the first look's ticks, as before the tree: only the Japanese fixes are off
        Assert.Equal(["Gxtreme V5 Some", "._XTREMEv5 On", ".Japanese fixes Off", ".Story Course Replacements On"], Rows(tree, survey.OnByDefault));
        Assert.All(tree.Top.Children, c => Assert.Null(c.Group));
        // the game's own folders inside a part are no rows
        Assert.All(tree.Top.Children, c => Assert.Empty(c.Children));
    }

    [Fact]
    public void UnleashedsTreeIsItsVersionsAndStepsDownToTheParts()
    {
        var survey = Survey("f-zero_gx_unleashed_504_all_versions_.zip", "unleashed_504.tsv.gz");
        var tree = survey.Tree;
        var on = survey.OnByDefault;
        if (Environment.GetEnvironmentVariable("MOD_TREE_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllLines(dump, Rows(tree, on).Concat(tree.Parts));
        }
        Assert.Equal(49, tree.Parts.Count);
        var top = tree.Top.Children.Select(c => c.Name).ToList();
        Assert.Equal(["1 - Required files (applicable for all versions of mod)", "2A - Standard Unleashed", "2B - Legends Unleashed"], top);
        var standard = tree.Top.Children[1];
        Assert.Equal(["1 (Optional) - Boost Fire Recolor and Machine Textures", "2A - Standard Unleashed", "2B - Standard Unleashed and Story Courses"],
            standard.Children.Select(c => c.Name));
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 1B - Jank machine camera removed", "Step 2A - Normal lvl Staff Ghosts",
            "Step 2B - Expert lvl Ghosts, with Side Attacks", "Step 2C - Expert lvl Ghosts, NO Side Attacks", "Step 2D - Master lvl Ghosts, with Side Attacks",
            "Step 2E - Master lvl Ghosts, NO Side Attacks", "Step Final - Required files"], standard.Children[1].Children.Select(c => c.Name));
        // the alternatives: the two versions, the two ways of each version, each step's letters
        Assert.Equal("2", tree.Top.Children[1].Group);
        Assert.Equal("2", tree.Top.Children[2].Group);
        Assert.Null(tree.Top.Children[0].Group);
        Assert.Equal(["1", "1", "2", "2", "2", "2", "2", null], standard.Children[1].Children.Select(c => c.Group));

        // the first look's ticks: the required files, Standard's first way with its first steps; no option, no Legends
        Assert.Equal(ModTick.On, ModTree.TickOf(tree.Top.Children[0], on));
        Assert.Equal(ModTick.Some, ModTree.TickOf(standard, on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard.Children[0], on));
        Assert.Equal(ModTick.On, ModTree.TickOf(standard.Children[1], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard.Children[2], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(tree.Top.Children[2], on));
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 2A - Normal lvl Staff Ghosts", "Step Final - Required files"],
            standard.Children[1].Children.Where(c => ModTree.TickOf(c, on) == ModTick.On).Select(c => c.Name));
    }

    [Fact]
    public void TickingAnAlternativeUnticksItsSiblingsAndTheTopTicksOneOfEach()
    {
        var survey = Survey("f-zero_gx_unleashed_504_all_versions_.zip", "unleashed_504.tsv.gz");
        var tree = survey.Tree;
        var (standard, legends) = (tree.Top.Children[1], tree.Top.Children[2]);
        // Legends: Standard goes, all of Legends' one way comes (its first way, its steps' first letters)
        var on = ModTree.Press(legends, survey.OnByDefault);
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard, on));
        Assert.Equal(ModTick.On, ModTree.TickOf(legends, on));
        Assert.Equal(ModTick.On, ModTree.TickOf(legends.Children[1], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(legends.Children[2], on));
        var steps = legends.Children[1].Children;
        Assert.Equal(ModTick.On, ModTree.TickOf(steps[0], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(steps[1], on));
        Assert.Equal(ModTick.On, ModTree.TickOf(steps[2], on));
        Assert.All(steps.Skip(3).Take(4), s => Assert.Equal(ModTick.Off, ModTree.TickOf(s, on)));

        // a step's other letter: the first goes
        var expert = steps.Single(s => s.Name.StartsWith("Step 2C", StringComparison.Ordinal));
        on = ModTree.Press(expert, on);
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 2C - Expert lvl Ghosts, NO Side Attacks", "Step 3 - Pilot themes",
            "Step Final - Required files"], steps.Where(s => ModTree.TickOf(s, on) == ModTick.On).Select(s => s.Name));
        Assert.Equal(ModTick.On, ModTree.TickOf(legends.Children[1], on));

        // one of each set of alternatives is all of it
        Assert.Equal(ModTick.On, ModTree.TickOf(tree.Top, on));

        // the top row from the first look's ticks: all of it, in each set of alternatives the one ticked
        var all = ModTree.Press(tree.Top, survey.OnByDefault);
        Assert.Equal(ModTick.On, ModTree.TickOf(tree.Top, all));
        Assert.Equal(ModTick.On, ModTree.TickOf(standard, all));
        Assert.Equal(ModTick.Off, ModTree.TickOf(legends, all));
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard.Children[2], all));
        Assert.Equal(ModTick.On, ModTree.TickOf(standard.Children[0], all));
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 2A - Normal lvl Staff Ghosts", "Step Final - Required files"],
            standard.Children[1].Children.Where(c => ModTree.TickOf(c, all) == ModTick.On).Select(c => c.Name));
        // pressed again: none of it
        Assert.Empty(ModTree.Press(tree.Top, all));
        // a folder of plain parts unticked and ticked: its parts only
        var options = standard.Children[0];
        var some = ModTree.Press(options, survey.OnByDefault);
        Assert.Equal(survey.OnByDefault.Count + options.Children.Count, some.Count);
        Assert.Equal(survey.OnByDefault, ModTree.Press(options, some));
    }

    [Fact]
    public void ExplorersOrderPutsSignsFirstThenNumbersByValueThenLetters()
    {
        string[] names = ["Story Course Replacements", "Japanese fixes", "_XTREMEv5", "Step 10", "Step 2B", "Step 2", "step 2a", "Step Final", "1 - Required", "2A - Standard"];
        Assert.Equal(["_XTREMEv5", "1 - Required", "2A - Standard", "Japanese fixes", "Step 2", "step 2a", "Step 2B", "Step 10", "Step Final", "Story Course Replacements"],
            names.Order(ExplorerOrder.Names));
        // a path folder by folder: a folder before what is in it, and before a sibling that sorts later
        string[] paths = ["b/a", "a/z", "a", "a/b/c", "A/b"];
        Assert.Equal(["a", "A/b", "a/b/c", "a/z", "b/a"], paths.Order(ExplorerOrder.Paths));
    }

    [Fact]
    public void APartsOwnRootFolderAndTheFoldersEveryPartSitsInAreNoRows()
    {
        var tree = ModTree.Of("Mod", ["Mod v1/A/root", "Mod v1/B", "Mod v1/B/extra"]);
        Assert.Equal(["Mod", "A", "B", "extra"], tree.Rows.Select(r => r.Name));
        Assert.Equal(["Mod v1/A/root"], tree.Top.Children[0].Own);
        // a mod of one folder: the top row is its part
        var one = ModTree.Of("One", ["One/root"]);
        Assert.Equal(["One/root"], one.Top.Own);
        Assert.Empty(one.Top.Children);
    }
}
