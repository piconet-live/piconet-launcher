using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The Garage, used as a player uses it in a headless
/// window: the Stats choice as a lens (a pack's value marked, the stock one beside it; the order, the chips and the
/// cells through it), fixed to the lobby's pack in a lobby; the lobby's machines only, "All 41" showing the others
/// greyed and never picked (by a press, the command or the arrow keys); the search box and the chips; a custom machine
/// marked in the builder on the Favourites shelf, and pressed there.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class GarageShelvesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // made-up weights: 1000 + 50 a machine (0 and 1 light, 2 to 11 mid, 12 on heavy); the letters MadeUpStats'
    private static float? Value(int machine, MachineStat stat) => stat switch
    {
        MachineStat.Weight => 1000 + (50 * machine),
        MachineStat.BodyRating => MadeUpStats.Rating(machine, 0),
        MachineStat.BoostRating => MadeUpStats.Rating(machine, 1),
        MachineStat.GripRating => MadeUpStats.Rating(machine, 2),
        _ => MadeUpStats.Value(machine, stat),
    };

    private static readonly MachineStatTable Table = MachineStatTable.Of(Value);

    private static StatPack Pack(string name, float weight) =>
        StatPack.Of(name, [StatEntry.Of(0, 0x04, weight)], "A Maker").Pack!;

    private static int[] Shown(GarageViewModel garage) => [.. garage.Choices.Select(c => c.Id)];

    private string Data(string more = "")
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}""" + more + "}}");
        return data;
    }

    private static async Task OnAWindow(string data, IReadOnlyList<StatPack> packs, Action<Window, GarageView, GarageViewModel, AppState> steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var state = AppState.Load(data);
                GameSkin.Load(null);
                var garage = new GarageViewModel(state, () => Task.FromResult<MachineStatTable?>(Table), packs);
                var view = new GarageView { DataContext = garage };
                var window = new Window { Content = new ScrollViewer { Content = view }, Width = 1280, Height = 720 };
                window.Show();
                Pump(() => garage.HasStats && ShelfMachines(view).Count == 41);
                steps(window, view, garage, state);
                window.Close();
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    /// <summary>SELECT's Sort &amp; filter panel: its button and the order's words both open
    /// it; a stat chosen orders the roster by it, best or largest first, and the switch turns it round, the words
    /// beside the Stats choice saying so and the Stats tab's rows marking the same order; letters of body and grip
    /// leave the machines with any of the body's and the grip's; Escape closes it; the heading row's regions stay
    /// where they were.</summary>
    [Fact]
    public Task TheSortAndFilterPanelOrdersAndNarrowsTheRoster() => OnAWindow(Data(), [], (window, view, garage, state) =>
    {
        int[] heaviest = [.. Enumerable.Range(0, 41).Reverse()];                        // (1000 + 50 a machine)
        Assert.Equal(Enumerable.Range(0, 41), Shown(garage));
        string[] rows = ["LensRow", "Search", "SortFilter", "RosterPlace"];
        Point At(string name) => Named<Control>(view, name).TranslatePoint(default, window)!.Value;
        var before = rows.ToDictionary(r => r, At);
        Button In(Func<object?, bool> which) => view.GetVisualDescendants().OfType<Button>().Single(b => which(b.DataContext) && b.IsEffectivelyVisible);

        // the button opens it; a stat chosen: the roster by it, heaviest first
        Assert.False(Named<Control>(view, "SortFilterPanel").IsEffectivelyVisible);
        Click(window, Named<Button>(view, "SortFilter"));
        Assert.True(Named<Control>(view, "SortFilterPanel").IsEffectivelyVisible);
        Assert.Equal(MachineStats.Shown.Count, view.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is SortChoice));
        Click(window, In(c => c is SortChoice { Stat: MachineStat.Weight }));
        Pump(() => Shown(garage).SequenceEqual(heaviest));
        Assert.Equal("Sorted by weight, high to low", garage.SortText);
        Assert.Equal("high to low", garage.SortOrderText);
        Assert.True(garage.Stats.Single(r => r.Stat == MachineStat.Weight).Sorted);         // the Stats tab's mark too
        Assert.Single(garage.SortChoices, c => c.On);
        Click(window, Named<Button>(view, "SortWay"));                                        // the other way round
        Pump(() => Shown(garage).SequenceEqual(heaviest.Reverse()));
        Assert.Equal("Sorted by weight, low to high", garage.SortText);
        Assert.Equal(new MachineSort(MachineStat.Weight), state.Settings.GarageSort);

        // letters: body A or B, and grip A
        Click(window, In(c => c is LetterChip { Stat: MachineStat.BodyRating, Index: 0 }));
        Click(window, In(c => c is LetterChip { Stat: MachineStat.BodyRating, Index: 1 }));
        int[] bodyAB = [.. Enumerable.Range(0, 41).Where(m => MadeUpStats.Rating(m, 0) is 1 or 2)];
        Pump(() => Shown(garage).SequenceEqual(bodyAB));
        Click(window, In(c => c is LetterChip { Stat: MachineStat.GripRating, Index: 0 }));
        int[] andGripA = [.. bodyAB.Where(m => MadeUpStats.Rating(m, 2) == 1)];
        Pump(() => Shown(garage).SequenceEqual(andGripA));
        Assert.Equal("3", garage.FilterCountText);
        Assert.Equal(andGripA, ShelfMachines(view));                                          // the roster's tiles
        Click(window, In(c => c is LetterChip { Stat: MachineStat.GripRating, Index: 0 }));   // pressed again: off
        Pump(() => Shown(garage).SequenceEqual(bodyAB));

        // Escape closes it; the order's words open it again, still SELECT; its button closes it
        Press(window, Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        Assert.False(garage.SortFilterOpen);
        Assert.Equal(bodyAB, Shown(garage));
        Click(window, Named<Button>(view, "SortWords"));
        Assert.True(garage.SortFilterOpen && garage.Selecting);
        Click(window, Named<Button>(view, "SortFilter"));
        Assert.False(garage.SortFilterOpen);
        Assert.All(rows, r => Assert.Equal(before[r], At(r)));
    });

    [Fact]
    public Task TheStatsChoiceIsALensAndALobbyFixesItsOwn() => OnAWindow(Data(), [Pack("Made Up", 5000)], (window, view, garage, state) =>
    {
        var lens = Named<ComboBox>(view, "Lens");
        Assert.Equal(["Stock", "Made Up"], garage.Lenses);
        Assert.True(lens.IsEffectivelyVisible);
        garage.Select(0);
        var weight = garage.Stats.Single(s => s.Stat == MachineStat.Weight);
        Assert.Equal(("1000 kg", false, ""), (weight.Value, weight.Changed, weight.Stock));

        // the pack chosen: its value, marked, with the stock one beside it
        lens.SelectedIndex = 1;
        Pump(() => weight.Value == "5000 kg");
        Assert.Equal((true, "1000 kg", "Made Up · A Maker"), (weight.Changed, weight.Stock, garage.PackText));
        Assert.Equal("Made Up", state.Settings.GarageStats);                                // kept for the next start
        Assert.Equal(1, new GarageViewModel(state, () => Task.FromResult<MachineStatTable?>(Table), [Pack("Made Up", 5000)]).LensIndex);
        Assert.False(garage.Stats.Single(s => s.Stat == MachineStat.Acceleration).Changed);   // what the pack leaves
        Assert.Equal(5000f, garage.Builder.Pack!.Entries.Single().Value);                     // and the builder's too

        // in a lobby on another pack: the lobby's, fixed and named, whatever was chosen
        garage.InLobby(null, SettingRange.Only(100), Pack("Lobby Pack", 3000), "Lobby Pack · A Maker");
        Pump(() => weight.Value == "3000 kg");
        Assert.True(garage.LensFixed);
        Assert.Equal("Lobby Pack · A Maker", garage.LensFixedText);
        // no Stats box in a lobby's heading row: the pack is a race rule, named on the lobby's course band (among
        // the rest it was cut off)
        Dispatcher.UIThread.RunJobs();
        Assert.False(Named<Control>(view, "LensRow").IsEffectivelyVisible);
        Assert.False(lens.IsEffectivelyVisible);
        // a lobby on the disc's stats: Stock, fixed
        garage.InLobby(null, SettingRange.Only(100));
        Pump(() => weight.Value == "1000 kg");
        Assert.Equal(("Stock", false, ""), (garage.LensFixedText, weight.Changed, garage.PackText));
        // out of the lobby: the player's choice again
        garage.InLobby(null, null);
        Pump(() => weight.Value == "5000 kg");
        Assert.True(lens.IsEffectivelyVisible);
    });

    [Fact]
    public Task ALobbyShowsItsOwnMachinesAndAll41ShowsTheOthersGreyedAndNeverPicked() => OnAWindow(Data(), [], (window, view, garage, state) =>
    {
        int[] offer = [6, 0, 19];
        garage.InLobby(offer, null);
        Pump(() => ShelfMachines(view).Count == 3);
        Assert.Equal([0, 6, 19], Shown(garage));
        Assert.Equal("3 / 41", garage.ShelfTabs[GarageViewModel.StockShelf].Count);
        var all = Named<Button>(view, "ShowAll");
        Assert.True(all.IsEffectivelyVisible);

        // "All 41": every machine, the ones not offered greyed (not enabled), in the same order
        Click(window, all);
        Pump(() => ShelfMachines(view).Count == 41);
        Assert.Contains("on", all.Classes);
        ForEachCard(view, cell => Assert.Equal(offer.Contains(((GarageCard)cell.DataContext!).Id), cell.IsEnabled));

        // a greyed machine is never opened nor picked: not by a press, not by the commands
        Click(window, CardOf(view, 10)!);
        Assert.Equal(6, garage.MachineIndex);
        Assert.True(garage.Selecting);
        garage.PreviewMachineCommand.Execute(10);
        Assert.True(garage.Selecting);
        garage.PickMachineCommand.Execute(10);
        Assert.Equal((6, 6), (garage.MachineIndex, state.Settings.Racer.Machine));

        // off again: the three; a lobby that offers all: no switch
        Click(window, all);
        Pump(() => ShelfMachines(view).Count == 3);
        garage.InLobby([.. Enumerable.Range(0, 41)], null);
        Pump(() => ShelfMachines(view).Count == 41);
        Assert.False(all.IsEffectivelyVisible);
        Assert.Equal("41", garage.ShelfTabs[GarageViewModel.StockShelf].Count);
    });

    [Fact]
    public Task TheSearchAndTheLettersLeaveTheMachinesThatMatch() => OnAWindow(Data(), [], (window, view, garage, state) =>
    {
        var search = Named<TextBox>(view, "Search");
        search.Text = "WILD";                                                            // any case: Wild Goose, Wild Boar
        Pump(() => ShelfMachines(view).Count == 2);
        Assert.Equal([5, 23], Shown(garage));
        search.Text = "boar wild";                                                       // every word, any order
        Pump(() => ShelfMachines(view).Count == 1);
        search.Text = "";
        Pump(() => ShelfMachines(view).Count == 41);

        Letter(0);                                                                      // body A (Sort & filter)
        var bodyA = Enumerable.Range(0, 41).Where(m => MadeUpStats.Rating(m, 0) == 1).ToArray();
        Pump(() => Shown(garage).SequenceEqual(bodyA));
        search.Text = "nothing like it";
        Pump(() => ShelfMachines(view).Count == 0);
        Assert.Equal(("No match", true), (garage.EmptyText, Named<TextBlock>(view, "Empty").IsEffectivelyVisible));

        void Letter(int letter)
        {
            Click(window, Named<Button>(view, "SortFilter"));
            Click(window, view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.DataContext is LetterChip c && c.Stat == MachineStat.BodyRating && c.Index == letter));
            Press(window, Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        }
    });

    [Fact]
    public Task ACustomMachineMarkedInTheBuilderIsOnTheFavouritesShelf() => OnAWindow(
        Data(""", "customs": [{"body": 2, "cockpit": 25, "booster": 50, "pilot": 29}, {"body": 4, "cockpit": 26, "booster": 51, "pilot": 6}]"""),
        [], (window, view, garage, state) =>
    {
        Click(window, Named<Button>(view, "ShelfCustom"));
        Pump(() => garage.CustomShown);
        Click(window, view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is CustomCard { Index: 1 } && b.IsEffectivelyVisible));
        Pump(() => garage.Builder.Editing == 1);
        var heart = Named<Button>(view, "BuildHeart");
        Click(window, heart);
        Pump(() => heart.Classes.Contains("on"));
        Assert.Equal([false, true], state.Settings.Racer.Customs!.Select(c => c.Favourite));
        Assert.Equal("1", garage.ShelfTabs[GarageViewModel.FavouritesShelf].Count);

        // the Favourites shelf: the marked custom machine; pressed, it is opened (on the Custom shelf), and its
        // primary races it
        Click(window, Named<Button>(view, "BackButton"));
        Click(window, Named<Button>(view, "ShelfFavourites"));
        Pump(() => garage.ShelfCustoms.Count == 1 && ShelfMachines(view).Count == 0);
        Click(window, Named<Button>(view, "ShelfStock"));
        Pump(() => ShelfMachines(view).Count == 41);
        Click(window, CardOf(view, 10)!);
        Click(window, Named<Button>(view, "Use"));
        Pump(() => state.Settings.Racer.Custom is null);                                 // a stock machine raced
        Click(window, Named<Button>(view, "ShelfFavourites"));
        Button Marked() => view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is CustomCard && b.IsEffectivelyVisible
            && b.FindAncestorOfType<ItemsControl>()?.Name == "Roster");
        Click(window, Marked());
        Pump(() => garage.CustomShown && garage.Builder.Editing == 1);
        Assert.Null(state.Settings.Racer.Custom);                                        // opened, not raced
        Click(window, Named<Button>(view, "Use"));
        Pump(() => state.Settings.Racer.Custom is { Body: 4 });
        Assert.Equal(GarageViewModel.FavouritesShelf, garage.Shelf);                    // SELECT again, on the shelf it came from
        Click(window, Marked());
        Pump(() => garage.Previewing && garage.Builder.Editing == 1);

        // unmarked: off the shelf, and the mark is the card's, not the machine's (no change to save)
        Click(window, heart);
        Pump(() => !state.Settings.Racer.Customs![1].Favourite);
        Assert.False(garage.Builder.Unsaved);
        Assert.Equal("0", garage.ShelfTabs[GarageViewModel.FavouritesShelf].Count);
    });

    /// <summary>The left column shows the raced custom machine in its colours as the builder shows them: the
    /// saved machine's paint, and a new paint once it is saved.</summary>
    [Fact]
    public async Task TheLeftColumnShowsTheRacedCustomMachineInItsColours()
    {
        var data = Data(""", "custom": {"body": 2, "cockpit": 25, "booster": 50, "pilot": 29}""" +
            """, "customs": [{"body": 2, "cockpit": 25, "booster": 50, "pilot": 29, "colours": [{"stock": null, "paint": {"r": 232, "g": 67, "b": 46}}, {"stock": 0}, {"stock": 0}]}]""");
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var state = AppState.Load(data);
                GameSkin.Load(null);
                using var main = new MainViewModel(state);
                PumpFor(100);
                Assert.Equal(new Launcher.Core.CustomBuild(2, 25, 50, 29), main.ShownCustom);
                Assert.Equal([new Rgb(232, 67, 46), null, null], main.ShownPartColours);

                // repainted and saved in the builder: the column shows the new paint
                main.Garage.Builder.Build = main.Garage.Builder.Build.WithColour(0, new PartColour(null, new Rgb(10, 20, 200)));
                main.Garage.Builder.SaveCommand.Execute(null);
                Pump(() => main.ShownPartColours[0] == new Rgb(10, 20, 200), "the column kept the old paint");
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }
}
