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
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// Favourite machines, used as a player uses them in the launcher's own window:
/// the heart over the 3D view marks the machine being set up, and a marked machine's cell shows a heart that takes
/// no click (players picking a machine marked it by accident with the cell's own heart); the Favourites
/// shelf shows the marked ones only, in the order the cells are sorted in, with their
/// count on its tab; with nothing marked it says so; and the marks and the shelf are in the settings for the next
/// start.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class GarageFavouritesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static int[] Shown(GarageView view) => [.. ShelfMachines(view)];

    /// <summary>A machine's cell, its page of the strip turned to, laid out.</summary>
    private static Button Cell(GarageView view, int machine)
    {
        Pump(() => CardOf(view, machine) is { IsEffectivelyVisible: true }, $"machine {machine} has no cell");   // (a cell that just came is laid out first)
        return CardOf(view, machine)!;
    }

    /// <summary>Whether a machine's cell shows its heart: the mark of a favourite.</summary>
    private static bool ShowsHeart(GarageView view, int machine) => CardOf(view, machine) is not null &&
        view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(p => p.Classes.Contains("mark")
            && p.DataContext is GarageCard card && card.Id == machine && p.IsEffectivelyVisible);

    /// <summary>Back to SELECT from a PREVIEW (the shelves are SELECT's).</summary>
    private static void Back(Window window, GarageView view, GarageViewModel garage)
    {
        if (garage.Previewing)
        {
            Click(window, Named<Button>(view, "BackButton"));
        }
    }

    /// <summary>A machine's tile pressed (its PREVIEW), then the heart over the 3D view: marked, or no longer.</summary>
    private static void Mark(Window window, GarageView view, GarageViewModel garage, int machine)
    {
        Click(window, Cell(view, machine));
        Pump(() => garage.MachineIndex == machine, $"machine {machine} was not picked");
        Click(window, Named<Button>(view, "Heart"));
    }

    private static readonly int[] Marked = [12, 6, 31];
    private static readonly int[] Later = [40, 3];

    // the made-up weights: 400.5 + (7 * id) % 41
    private static int Weight(int machine) => (machine * 7) % 41;

    /// <summary>The Favourites shelf: the marked ones only, in the cells' order, its count on its tab, kept for the next
    /// start; unmarked there they leave it; marked on the Stock shelf they come.</summary>
    private static void OnTheShelf(Window window, GarageView view, GarageViewModel garage, AppState state)
    {
        var (favourites, stock) = (Named<Button>(view, "ShelfFavourites"), Named<Button>(view, "ShelfStock"));
        // the Favourites shelf: the marked ones only, in the cells' order (heaviest first), its count on its tab,
        // and the shelf kept for the next start
        Assert.Equal("3", garage.ShelfTabs[GarageViewModel.FavouritesShelf].Count);
        Back(window, view, garage);
        Click(window, favourites);
        Pump(() => ShelfMachines(view).Count == 3);
        Assert.Equal(Marked.OrderByDescending(Weight), Shown(view));
        Assert.True(state.Settings.FavouritesOnly && favourites.Classes.Contains("on"));
        // another order: the same three, sorted by it
        garage.SortByCommand.Execute(MachineStat.Weight);                      // the other way round
        Pump(() => Shown(view).SequenceEqual(Marked.OrderBy(Weight)));
        // a machine unmarked on the shelf leaves it; the last one unmarked, and the shelf is empty
        Mark(window, view, garage, 12);
        Pump(() => ShelfMachines(view).Count == 2);
        Mark(window, view, garage, 31);
        Mark(window, view, garage, 6);
        Pump(() => ShelfMachines(view).Count == 0 && garage.EmptyText == "No favourites");
        Assert.Empty(state.Settings.FavouriteMachines!);

        // marked on the Stock shelf, shown on the Favourites shelf
        Back(window, view, garage);
        Click(window, stock);
        Pump(() => ShelfMachines(view).Count == 41);
        Assert.Equal(Enumerable.Range(0, 41).OrderBy(Weight), Shown(view));
        Mark(window, view, garage, 40);
        Mark(window, view, garage, 3);
        Back(window, view, garage);
        Click(window, favourites);
        Pump(() => ShelfMachines(view).Count == 2);
    }

    [Fact]
    public async Task TheHeartMarksTheMachineAndTheFilterShowsTheMarkedOnesInTheSortedOrder()
    {
        var cancel = TestContext.Current.CancellationToken;
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "garageSort": {"by": "weight", "descending": true}, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}}""");
        var table = MachineStatTable.Of((machine, stat) => MadeUpStats.Value(machine, stat));
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                var state = AppState.Load(data);
                GameSkin.Load(null);
                var garage = new GarageViewModel(state, () => Task.FromResult<MachineStatTable?>(table));
                var view = new GarageView { DataContext = garage };
                var window = new Window { Content = new ScrollViewer { Content = view }, Width = 900, Height = 640 };
                window.Show();
                Pump(() => garage.HasStats && ShelfMachines(view).Count == 41);
                var all = Shown(view);
                Assert.Equal(Enumerable.Range(0, 41).OrderByDescending(Weight), all);

                // nothing marked: the Favourites shelf shows none and says so, and no cell shows a heart
                var favourites = Named<Button>(view, "ShelfFavourites");
                var stock = Named<Button>(view, "ShelfStock");
                Assert.Contains("on", stock.Classes);
                Assert.Null(state.Settings.FavouriteMachines);
                Assert.DoesNotContain(all, m => ShowsHeart(view, m));
                Click(window, favourites);
                Pump(() => ShelfMachines(view).Count == 0 && garage.EmptyText == "No favourites", "the empty Favourites shelf");
                Click(window, stock);
                Pump(() => ShelfMachines(view).Count == 41);

                // a click in a cell's lower left corner, where its heart's button was: the machine is picked, and
                // nothing is marked
                var cell = Cell(view, 12);
                var corner = cell.TranslatePoint(new Point(8, cell.Bounds.Height - 26), window)!.Value;
                window.MouseMove(corner);
                window.MouseDown(corner, Avalonia.Input.MouseButton.Left);
                window.MouseUp(corner, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Pump(() => garage.MachineIndex == 12, "the corner's click did not pick the machine");
                Assert.Null(state.Settings.FavouriteMachines);

                // the heart over the 3D view: the machine being set up is marked, its cell shows it, nothing moves
                var heart = Named<Button>(view, "Heart");
                Assert.DoesNotContain("on", heart.Classes);
                Click(window, heart);
                Assert.Contains("on", heart.Classes);
                Assert.Equal([12], state.Settings.FavouriteMachines!);
                Pump(() => ShowsHeart(view, 12), "the marked machine's cell shows no heart");
                Assert.Equal(all, Shown(view));                                         // marking moves nothing
                Mark(window, view, garage, 6);
                Mark(window, view, garage, 31);
                Assert.Equal([12, 6, 31], state.Settings.FavouriteMachines!);

                OnTheShelf(window, view, garage, state);
                window.Close();
            }, cancel);

            // the next start: the marks and the shelf are there
            Assert.Contains("\"favouriteMachines\"", File.ReadAllText(Path.Combine(data, "settings.json")), StringComparison.Ordinal);
            await ui.Dispatch(() =>
            {
                var state = AppState.Load(data);
                Assert.Equal([40, 3], state.Settings.FavouriteMachines!);
                Assert.True(state.Settings.FavouritesOnly);
                var garage = new GarageViewModel(state, () => Task.FromResult<MachineStatTable?>(table));
                Assert.Equal(GarageViewModel.FavouritesShelf, garage.Shelf);
                Assert.Equal(Later.OrderBy(Weight), garage.Choices.Select(c => c.Id));
                Assert.True(garage.Choices.All(c => c.Favourite));
            }, cancel);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }
}
