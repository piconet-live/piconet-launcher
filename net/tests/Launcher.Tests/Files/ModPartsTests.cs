using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// A mod's parts on a made-up mod (a folder) over a made-up disc: the import keeps every part, each file once in
/// <c>store/</c> by its SHA-256; the files laid over the disc are the ticked parts', a later part's (Explorer's order)
/// over an earlier one's, and a later part's file that is the disc's own puts the disc's back; ticks changed after
/// the import change what a start is given and are saved; a mod of the first form (imported before the parts) loads
/// and plays as it was.
/// </summary>
public sealed class ModPartsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly byte[] A0 = [0xA0, 0], A1 = [0xA1, 1], A2 = [0xA2, 2], I0 = [0x10, 0], I1 = [0x11, 1], I2 = [0x12, 2], B1 = [0xB1], B2 = [0xB2];

    private string GameFiles => Path.Combine(_dir, "game", "files");

    private string Mods => Path.Combine(_dir, "data", ModLibrary.FolderName);

    private void Write(string folder, string path, byte[] bytes)
    {
        var file = Path.Combine(_dir, folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }

    /// <summary>The disc (a course's look and a menu file) and a mod laid out as authors do: the core in "_Core/root",
    /// the Japanese fixes, two lettered alternatives, and an extra copied over the core.</summary>
    private string MadeUp()
    {
        Write("game/files", "stage/st05.tpl.lz", A0);
        Write("game/files", "init/race.tpl", I0);
        Write("game/files", "fze.str", [1]);
        Write("My Mod", "readme.txt", [1]);
        Write("My Mod", "_Core/root/stage/st05.tpl.lz", A1);
        Write("My Mod", "_Core/root/init/race.tpl", I1);
        Write("My Mod", "Japanese fixes/init/race.tpl", I2);
        Write("My Mod", "Step 1A/stage/st07.tpl.lz", B1);
        Write("My Mod", "Step 1B/stage/st07.tpl.lz", B2);
        Write("My Mod", "Step 1B/stage/st05.tpl.lz", A1);
        Write("My Mod", "Story/stage/st05.tpl.lz", A2);
        Write("My Mod", "Story/init/race.tpl", I0);
        return Path.Combine(_dir, "My Mod");
    }

    private (ModSurvey Survey, ModManifest Mod) Imported(IReadOnlySet<string>? on = null)
    {
        var source = MadeUp();
        var disc = ModImport.DiscFiles(GameFiles);
        var (survey, refusal) = ModImport.Survey(source, disc);
        Assert.True(survey is not null, refusal);
        Directory.CreateDirectory(Mods);
        var (mod, why) = ModImport.Import(survey, on ?? survey.OnByDefault, GameFiles, disc, Mods, cancel: TestContext.Current.CancellationToken);
        Assert.True(mod is not null, why);
        return (survey, mod);
    }

    /// <summary>What a start lays over the disc: each disc path and the bytes it is given.</summary>
    private static Dictionary<string, byte[]> Laid(InstalledMod mod) =>
        ModOverlay.Of([mod]).Offline.ToDictionary(f => f.Disc, f => File.ReadAllBytes(f.From), StringComparer.Ordinal);

    [Fact]
    public void EveryPartIsKeptEachFileOnceAndTheTickedPartsLaterOneWins()
    {
        var (survey, mod) = Imported();
        Assert.Equal(["_Core/root", "Japanese fixes", "Step 1A", "Step 1B", "Story"], mod.Parts.Select(p => p.Path));
        Assert.Equal(["_Core/root", "Step 1A", "Story"], mod.Parts.Where(p => p.On).Select(p => p.Path));
        Assert.Equal(survey.OnByDefault, mod.PartsOn);
        // A1 is in two parts and kept once; the disc's own I0 not at all
        var store = Path.Combine(Mods, mod.Id, ModManifest.StoreFolder);
        Assert.Equal(6, Directory.GetFiles(store).Length);
        Assert.False(Directory.Exists(Path.Combine(Mods, mod.Id, "files")));
        Assert.Equal(["init/race.tpl"], mod.Parts.Single(p => p.Path == "Story").Same);

        // Story comes after the core: its course look wins, and its menu file, the disc's own, takes the core's away
        var installed = new InstalledMod(mod, Path.Combine(Mods, mod.Id), true);
        var laid = Laid(installed);
        Assert.Equal(["stage/st05.tpl.lz", "stage/st07.tpl.lz"], laid.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(A2, laid["stage/st05.tpl.lz"]);
        Assert.Equal(B1, laid["stage/st07.tpl.lz"]);
        Assert.Equal(laid.Keys.Order(StringComparer.Ordinal), ModOverlay.Of([installed]).Guarded.Select(f => f.Disc).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TicksChangedAfterTheImportChangeTheOverlayAndAreSaved()
    {
        var (survey, mod) = Imported();
        var library = new ModLibrary(Path.Combine(_dir, "data"));
        library.Save([new InstalledMod(mod, Path.Combine(Mods, mod.Id), true)]);
        var tree = survey.Tree;

        // the other letter: Step 1A goes; Story still wins the course look
        var on = ModTree.Press(tree.Top.Children.Single(c => c.Name == "Step 1B"), mod.PartsOn);
        Assert.DoesNotContain("Step 1A", on);
        var picked = library.Read().Single();
        picked = picked with { Manifest = picked.Manifest.WithOn(on) };
        Assert.Equal(B2, Laid(picked)["stage/st07.tpl.lz"]);
        Assert.Equal(A2, Laid(picked)["stage/st05.tpl.lz"]);
        // Story off: the core's menu file is back, and Step 1B's look (the core's own bytes)
        on = ModTree.Press(tree.Top.Children.Single(c => c.Name == "Story"), on);
        picked = picked with { Manifest = picked.Manifest.WithOn(on) };
        Assert.Equal(A1, Laid(picked)["stage/st05.tpl.lz"]);
        Assert.Equal(I1, Laid(picked)["init/race.tpl"]);
        // the Japanese fixes after the core: their menu file
        on = ModTree.Press(tree.Top.Children.Single(c => c.Name == "Japanese fixes"), on);
        picked = picked with { Manifest = picked.Manifest.WithOn(on) };
        Assert.Equal(I2, Laid(picked)["init/race.tpl"]);

        // saved in the list, read back as the player left them; the manifest keeps the import's
        library.Save([picked]);
        var read = library.Read().Single();
        Assert.Equal(on, read.Manifest.PartsOn);
        Assert.Equal(I2, Laid(read)["init/race.tpl"]);
        Assert.Equal(survey.OnByDefault, ModLibrary.ReadManifest(Path.Combine(Mods, mod.Id))!.PartsOn);
        // none ticked: the mod lays nothing over the disc
        library.Save([read with { Manifest = read.Manifest.WithOn(new HashSet<string>()) }]);
        Assert.Empty(Laid(library.Read().Single()));
    }

    [Fact]
    public void TheSameModImportedAgainReplacesItselfWithTheNewTicks()
    {
        var (survey, mod) = Imported();
        var again = ModImport.Import(survey, new HashSet<string> { "Story" }, GameFiles, ModImport.DiscFiles(GameFiles), Mods,
            cancel: TestContext.Current.CancellationToken).Mod!;
        Assert.Equal(mod.Id, again.Id);
        Assert.Single(Directory.GetDirectories(Mods));
        Assert.Equal(["Story"], ModLibrary.ReadManifest(Path.Combine(Mods, mod.Id))!.PartsOn);
    }

    [Fact]
    public void AModOfTheFirstFormLoadsAndPlaysAsItWas()
    {
        // as the launcher of 2026-11-03 wrote it (a real one's shape: D:\FZero\v060-launcher-g\data\Mods\679ec6437203)
        var folder = Path.Combine(Mods, "679ec6437203");
        Write(Path.Combine("data", "Mods", "679ec6437203", "files"), "stage/st05.tpl.lz", A1);
        Write(Path.Combine("data", "Mods", "679ec6437203", "files"), "bg/bg_tow.tpl.lz", A2);
        File.WriteAllText(Path.Combine(folder, ModManifest.FileName), """
            {
              "format": 1,
              "id": "679ec6437203",
              "name": "Luminouscity",
              "source": "luminouscity.zip",
              "sourceSha256": "52e2358edc9fcb8eda0554bae3c2bccf7371a65f4963e3dcb8de69c7283e1866",
              "imported": "2026-11-03T07:33:22.8236176\u002B00:00",
              "chosen": [ "luminouscity_complete", "extra" ],
              "dropped": {},
              "files": [
                { "disc": "bg/bg_tow.tpl.lz", "class": "look", "online": true, "size": 2, "sha256": "d923220fa77340ae3e3b5889bc5b09de58d74671df29bc03cea5679a3a4fe77a" },
                { "disc": "stage/st05.tpl.lz", "class": "look", "online": true, "size": 2, "sha256": "195d55622d22be6c9df7158b44c18fee5ee0dc2892d377b850c4d18d7b1d59bc" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(Mods, ModLibrary.ListFile), """{"format": 1, "mods": [{"id": "679ec6437203", "on": true}]}""");
        var library = new ModLibrary(Path.Combine(_dir, "data"));
        var mod = library.Read().Single();
        Assert.True(mod.On);
        Assert.True(mod.Manifest.Fixed);
        Assert.Equal(["luminouscity_complete", "extra"], mod.Manifest.Parts.Where(p => p.On).Select(p => p.Path));
        Assert.Equal(["bg/bg_tow.tpl.lz", "stage/st05.tpl.lz"], mod.Manifest.Files.Select(f => f.Disc));
        var laid = Laid(mod);
        Assert.Equal(A1, laid["stage/st05.tpl.lz"]);
        Assert.Equal(A2, laid["bg/bg_tow.tpl.lz"]);
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "files", "stage", "st05.tpl.lz")),
            ModOverlay.Of([mod]).Offline.Single(f => f.Disc == "stage/st05.tpl.lz").From);
        // its parts stay as they were chosen: no tick changes it, the list saves none, and it is never written again
        Assert.Same(mod.Manifest, mod.Manifest.WithOn(new HashSet<string>()));
        library.Save([mod]);
        Assert.DoesNotContain("parts", File.ReadAllText(Path.Combine(Mods, ModLibrary.ListFile)), StringComparison.Ordinal);
        Assert.Equal(laid.Keys, Laid(library.Read().Single()).Keys);
        Assert.Throws<ArgumentException>(() => ModLibrary.WriteManifest(folder, mod.Manifest));
    }

    [Fact]
    public void AManifestThatNamesAFileOutsideTheModIsNoMod()
    {
        var (_, mod) = Imported();
        var folder = Path.Combine(Mods, mod.Id);
        var text = File.ReadAllText(Path.Combine(folder, ModManifest.FileName));
        var sha = mod.Parts.SelectMany(p => p.Files).First().Sha256;
        File.WriteAllText(Path.Combine(folder, ModManifest.FileName), text.Replace(sha, "../../../x", StringComparison.Ordinal));
        Assert.Null(ModLibrary.ReadManifest(folder));
    }
}
