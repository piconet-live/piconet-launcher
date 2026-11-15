using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned first start ("Experimental UI" on), in the launcher's own window on a made-up disc image
/// (<see cref="SyntheticDisc"/>, as FirstScreenTests has it): three steps instead of the classic screen. 1: Next only
/// once the image is the game's; Next prepares the game, and when it is ready the name's step follows (the launcher
/// does not open yet); an image that is not the game's says so and Next stays off. 2: the name changed on the keyboard
/// on the screen. 3: the controller's test. Back (and Escape) a step; "Let's race" opens the launcher on the hub. Past
/// the first start (here: the stand-in, which opens on the hub), game files that are not ready make the hub's last
/// entry "Prepare the game": its panel prepares them, and the game can start. With <c>NEXT_SHOTS</c> naming a folder,
/// the steps there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextFirstStartTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
    private readonly SyntheticDisc _disc = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private DiscPins Pins => _disc.Pins();

    /// <summary>A native mod bundle for the made-up disc (as FirstScreenTests makes it).</summary>
    private string Bundle()
    {
        var folder = Path.Combine(_dir, "bundle");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.bin"), "bundle");
        string Entry(uint address, int offset, uint replacement, string what) => string.Create(CultureInfo.InvariantCulture,
            $"{{\"address\": \"{address:X8}\", \"original\": \"{SyntheticDisc.Word(_disc.Dol, offset):X8}\", \"new\": \"{replacement:X8}\", \"what\": \"{what}\"}}");
        var entries = Entry(SyntheticDisc.TextAddress + 0x40, 0x140, 0x38600000, "pin") + ",\n" +
            Entry(SyntheticDisc.TextAddress + 0x80, 0x180, 0x497FD784, "linked");
        File.WriteAllText(Path.Combine(folder, DiscPrep.PatchListName),
            $"{{\"version\": 1, \"complete\": true, \"image_sha1\": \"bundle\", \"main_rel\": \"801BA660\", \"patches\": [\n{entries}\n]}}");
        return folder;
    }

    private AppState State(string? disc = null, bool standIn = false)
    {
        Directory.CreateDirectory(Data);
        var (runner, module) = (Path.Combine(_dir, "runner.exe"), Path.Combine(_dir, "module.dll"));
        File.WriteAllText(runner, "");
        File.WriteAllText(module, "");
        var settings = new Dictionary<string, object?>
        {
            ["noServer"] = true,
            ["experimentalUi"] = true,
            ["soundMuted"] = true,
            ["racer"] = new { name = "ANA", machine = 6, color = new { r = 232, g = 67, b = 46 } },
            ["recomp"] = new { runner, module, bundle = Bundle() },
            ["discPath"] = disc,
        };
        File.WriteAllText(Path.Combine(Data, "settings.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var state = AppState.Load(Data, standIn: standIn);
        state.Preparation = new DiscPrepOptions { Pins = Pins };
        return state;
    }

    [Fact]
    public async Task ThreeStepsTakeTheDiscTheNameAndTheControllerThenTheLauncherOpens()
    {
        var image = _disc.Write(_dir);
        var state = State();
        await InWindow(state, (window, main) =>
        {
            Assert.IsType<DiscViewModel>(main.Current);
            Assert.False(main.Navigation);
            var first = main.Shell.FirstStart;
            var page = Page<FirstStartView>(window);
            Assert.Equal((1, "WELCOME TO PICONET"), (first.Step, Named<TextBlock>(page, "Title").Text));
            Assert.False(Named<Button>(page, "Next").IsEffectivelyEnabled);
            Assert.False(Named<Button>(page, "Back").IsEffectivelyVisible);
            Assert.Equal(["Select", "Next"], main.Shell.Hints.Select(h => h.Label));
            Shot(window, state, "next-first-disc");

            // an image that is not the game's: said, and Next stays off
            var wrong = Path.Combine(_dir, "other.iso");
            File.WriteAllBytes(wrong, new byte[4096]);
            main.Disc.Path = wrong;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<TextBlock>(page, "DiscWrong").IsEffectivelyVisible);
            Assert.False(Named<Button>(page, "Next").IsEffectivelyEnabled);

            // the game's image: Next prepares the game; ready, the name's step follows and the launcher stays shut
            main.Disc.Path = image;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<TextBlock>(page, "DiscStatus").IsEffectivelyVisible);
            Click(window, Named<Button>(page, "Next"));
            PumpTelling(state, () => first.Step == 2, "the game was not prepared", 60);
            Assert.True(main.Disc.Ready);
            Assert.Equal(image, state.Settings.DiscPath);
            Assert.IsType<DiscViewModel>(main.Current);
            Assert.False(main.Navigation);
            Assert.Equal("YOUR NAME", Named<TextBlock>(page, "Title").Text);
            Assert.Equal(["Select", "Next", "Back"], main.Shell.Hints.Select(h => h.Label));

            // 2: the name on the keyboard on the screen
            Click(window, Named<Button>(page, "ChangeName"));
            var keyboard = Assert.IsType<KeyboardViewModel>(main.Shell.Keyboard);
            keyboard.Text = "bob";
            Click(window, Named<Button>(Shell(window), "KeyboardDone"));
            Assert.Equal("BOB", state.Settings.Racer.Name);
            Assert.Equal("BOB", Named<TextBlock>(page, "NameShown").Text);
            Shot(window, state, "next-first-name");

            // 3: the controller; Escape a step back, and on again
            Click(window, Named<Button>(page, "Next"));
            Assert.Equal(3, first.Step);
            Assert.Equal("LET'S RACE", Named<Button>(page, "Next").Content);
            Shot(window, state, "next-first-pad");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal(2, first.Step);
            Click(window, Named<Button>(page, "Next"));
            Assert.Equal(3, first.Step);

            // Let's race: the launcher opens on the hub
            Click(window, Named<Button>(page, "Next"));
            Pump(() => main.Current is HubViewModel, "the launcher did not open");
            Assert.True(main.Navigation);
            Assert.True(main.Disc.OpensAtOnce);
        });
    }

    [Fact]
    public async Task GameFilesThatAreNotReadyArePreparedFromTheHub()
    {
        var image = _disc.Write(_dir);
        var state = State(image, standIn: true);
        await InWindow(state, (window, main) =>
        {
            Assert.IsType<HubViewModel>(main.Current);
            var game = main.Shell.Hub.Entries.Single(e => e.Id == HubViewModel.Game);
            Assert.Equal(("Prepare the game", true), (game.Label, game.Enabled));
            Assert.Equal("Not prepared", PrepareSheetViewModel.Words(state));

            // the entry opens the panel; Prepare game files makes them, the panel closes, and the game can start
            Click(window, Said(Shell(window), "Prepare the game"));
            Assert.IsType<PrepareSheetViewModel>(main.Shell.Sheet);
            var panel = Page<PrepareSheetView>(window);
            Assert.True(Named<TextBlock>(panel, "DiscStatus").IsEffectivelyVisible);
            Shot(window, state, "next-prepare");
            Click(window, Named<Button>(panel, "Prepare"));
            PumpTelling(state, () => main.Shell.Sheet is null, "the game's files were not prepared", 60);
            Assert.False(PrepareSheetViewModel.Wanted(state));
            Assert.Equal(("Start F-Zero GX", "Ready"), (game.Label, PrepareSheetViewModel.Words(state)));
        });
    }

    [Fact]
    public async Task ARaceThatCannotBeJoinedShowsTheWay()
    {
        var image = _disc.Write(_dir);
        var state = State(image);
        await InWindow(state, (window, main) =>
        {
            // the game's files not ready: RACE opens the panel that prepares them (no press does nothing)
            main.Control.GoCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<PrepareSheetViewModel>(main.Shell.Sheet);
            main.Shell.Sheet = null;

            // no game program: why, with OK alone
            File.Delete(Path.Combine(_dir, "runner.exe"));
            _ = state.LookForTheGame();
            PumpTelling(state, () => !PrepareSheetViewModel.Wanted(state) && state.RaceProblem is not null, "the launcher did not look again");
            main.Control.GoCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("Can't race yet", "OK"), (main.Shell.ConfirmTitle, main.Shell.KeepWord));
            Assert.True(main.Shell.AskingConfirm);
            Assert.False(Named<Button>(Shell(window), "ConfirmDo").IsEffectivelyVisible);
        });
    }

    [Fact]
    public async Task AGameFolderOfTheSettingsThatHoldsNoGameIsLetGoAndTheGamePrepared()
    {
        var image = _disc.Write(_dir);
        var state = State(image, standIn: true);
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;
        state.Update(s => s with { Recomp = s.Recomp! with { GameFolder = empty } });
        await state.LookForTheGame();
        await InWindow(state, (window, main) =>
        {
            // a folder named by hand that holds no game: the hub offers to prepare the game all the same
            Assert.Equal(DiscPrep.NotPreparedMessage, state.GameProblem);
            var game = main.Shell.Hub.Entries.Single(e => e.Id == HubViewModel.Game);
            Assert.Equal("Prepare the game", game.Label);
            Assert.Equal("Not prepared", PrepareSheetViewModel.Words(state));

            // the panel lets the folder go, and Prepare game files makes the launcher's own
            Click(window, Said(Shell(window), "Prepare the game"));
            Assert.Null(state.Settings.Recomp?.GameFolder);
            Click(window, Named<Button>(Page<PrepareSheetView>(window), "Prepare"));
            PumpTelling(state, () => main.Shell.Sheet is null, "the game's files were not prepared", 60);
            Assert.Equal(("Start F-Zero GX", "Ready"), (game.Label, PrepareSheetViewModel.Words(state)));
        });
    }
}
