using System.IO.Compression;
using Launcher.Core;
using TestSupport;

namespace Launcher.Tests;

/// <summary>
/// A mod's parts as a tree (<see cref="ModTree"/>), on the listings of two real mods as they ship (ModListings: each
/// file's size and path in GXtreme V5's and GX Unleashed 5.0.4's archives, nothing of their contents): the rows are
/// the mod's own folders down to the parts, a part's "root" folder no row, and the mod's own text files where they
/// sit; the ticks a first look gives (<see cref="ModImport.Defaults"/>, unchanged); any mix ticked; Explorer's order.
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
        // the movies' subtitles are game files that are text
        foreach (var path in folders.Select(f => f + "/x.bin").Concat(files).Concat(["movie/story00.txt", "movie/story00e.txt"]))
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

    /// <summary>The rows shown fully open, each as its depth in dots and its name, and its tick ("-": nothing to tick).</summary>
    private static List<string> Rows(ModTree tree, IReadOnlySet<string> on) =>
        [.. tree.Rows.Select(n => new string('.', n.Depth) + n.Name + " " + (n.HasParts ? ModTree.TickOf(n, on).ToString() : "-"))];

    [Fact]
    public void GXtremesTreeIsItsThreeFoldersInExplorersOrderAndItsTexts()
    {
        var survey = Survey("gxtreme_v5.zip", "gxtreme_v5.tsv");
        var tree = survey.Tree;
        Assert.Equal(["_XTREMEv5/root", "Japanese fixes", "Story Course Replacements/root"], tree.Parts);
        // the first look's ticks, as before the tree: only the Japanese fixes are off; folders first, then the texts
        Assert.Equal(
        [
            "Gxtreme V5 Some", "._XTREMEv5 On", ".Changed Game ID -", "..savefiles -", "...saves_install.txt -", "..boxart_readme.txt -",
            "..GameID_readme_warning.txt -", ".Japanese fixes Off", ".Story Course Replacements On", "..story_install.txt -",
            ".contents.txt -", ".credits.txt -", ".installation.txt -", ".introduction.txt -",
        ], Rows(tree, survey.OnByDefault));
        Assert.Equal("contents.txt", tree.Top.Children.Single(c => c.Name == "contents.txt").Text);
        Assert.Equal("Story Course Replacements/story_install.txt", tree.Rows.Single(r => r.Name == "story_install.txt").Text);
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
        Assert.Equal(["1 - Required files (applicable for all versions of mod)", "2A - Standard Unleashed", "2B - Legends Unleashed",
            "3 (Optional) - Save file or Memory Card", "4 (Optional) - Cheat files", "7 -(Optional)  Proper Dolphin settings", "Tools",
            "Please read me - Disclaimers and Warnings.txt", "Please read me - Info, links and more.txt"], top);
        var standard = tree.Top.Children[1];
        Assert.Equal(["1 (Optional) - Boost Fire Recolor and Machine Textures", "2A - Standard Unleashed", "2B - Standard Unleashed and Story Courses"],
            standard.Children.Select(c => c.Name));
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 1B - Jank machine camera removed", "Step 2A - Normal lvl Staff Ghosts",
            "Step 2B - Expert lvl Ghosts, with Side Attacks", "Step 2C - Expert lvl Ghosts, NO Side Attacks", "Step 2D - Master lvl Ghosts, with Side Attacks",
            "Step 2E - Master lvl Ghosts, NO Side Attacks", "Step 3 - Rename game and choose Game ID", "Step Final - Required files"],
            standard.Children[1].Children.Select(c => c.Name));
        // a folder of only a text: no part in it; the readme beside the texture choices
        var rename = standard.Children[1].Children[7];
        Assert.False(rename.HasParts);
        Assert.Equal(["ISO file Insructions README.txt"], rename.Children.Select(c => c.Name));
        Assert.Equal("ReadMe.txt", standard.Children[0].Children[^1].Name);
        // the movies' subtitles in the required files are game files, not texts to read
        Assert.DoesNotContain(tree.Rows, r => r.Name.StartsWith("story00", StringComparison.Ordinal));

        // the first look's ticks: the required files, Standard's first way with its first steps; no option, no Legends
        Assert.Equal(ModTick.On, ModTree.TickOf(tree.Top.Children[0], on));
        Assert.Equal(ModTick.Some, ModTree.TickOf(standard, on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard.Children[0], on));
        Assert.Equal(ModTick.Some, ModTree.TickOf(standard.Children[1], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(standard.Children[2], on));
        Assert.Equal(ModTick.Off, ModTree.TickOf(tree.Top.Children[2], on));
        Assert.Equal(["Step 1A - Machine camera unchanged", "Step 2A - Normal lvl Staff Ghosts", "Step Final - Required files"],
            standard.Children[1].Children.Where(c => ModTree.TickOf(c, on) == ModTick.On).Select(c => c.Name));
    }

    [Fact]
    public void AnyMixCanBeTickedAndAFolderTicksAllOfItOrNone()
    {
        var survey = Survey("f-zero_gx_unleashed_504_all_versions_.zip", "unleashed_504.tsv.gz");
        var tree = survey.Tree;
        var (standard, legends) = (tree.Top.Children[1], tree.Top.Children[2]);
        var steps = standard.Children[1].Children;
        // a second letter beside the first: both ticked, nothing else moves
        var on = ModTree.Press(steps[1], survey.OnByDefault);
        Assert.Equal(ModTick.On, ModTree.TickOf(steps[0], on));
        Assert.Equal(ModTick.On, ModTree.TickOf(steps[1], on));
        Assert.Equal(survey.OnByDefault.Count + 1, on.Count);
        // Legends beside Standard: all of Legends, Standard as it was
        on = ModTree.Press(legends, on);
        Assert.Equal(ModTick.On, ModTree.TickOf(legends, on));
        Assert.Equal(ModTick.Some, ModTree.TickOf(standard, on));
        // the top row while some is ticked: all of it; again: none
        var all = ModTree.Press(tree.Top, on);
        Assert.Equal(tree.Parts.Count, all.Count);
        Assert.Equal(ModTick.On, ModTree.TickOf(tree.Top, all));
        Assert.Empty(ModTree.Press(tree.Top, all));
        // a folder of plain parts ticked and unticked: its parts only
        var options = standard.Children[0];
        var some = ModTree.Press(options, survey.OnByDefault);
        Assert.Equal(survey.OnByDefault.Count + options.Parts.Count(), some.Count);
        Assert.Equal(survey.OnByDefault, ModTree.Press(options, some));
        // a folder of only texts has nothing to press
        var cheats = tree.Top.Children.Single(c => c.Name.StartsWith("4 (Optional)", StringComparison.Ordinal));
        Assert.False(cheats.HasParts);
        Assert.Equal(survey.OnByDefault, ModTree.Press(cheats, survey.OnByDefault));
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
        // texts where they sit: in a part's folder (none of the game's folders inside a part), beside the parts, in a
        // folder of their own; a folder every part sits in is the top row for them too
        var texts = ModTree.Of("Mod", ["Mod v1/A/root", "Mod v1/B"], ["Mod v1/A/readme.txt", "Mod v1/A/root/stage/notes.txt", "Mod v1/read me.md",
            "Mod v1/Docs/install.txt", "elsewhere.txt"]);
        Assert.Equal(["Mod", "A", "notes.txt", "readme.txt", "B", "Docs", "install.txt", "elsewhere.txt", "read me.md"], texts.Rows.Select(r => r.Name));
        Assert.Equal("Mod v1/A/root/stage/notes.txt", texts.Rows.Single(r => r.Name == "notes.txt").Text);
        Assert.Equal(["Mod v1/A/root", "Mod v1/B"], texts.Parts);
        // a mod of one folder: the top row is its part
        var one = ModTree.Of("One", ["One/root"]);
        Assert.Equal(["One/root"], one.Top.Own);
        Assert.Empty(one.Top.Children);
    }
}
