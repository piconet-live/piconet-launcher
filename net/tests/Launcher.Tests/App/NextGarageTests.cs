using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using Launcher.Core.Pads;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned Garage ("Experimental UI" on), in the launcher's own window: the shelves as chips, the machine on
/// the stage with its card, the grid of the shelf's machines (the focus, the arrow keys or a press put one on the
/// stage, nothing is raced; a press takes the focus to "Use this machine", which races it); the setting stepped with
/// left and right; Sort &amp; filter (Y) leaving
/// out the machines without the letters chosen, the dot on its button; the colours panel painting the machine and the
/// trail and "Back to the original"; the builder as an option page (New machine, a part stepped, Save racing it, Edit
/// parts and Back, Remove after a question). With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextGarageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}}""");
        return AppState.Load(data, standIn: true);
    }

    /// <summary>One of the top bar's areas pressed.</summary>
    private static void Area(MainWindow window, string name) =>
        Click(window, Named<StackPanel>(Shell(window), "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name));

    /// <summary>The Garage area, from the top bar.</summary>
    private static GaragePageView Garage(MainWindow window)
    {
        Area(window, "Garage");
        return Page<GaragePageView>(window);
    }

    /// <summary>The grid's tiles as they stand.</summary>
    /// <summary>The pointer moved onto the control's middle, from a little beside it (no press).</summary>
    private static void Hover(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at - new Point(12, 12));
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
    }

    private static List<Button> Tiles(GaragePageView page)
    {
        Dispatcher.UIThread.RunJobs();
        return [.. Named<ItemsControl>(page, "Tiles").GetVisualDescendants().OfType<Button>()];
    }

    [Fact]
    public async Task TheGridShowsAMachineAndOnlyUseRacesIt()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var garage = main.Garage;
            var page = Garage(window);
            Assert.Same(garage, main.Current);
            Assert.Equal(["Select", "Sort & filter", "Shelves", "Back"], Hints(main));
            Assert.False(main.Shell.BackShown);
            // the machines: the one raced on the stage, in use; the Stock shelf's 41 in the grid; the machine's own
            // page is the next step
            Assert.Equal(6, garage.MachineIndex);
            Assert.False(garage.StagedCustom);
            Assert.True(garage.StagedInUse);
            Assert.Equal(Machines.Names[6].ToUpperInvariant(), Named<TextBlock>(page, "ChooseName").Text);
            Assert.False(Named<Grid>(page, "MachinePage").IsEffectivelyVisible);
            Assert.Equal(41, Tiles(page).Count);
            Assert.Contains("go", Named<Button>(page, "ShelfStock").Classes);
            Shot(window, state, "next-garage");

            // the focus on another tile: that machine on the stage, nothing raced
            Focus(Tiles(page)[0]);
            Assert.Equal(0, garage.MachineIndex);
            Assert.True(garage.Previewing);
            Assert.False(garage.StagedInUse);
            Assert.Equal(6, state.Settings.Racer.Machine);
            // the arrow keys go through the grid, four to a row: down is the machine under it
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal(4, garage.MachineIndex);
            Assert.True(Tiles(page)[4].IsFocused);
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.Equal(0, garage.MachineIndex);
            // the pointer works as the pad does: moved onto another tile, that machine on the stage, its tile the focus
            // (the grid not scrolled for it); still nothing raced
            var offset = Named<ScrollViewer>(page, "Strip").Offset;
            Hover(window, Tiles(page)[5]);
            Assert.Equal(5, garage.MachineIndex);
            Assert.True(Tiles(page)[5].IsFocused);
            Assert.Equal(offset, Named<ScrollViewer>(page, "Strip").Offset);
            Assert.Equal(6, state.Settings.Racer.Machine);
            Hover(window, Tiles(page)[0]);
            Assert.Equal(0, garage.MachineIndex);
            // a click on another tile: its own page, the focus on "Use this machine"; nothing raced
            Click(window, Tiles(page)[1]);
            Assert.Equal(1, garage.MachineIndex);
            Assert.True(main.Shell.GaragePage.Detail);
            Assert.True(Named<Grid>(page, "MachinePage").IsEffectivelyVisible);
            Assert.False(Named<Grid>(page, "Browse").IsEffectivelyVisible);
            Assert.Equal(Machines.Names[1].ToUpperInvariant(), Named<TextBlock>(page, "MachineName").Text);
            Assert.Equal("USE THIS MACHINE", Named<Button>(page, "Use").Content);
            Assert.Equal(["Change", "Select", "Use", "Back"], Hints(main));
            Assert.True(main.Shell.BackShown);                            // (a step into the page: Back, and B)
            // no engine switch and no boost under the machine (taken out, the owner 2026-11-15)
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.Name is "Engine" or "BoostNow");
            Pump(() => Named<Button>(page, "Use").IsFocused, "the focus did not go on to Use this machine");
            Assert.Equal(6, state.Settings.Racer.Machine);
            Shot(window, state, "next-garage-machine");
            // Escape (as B): back to the machines, the focus on that machine's tile; the page stays
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.GaragePage.Detail);
            Assert.Same(garage, main.Current);
            Pump(() => Tiles(page)[1].IsFocused, "the focus did not go back to the machine's tile");
            // A (Enter) on a tile: its page; A there races it, and the machines again with the focus on it
            Focus(Tiles(page)[0]);
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.True(main.Shell.GaragePage.Detail);
            Pump(() => Named<Button>(page, "Use").IsFocused, "the focus did not go on to Use this machine");
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(0, state.Settings.Racer.Machine);
            Assert.True(garage.StagedInUse);
            Assert.True(garage.Selecting);
            Assert.False(main.Shell.GaragePage.Detail);
            Pump(() => Tiles(page)[0].IsFocused, "the focus did not go back to the machine raced");

            // the focus on a third, then the page left: SELECT stands on the machine raced again
            Focus(Tiles(page)[2]);
            Assert.Equal(2, garage.MachineIndex);
            Area(window, "Play");
            Assert.Equal(0, garage.MachineIndex);
            Assert.True(garage.Selecting);
            Assert.Equal(0, state.Settings.Racer.Machine);

            // the third's page: "Use this machine"
            page = Garage(window);
            Assert.False(main.Shell.GaragePage.Detail);                   // (a visit begins with the machines)
            Click(window, Tiles(page)[2]);
            Click(window, Named<Button>(page, "Use"));
            Assert.Equal(2, state.Settings.Racer.Machine);
            Assert.True(garage.StagedInUse);

            // its page again: in use, the focus on its setting; left and right step it by five, kept with its machine
            Click(window, Tiles(page)[2]);
            Assert.Equal("IN USE", Named<Button>(page, "Use").Content);
            Assert.False(Named<Button>(page, "Use").IsEffectivelyEnabled);
            var setting = Named<StepButton>(page, "Setting");
            Pump(() => setting.IsFocused, "the focus did not land on the setting");
            Assert.Equal("100", Named<TextBlock>(page, "SettingValue").Text);
            Press(window, Key.Right, PhysicalKey.ArrowRight);         // (at its end: nothing)
            Assert.Equal(100, garage.Setting);
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(90, garage.Setting);
            Assert.Equal("90", Named<TextBlock>(page, "SettingValue").Text);
            garage.SaveNow();
            Assert.Equal(90, state.Settings.Racer.LookOf(2).Setting);

            // the star: the machine on the Favourites shelf
            Click(window, Named<Button>(page, "Star"));
            Assert.Contains(2, state.Settings.FavouriteMachines ?? []);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Click(window, Named<Button>(page, "ShelfFavourites"));
            Assert.Single(Tiles(page));
            Assert.Equal("1", garage.ShelfTabs[GarageViewModel.FavouritesShelf].Count);
        });
    }

    [Fact]
    public async Task SortAndFilterLeaveOutMachinesAndTheColoursPaintThem()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var garage = main.Garage;
            var page = Garage(window);
            // Y: Sort & filter, over the page
            Press(window, Key.Y, PhysicalKey.Y);
            var sort = Assert.IsType<SortSheetViewModel>(main.Shell.Sheet);
            var sheet = Page<SortSheetView>(window);
            Assert.Equal(["Select", "Close"], Hints(main));
            Assert.Equal(2, sort.Ways.Count);
            Assert.Single(sort.Ways, w => w.On);
            Shot(window, state, "next-garage-sort");
            // a letter: only the machines with it (without the disc's stats, none), the dot on the button
            Click(window, Said(sheet, "Body A"));
            Assert.True(garage.Filtered);
            Assert.Empty(Tiles(page));
            Assert.Equal("No machine fits. Clear the search or the filter.", Named<TextBlock>(page, "Empty").Text);
            var clear = Said(sheet, "Clear the filter");
            clear.BringIntoView();                                         // (at the panel's foot)
            Click(window, clear);
            Assert.False(garage.Filtered);
            Assert.Equal(41, Tiles(page).Count);
            // another way round
            var other = sort.Ways.Single(w => !w.On);
            Click(window, Said(sheet, other.Label));
            Assert.Equal(other.Descending, garage.Sort.Descending);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(main.Shell.Sheet);
            Assert.Same(garage, main.Current);

            // the colours, on the machine's page: a colour painted on the machine, then back to its own
            Click(window, Tiles(page)[0]);
            Assert.True(main.Shell.GaragePage.Detail);
            Press(window, Key.Y, PhysicalKey.Y);                          // (Y is the machines' own: nothing here)
            Assert.Null(main.Shell.Sheet);
            Click(window, Named<Button>(page, "Colours"));
            var colours = Assert.IsType<ColourSheetViewModel>(main.Shell.Sheet);
            var panel = Page<ColourSheetView>(window);
            Assert.Equal(["Machine", "Trail"], colours.Targets.Select(t => t.Label));
            Click(window, Said(panel, "Paint it #2E9BE8"));
            Assert.Null(garage.StockColor);
            Assert.Equal("#2E9BE8", garage.ColorHex);
            Shot(window, state, "next-garage-colours");
            Click(window, Said(panel, "Back to the original"));
            Assert.Equal(0, garage.StockColor);
            // a colour of one's own: the wheel and the hex on a step of their own; B goes back to the palette
            Click(window, Said(panel, "Mix your own colour"));
            Assert.True(colours.Mixing);
            Assert.True(Named<TextBox>(panel, "HexBox").IsEffectivelyVisible);
            Assert.False(Named<ItemsControl>(panel, "Hues").IsEffectivelyVisible);
            Pump(() => Named<TextBox>(panel, "HexBox").IsFocused, "the focus did not go on to the hex");
            colours.Hex = "#2BC3C3";
            Assert.Equal("#2BC3C3", garage.ColorHex);
            window.Pad!.Act(new PadPressed(PadButton.B));                 // (Escape in the hex box is the box's own)
            Dispatcher.UIThread.RunJobs();
            Assert.False(colours.Mixing);
            Assert.Same(colours, main.Shell.Sheet);
            Click(window, Said(panel, "Back to the original"));
            Assert.Equal(0, garage.StockColor);
            // the trail: the racer's, whichever machine
            Click(window, Said(panel, "Trail"));
            Click(window, Said(panel, "Paint it #F2C230"));
            Assert.Equal("#F2C230", garage.Trail.ColorHex);
            garage.SaveNow();
            Assert.NotNull(state.Settings.Racer.Trail);
            Click(window, Said(panel, "Done"));
            Assert.Null(main.Shell.Sheet);
        });
    }

    [Fact]
    public async Task AMachineIsBuiltOnAnOptionPageSavedRacedAndRemoved()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var garage = main.Garage;
            var page = Garage(window);
            Click(window, Named<Button>(page, "ShelfCustom"));
            var tiles = Tiles(page);
            Assert.Equal(["New machine"], tiles.Select(AutomationProperties.GetName));
            Assert.Equal("No machines of your own yet. Build one with New machine.", Named<TextBlock>(page, "Empty").Text);

            // New machine: the builder's option page
            Click(window, tiles[0]);
            var builder = Assert.IsType<BuilderPageViewModel>(main.Shell.GaragePage.BuilderPage);
            Assert.True(main.Shell.GaragePage.Building);
            Assert.True(main.Shell.BackShown);
            Assert.Equal(["Change", "Select", "Next", "Back"], Hints(main));
            var view = Page<BuilderPageView>(window);
            Assert.Equal("NEW MACHINE", Named<TextBlock>(view, "Title").Text);
            Assert.Equal(["Body", "Cockpit", "Booster", "Pilot"], builder.Groups[0].Items.Select(i => i.Label));
            Assert.Equal(["Body colour", "Cockpit colour", "Booster colour", "Trail colour"], builder.Groups[1].Items.Select(i => i.Label));
            Shot(window, state, "next-garage-builder");
            // a part stepped with right: the build changes, its line in the help
            var body = view.GetVisualDescendants().OfType<OptionButton>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Body");
            Focus(body);
            Assert.Equal("Body", builder.Focused?.Label);
            Assert.Contains("frame", Named<TextBlock>(view, "RowHelp").Text, StringComparison.Ordinal);
            var first = garage.Builder.Build.Body;
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.NotEqual(first, garage.Builder.Build.Body);
            Assert.True(garage.Builder.Unsaved);
            // NEXT: the colours on a step of their own; a colour row: right goes to its next colour
            Assert.Equal("NEXT", Named<Button>(view, "Save").Content);
            Click(window, Named<Button>(view, "Save"));
            Assert.Equal(1, builder.Step);
            Assert.Equal(["Change", "Select", "Save", "Back"], Hints(main));
            Assert.Equal("Body colour", builder.Focused?.Label);
            var bodyColour = view.GetVisualDescendants().OfType<OptionButton>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Body colour");
            Focus(bodyColour);
            Assert.Equal("Its own", builder.Focused?.ValueText);

            // Save machine: kept, raced, the Garage again with it on the stage
            Click(window, Named<Button>(view, "Save"));
            Assert.False(main.Shell.GaragePage.Building);
            Assert.Single(state.Settings.Racer.Customs ?? []);
            Assert.NotNull(state.Settings.Racer.Custom);
            Assert.True(garage.StagedCustom);
            Assert.True(garage.StagedInUse);
            page = Page<GaragePageView>(window);
            Assert.Equal(2, Tiles(page).Count);                           // (the machine, and New machine)
            Assert.False(main.Shell.GaragePage.Detail);
            Click(window, Tiles(page)[0]);                                // (its page: Edit parts, Remove)
            Assert.True(Named<Button>(page, "EditParts").IsEffectivelyVisible);

            // Edit parts, and Back (Escape) without a change
            Click(window, Named<Button>(page, "EditParts"));
            Assert.Equal("EDIT MACHINE", Named<TextBlock>(Page<BuilderPageView>(window), "Title").Text);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.GaragePage.Building);
            Assert.True(main.Shell.GaragePage.Detail);                    // (back on its page)
            Assert.Same(garage, main.Current);

            // Remove: asked first, Escape keeps it; then removed, and the stock machine races again
            Click(window, Named<Button>(Page<GaragePageView>(window), "Remove"));
            Assert.True(main.Shell.AskingConfirm);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.Single(state.Settings.Racer.Customs ?? []);
            Click(window, Named<Button>(Page<GaragePageView>(window), "Remove"));
            Click(window, Named<Button>(Shell(window), "ConfirmDo"));
            Assert.Empty(state.Settings.Racer.Customs ?? []);
            Assert.Null(state.Settings.Racer.Custom);
            Assert.False(garage.StagedCustom);
            Assert.True(garage.StagedInUse);
        });
    }

    /// <summary>A colour row by itself: left and right go through the colours known, A opens every colour.</summary>
    [Fact]
    public void AColourRowStepsThroughItsColours()
    {
        var picked = new List<int>();
        var opened = 0;
        var row = new SwatchRule("Body colour", picked.Add, () => opened++);
        row.Show([new StockSwatch(0, "#111111", "1", true), new StockSwatch(1, "#222222", "2", false),
            new StockSwatch(2, "#5A6478", "3", false, Known: false), new StockSwatch(-1, "#333333", "Paint", false)]);
        var item = new OptionItem(row, "Colours");
        Assert.Equal("Its own", item.ValueText);
        Assert.Equal("#111111", item.SwatchHex);
        Assert.False(item.Step(-1));                                      // (at its first)
        Assert.True(item.Step(1));
        Assert.Equal([1], picked);
        row.Show([new StockSwatch(0, "#111111", "1", false), new StockSwatch(1, "#222222", "2", false),
            new StockSwatch(-1, "#333333", "Paint", true)]);
        Assert.Equal("Your paint", item.ValueText);
        Assert.False(item.Step(1));                                       // (the paint is the last)
        item.Press();
        Assert.Equal(1, opened);
    }
}
