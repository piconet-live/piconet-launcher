using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Mods place, pressed with the headless window's mouse: its tab in the left column opens the cards; a card's
/// switch and its Down are saved at once (<c>Mods/mods.json</c>) and change what a game start is given; a mod with a
/// single-player part shows the tag; a card pressed opens the mod's tree, and a tick in it is saved and changes the
/// start. LOBBY_SHOTS keeps pictures; MODS_SHOTS_DATA (a data folder RealModsTests filled) with MODS_DOWNLOADS and
/// MODS_DISC pictures the five real mods and Unleashed's chooser; MODS_DOWNLOADS and MODS_DISC alone, GXtreme's and
/// Unleashed's trees, in the chooser and on their cards after an import into the test's own folder.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class ModsPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private static void MadeUpMod(string mods, string id, string name, params ModFile[] files) => MadeUpMod(mods, id, name, [new ModPart("", true, files, [])]);

    /// <summary>A mod of these parts, each file's bytes its disc path's (its SHA-256 the file's name in the store).</summary>
    private static void MadeUpMod(string mods, string id, string name, IReadOnlyList<ModPart> parts)
    {
        var folder = Path.Combine(mods, id);
        Directory.CreateDirectory(Path.Combine(folder, ModManifest.StoreFolder));
        var stored = parts.Select(p => p with { Files = [.. p.Files.Select(f =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(p.Path + ":" + f.Disc);
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            if (f.Class != ModFileClass.Code)
            {
                File.WriteAllBytes(Path.Combine(folder, ModManifest.StoreFolder, sha), bytes);
            }
            return f with { Sha256 = sha };
        })] }).ToList();
        ModLibrary.WriteManifest(folder, new ModManifest(id, name, name + ".zip", "00", DateTimeOffset.UnixEpoch, stored,
            new Dictionary<string, int>(), null));
    }

    private static Button Tab(MainWindow window, string word) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab") && Equals(b.Content, word));

    private static async Task OnAWindow(AppState state, Action<MainWindow, MainViewModel> steps, string? disc = null, int height = 800)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(disc);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = height };
                window.Show();
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                try
                {
                    steps(window, main);
                }
                finally
                {
                    window.Close();
                    main.Dispose();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
            state.Shutdown();
        }
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is { Length: > 0 } folder)
        {
            for (var i = 0; i < 20; i++)
            {
                Frame();
            }
            Directory.CreateDirectory(folder);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    [Fact]
    public async Task TheCardsSwitchAndOrderAreSavedAndReachTheStart()
    {
        var mods = Path.Combine(Data, ModLibrary.FolderName);
        MadeUpMod(mods, "aaaaaaaaaaaa", "Looks", new ModFile("stage/st05.tpl.lz", ModFileClass.Look, true, null, false, 3, ""));
        MadeUpMod(mods, "bbbbbbbbbbbb", "Ghosts", new ModFile("staff_ghost/ghost1.dat", ModFileClass.Ghost, false, "Ghosts: single player only", false, 3, ""),
            new ModFile("enemy_line/line__.bin", ModFileClass.Code, false, ModGuard.CodeWhy, false, 3, ""));
        new ModLibrary(Data).Save([.. new ModLibrary(Data).Read().Select(m => m with { On = true })]);
        // a server nobody answers on: the test asks no real one (trap 1)
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{"server": "ws://127.0.0.1:9/ws"}""");
        var state = AppState.Load(Data);
        await state.LoadModsAsync();

        await OnAWindow(state, (window, main) =>
        {
            Click(window, Tab(window, "Mods"));
            Pump(() => main.Current is ModsViewModel);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Looks", texts);
            Assert.Contains("Single player", texts);
            Assert.Contains("Code not used", texts);
            Assert.Contains("Mods on top take priority", texts);
            Shot(window, "mods-made-up");

            // the first card's Down: the order is saved (the mod on top's files win)
            Click(window, window.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Down" && b.IsEffectivelyVisible));
            Pump(() => state.Mods![0].Manifest.Name == "Ghosts");
            Assert.Equal(["bbbbbbbbbbbb", "aaaaaaaaaaaa"], new ModLibrary(Data).Read().Select(m => m.Manifest.Id));

            // a switch off: saved, and the start's overlay loses that mod's file
            Assert.Contains(state.GamePaths.Mods!.Offline, f => f.Disc == "stage/st05.tpl.lz");
            Click(window, window.GetVisualDescendants().OfType<ToggleSwitch>().First(t => AutomationProperties.GetName(t) == "Looks"));
            Pump(() => state.Mods!.Single(m => m.Manifest.Name == "Looks").On == false);
            Assert.False(new ModLibrary(Data).Read().Single(m => m.Manifest.Name == "Looks").On);
            Assert.DoesNotContain(state.GamePaths.Mods!.Offline, f => f.Disc == "stage/st05.tpl.lz");
            // the ghost goes to offline play only, and the code nowhere
            Assert.Empty(state.GamePaths.Mods!.Guarded);
            Assert.DoesNotContain(state.GamePaths.Mods!.Offline, f => f.Disc.StartsWith("enemy_line", StringComparison.Ordinal));
        });
    }

    private static CheckBox Tick(Window window, string name) =>
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);

    private static void Open(Window window, string name) =>
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("arrow") && b.IsEffectivelyVisible &&
            AutomationProperties.GetName(b) == name));

    [Fact]
    public async Task ACardsTreeTicksAPartAndTheStartFollows()
    {
        var mods = Path.Combine(Data, ModLibrary.FolderName);
        var look = new ModFile("stage/st05.tpl.lz", ModFileClass.Look, true, null, false, 3, "");
        MadeUpMod(mods, "cccccccccccc", "Two parts", [new ModPart("Pack/_Core/root", true, [look], []), new ModPart("Pack/Extra", true, [look,
            new ModFile("bg/bg_tow.tpl.lz", ModFileClass.Look, true, null, false, 3, "")], [])]);
        MadeUpMod(mods, "dddddddddddd", "One part", new ModFile("bg/bg_big.tpl.lz", ModFileClass.Look, true, null, false, 3, ""));
        new ModLibrary(Data).Save([.. new ModLibrary(Data).Read().Select(m => m with { On = true })]);
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{"server": "ws://127.0.0.1:9/ws"}""");
        var state = AppState.Load(Data);
        await state.LoadModsAsync();
        string FromOf(string disc) => File.ReadAllText(state.GamePaths.Mods!.Offline.Single(f => f.Disc == disc).From);

        await OnAWindow(state, (window, main) =>
        {
            Click(window, Tab(window, "Mods"));
            Pump(() => main.Current is ModsViewModel);
            var page = (ModsViewModel)main.Current;
            Assert.Equal("Pack/Extra:stage/st05.tpl.lz", FromOf("stage/st05.tpl.lz"));
            // a mod of one part has no tree; one of two opens it when pressed: the mod's row, then its two parts
            var faces = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cardface")).ToList();
            Click(window, faces.Single(f => AutomationProperties.GetName(f) == "One part"));
            Frame();
            Assert.False(page.Cards.Single(c => c.Name == "One part").TreeOpen);
            Click(window, faces.Single(f => AutomationProperties.GetName(f) == "Two parts"));
            Pump(() => window.GetVisualDescendants().OfType<CheckBox>().Count(c => c.IsEffectivelyVisible) == 3);
            Assert.True(Tick(window, "Two parts").IsChecked);
            Assert.True(Tick(window, "_Core").IsChecked);
            Shot(window, "mods-tree-made-up");

            // Extra unticked: the mod's row shows some, the list saves it, and the core's file is the start's
            Click(window, Tick(window, "Extra"));
            Pump(() => !state.Mods!.Single(m => m.Manifest.Id == "cccccccccccc").Manifest.PartsOn.Contains("Pack/Extra"));
            Frame();
            Assert.False(Tick(window, "Extra").IsChecked);
            Assert.Null(Tick(window, "Two parts").IsChecked);
            Assert.Equal(["Pack/_Core/root"], new ModLibrary(Data).Read().Single(m => m.Manifest.Id == "cccccccccccc").Manifest.PartsOn);
            Assert.Equal("Pack/_Core/root:stage/st05.tpl.lz", FromOf("stage/st05.tpl.lz"));
            Assert.DoesNotContain(state.GamePaths.Mods!.Offline, f => f.Disc == "bg/bg_tow.tpl.lz");
            // the card is the same card, its tree still open
            Assert.True(page.Cards.Single(c => c.Name == "Two parts").TreeOpen);
            // the mod's row pressed while some is ticked: all of it
            Click(window, Tick(window, "Two parts"));
            Pump(() => state.Mods!.Single(m => m.Manifest.Id == "cccccccccccc").Manifest.PartsOn.Count == 2);
            Assert.Equal("Pack/Extra:stage/st05.tpl.lz", FromOf("stage/st05.tpl.lz"));
            Frame();
            Assert.True(Tick(window, "Extra").IsChecked);
            Shot(window, "mods-tree-made-up-2");
        });
    }

    [Fact]
    public async Task TheTreesOfGXtremeAndUnleashed()
    {
        var downloads = Environment.GetEnvironmentVariable("MODS_DOWNLOADS");
        var gameFiles = Environment.GetEnvironmentVariable("MODS_DISC");
        Assert.SkipWhen(downloads is null || gameFiles is null, "no real mods: set MODS_DOWNLOADS and MODS_DISC");
        var game = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameFiles!))!;
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            $$"""{"recomp": {"gameFolder": {{System.Text.Json.JsonSerializer.Serialize(game)}}}, "server": "ws://127.0.0.1:9/ws"}""");
        var state = AppState.Load(Data);
        await state.LoadModsAsync();
        var image = RealDiscStatsTests.PlayersDisc();

        await OnAWindow(state, (window, main) =>
        {
            Click(window, Tab(window, "Mods"));
            Pump(() => main.Current is ModsViewModel);
            var page = (ModsViewModel)main.Current;
            void Wait(Func<bool> done, string what)
            {
                // (the first look reads the whole disc's file list and the archive's, the import every part: slow over a Windows drive)
                for (var i = 0; i < 3000 && !done(); i++)
                {
                    Thread.Sleep(100);
                    Frame();
                }
                Assert.True(done(), what + ": " + page.Notice);
            }
            void Import(string file, string shot, Action? open = null)
            {
                var picked = page.PickedAsync(Path.Combine(downloads!, file));
                Wait(() => page.Choosing, "the chooser did not open");
                open?.Invoke();
                Shot(window, shot + "-import");
                Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ImportChosen"));
                Wait(() => picked.IsCompleted && !page.Importing && page.Cards.Count > 0 && page.Cards[^1].HasTree, "not imported");
                var card = page.Cards[^1];
                foreach (var other in page.Cards.Where(c => c.TreeOpen))
                {
                    other.ToggleTreeCommand.Execute(null);
                }
                Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("cardface") && AutomationProperties.GetName(b) == card.Name));
                Pump(() => card.TreeOpen);
                open?.Invoke();
                Shot(window, shot + "-card");
            }
            Import("gxtreme_v5.zip", "gxtreme");
            var gx = state.Mods!.Single();
            Assert.Equal(["_XTREMEv5/root", "Story Course Replacements/root"], gx.Manifest.PartsOn.Order(ExplorerOrder.Paths));
            Assert.Equal(3, gx.Manifest.Parts.Count);
            Assert.Null(Tick(window, gx.Manifest.Name).IsChecked);

            Import("f-zero_gx_unleashed_504_all_versions_.zip", "unleashed", () =>
            {
                Open(window, "2A - Standard Unleashed");
                Frame();
                // the version's own "2A - Standard Unleashed" (the second row of that name, now shown)
                Click(window, window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("arrow") && b.IsEffectivelyVisible &&
                    AutomationProperties.GetName(b) == "2A - Standard Unleashed").Last());
                Frame();
            });
            var unleashed = state.Mods![^1];
            Assert.Equal(49, unleashed.Manifest.Parts.Count);
            Assert.Contains(unleashed.Manifest.Files, f => f.Class == ModFileClass.Code);
        }, image, height: 1400);
    }

    [Fact]
    public async Task TheRealModsAsTheirCardsShowThem()
    {
        var real = Environment.GetEnvironmentVariable("MODS_SHOTS_DATA");
        var downloads = Environment.GetEnvironmentVariable("MODS_DOWNLOADS");
        var gameFiles = Environment.GetEnvironmentVariable("MODS_DISC");
        Assert.SkipWhen(real is null || downloads is null || gameFiles is null, "no real mods: set MODS_SHOTS_DATA, MODS_DOWNLOADS and MODS_DISC");
        CopyFolder(Path.Combine(real!, ModLibrary.FolderName), Path.Combine(Data, ModLibrary.FolderName));
        var game = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameFiles!))!;
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            $$"""{"recomp": {"gameFolder": {{System.Text.Json.JsonSerializer.Serialize(game)}}}, "server": "ws://127.0.0.1:9/ws"}""");
        var state = AppState.Load(Data);
        await state.LoadModsAsync();
        var image = RealDiscStatsTests.PlayersDisc();

        await OnAWindow(state, (window, main) =>
        {
            Click(window, Tab(window, "Mods"));
            Pump(() => main.Current is ModsViewModel page && page.Cards.Count == 5);
            var page = (ModsViewModel)main.Current;
            Pump(() => page.Cards.Count(c => c.HasPicture) >= 2, "no pictures");
            Shot(window, "mods-page");

            // Unleashed's folders to choose from
            var unleashed = Path.Combine(downloads!, "f-zero_gx_unleashed_504_all_versions_.zip");
            var picked = page.PickedAsync(unleashed);
            // (the first look reads the whole disc's file list and the archive's: slow over a Windows drive)
            for (var i = 0; i < 600 && !page.Choosing; i++)
            {
                Thread.Sleep(200);
                Frame();
            }
            Assert.True(page.Choosing, "the chooser did not open: " + page.Notice);
            Shot(window, "mods-chooser");
            page.CancelImportCommand.Execute(null);
            Pump(() => picked.IsCompleted);
        }, image);
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
