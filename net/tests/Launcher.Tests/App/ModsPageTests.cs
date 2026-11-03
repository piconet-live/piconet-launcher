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
/// single-player part shows the tag. LOBBY_SHOTS keeps pictures; MODS_SHOTS_DATA (a data folder RealModsTests
/// filled) with MODS_DOWNLOADS and MODS_DISC pictures the five real mods and Unleashed's chooser.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class ModsPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private static void MadeUpMod(string mods, string id, string name, params ModFile[] files)
    {
        var folder = Path.Combine(mods, id);
        foreach (var f in files.Where(f => f.Class != ModFileClass.Code))
        {
            var path = Path.Combine(folder, "files", f.Disc);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
        }
        ModLibrary.WriteManifest(folder, new ModManifest(id, name, name + ".zip", "00", DateTimeOffset.UnixEpoch, [""], files,
            new Dictionary<string, int>(), null));
    }

    private static Button Tab(MainWindow window, string word) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab") && Equals(b.Content, word));

    private static async Task OnAWindow(AppState state, Action<MainWindow, MainViewModel> steps, string? disc = null)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(disc);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
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
            Shot(window, "mods-made-up");

            // the first card's Down: the order is saved, and the later mod's files win
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
