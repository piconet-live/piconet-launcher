using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The import of real community mods, where this computer has them (skipped where it does not): MODS_DOWNLOADS names
/// the folder that holds the archives, MODS_DISC the disc's <c>files/</c> folder, MODS_OUT where the mods are
/// imported (a data folder of its own, <c>MODS_OUT/data</c>) and what the guard said is written (<c>summary.txt</c>).
/// Each mod is imported whole, the parts ticked by default on. Nothing of a mod or the disc goes into the repository.
/// </summary>
public sealed class RealModsTests
{
    [Fact]
    public void TheFiveModsImportAndTheGuardSaysWhatEachMayDoOnline()
    {
        var downloads = Environment.GetEnvironmentVariable("MODS_DOWNLOADS");
        var gameFiles = Environment.GetEnvironmentVariable("MODS_DISC");
        var output = Environment.GetEnvironmentVariable("MODS_OUT");
        Assert.SkipWhen(downloads is null || gameFiles is null || output is null || !Directory.Exists(gameFiles), "no mods on this computer: set MODS_DOWNLOADS, MODS_DISC and MODS_OUT");
        var disc = ModImport.DiscFiles(gameFiles!);
        var library = new ModLibrary(Path.Combine(output!, "data"));
        var lines = new List<string>();
        var imported = new List<InstalledMod>();
        string[] mods = ["luminouscity.zip", "tron_aeropolis.7z", "am_tron_gf_tron.7z", "gxtreme_v5.zip", "f-zero_gx_unleashed_504_all_versions_.zip"];
        foreach (var file in mods)
        {
            var (survey, refusal) = ModImport.Survey(Path.Combine(downloads!, file), disc);
            Assert.True(survey is not null, file + ": " + refusal);
            var on = survey!.OnByDefault;
            lines.Add($"== {file}: {survey.Roots.Count} parts, ticked: {string.Join(" | ", survey.Tree.Parts.Where(on.Contains).Select(c => c.Length == 0 ? "(top)" : c))}");
            var (mod, why) = ModImport.Import(survey, on, gameFiles!, disc, library.Folder, cancel: TestContext.Current.CancellationToken);
            Assert.True(mod is not null, file + ": " + why);
            imported.Add(new InstalledMod(mod!, Path.Combine(library.Folder, mod!.Id), true));
            var stored = Directory.GetFiles(Path.Combine(library.Folder, mod.Id, ModManifest.StoreFolder));
            lines.Add($"   used {mod.Files.Count} of {mod.Parts.Sum(p => p.Files.Count)} in all parts, stored {stored.Length} ({stored.Sum(f => new FileInfo(f).Length) >> 20} MiB), " +
                      $"dropped {string.Join(", ", mod.Dropped.Select(d => $"{d.Key} {d.Value}"))}, picture {mod.Picture ?? "none"}");
            foreach (var group in mod.Files.GroupBy(f => (f.Class, f.Online, f.Why)))
            {
                lines.Add($"   {group.Key.Class} online={group.Key.Online} {group.Key.Why}: {group.Count()} ({string.Join(", ", group.Take(4).Select(f => f.Disc))})");
            }
        }
        library.Save(imported);
        File.WriteAllLines(Path.Combine(output!, "summary.txt"), lines);

        var byName = imported.ToDictionary(m => m.Manifest.Source, m => m.Manifest);
        Assert.All(byName["luminouscity.zip"].Files, f => Assert.True(f.Online));
        Assert.All(byName["am_tron_gf_tron.7z"].Files, f => Assert.True(f.Online));
        Assert.All(byName["tron_aeropolis.7z"].Files, f => Assert.True(f.Online, f.Disc + ": " + f.Why));
        Assert.Contains(byName["tron_aeropolis.7z"].Files, f => f.Class == ModFileClass.Course);
        Assert.Contains(byName["gxtreme_v5.zip"].Files, f => f.Class == ModFileClass.CarData && !f.Online);
        // GXtreme's story course on Trident's slot: its looks and its background go with its course file, offline
        var gxtreme = byName["gxtreme_v5.zip"];
        Assert.Equal("bg_ele", gxtreme.Backgrounds![24]);
        Assert.All(gxtreme.Files.Where(f => f.Disc is "stage/COLI_COURSE24.lz" or "stage/st24.gma.lz" or "stage/selst24.tpl.lz" or "bg/bg_ele.tpl.lz"),
            f => Assert.False(f.Online, f.Disc));
        // its story courses' own models are re-modelled: single player by their shape; Luminous City's re-skin passes
        Assert.Contains(gxtreme.Files, f => f is { Disc: "stage/st24.gma.lz", Why: ModModels.TrackWhy });
        Assert.Contains(byName["f-zero_gx_unleashed_504_all_versions_.zip"].Files, f => f.Class == ModFileClass.Code);
    }
}
