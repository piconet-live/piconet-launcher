using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The Garage keeps every region in its place and its order (tabs that move
/// positions confuse), in both phases. SELECT: the step line with the shelves, the heading row with the
/// Stats choice, the search and the chips, and the roster with its tiles' size stand where they stood on Stock on
/// Favourites and on Custom. PREVIEW: the step line with Back, the Stats choice, the stage with the 3D view and the name,
/// the panel floating on it with its tabs, the setting, Save and Remove and the primary stand where they stood for a
/// stock machine for a saved custom machine, a new one, a change not saved, and on every tab; a tab that does not
/// apply (a stock machine's parts) is there, dim.</summary>
[Collection(HeadlessApp.Name)]
public sealed class GarageRowsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>SELECT's regions, by name.</summary>
    private static readonly string[] SelectRows =
        ["StepLine", "Steps", "Shelves", "ShelfFavourites", "ShelfStock", "ShelfCustom", "Heading", "LensRow", "Filters", "Search", "SortFilter", "SortWords", "RosterPlace"];

    /// <summary>PREVIEW's regions, by name.</summary>
    private static readonly string[] PreviewRows =
    [
        "StepLine", "Steps", "BackButton", "Heading", "LensRow", "Stage", "Well", "NameLine", "SidePanel", "Side", "Sections", "SettingRow",
        "Actions", "Save", "Remove", "Use",
    ];

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "favouriteMachines": [6, 11], "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3},""" +
            """ "customs": [{"body": 2, "cockpit": 25, "booster": 50, "pilot": 29, "favourite": true}, {"body": 4, "cockpit": 26, "booster": 51, "pilot": 6}]}}""");
        return AppState.Load(data);
    }

    private async Task OnAWindow(int width, int height, Action<MainWindow, GarageView, GarageViewModel> steps)
    {
        var state = State();
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = width, Height = height };
                window.Show();
                main.Current = main.Garage;
                PumpFor(200);
                try
                {
                    steps(window, window.GetVisualDescendants().OfType<GarageView>().Single(), main.Garage);
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
            state.Shutdown();
            await ui.DisposeAsync();
        }
    }

    /// <summary>Each state's places of <paramref name="rows"/> (and of <paramref name="more"/>) compared with the
    /// first state's.</summary>
    private static void SamePlaces(MainWindow window, GarageView view, string[] rows, string size, List<(string Name, Action Go)> states,
        Func<Dictionary<string, Rect>>? more = null)
    {
        Dictionary<string, Rect>? first = null;
        string? firstName = null;
        foreach (var (name, go) in states)
        {
            go();
            PumpFor(150);
            var places = rows.ToDictionary(r => r, r => In(window, Named<Control>(view, r)));
            Assert.All(rows, r => Assert.True(Named<Control>(view, r).IsEffectivelyVisible, $"{size}, {name}: {r} is hidden"));
            foreach (var (key, rect) in more?.Invoke() ?? [])
            {
                places[key] = rect;
            }
            if (first is null)
            {
                (first, firstName) = (places, name);
                continue;
            }
            foreach (var (row, at) in places)
            {
                // the name's line as wide as the name: its place stays
                var was = first[row];
                var same = row == "NameLine" ? Near(was.TopLeft, at.TopLeft) && Math.Abs(was.Height - at.Height) < 0.5
                    : Near(was.TopLeft, at.TopLeft) && Near(was.BottomRight, at.BottomRight);
                Assert.True(same, $"{size}: {row} at {at} in \"{name}\", at {was} in \"{firstName}\"");
            }
        }
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public Task SelectKeepsEveryRowInItsPlaceOnEveryShelf(int width, int height) => OnAWindow(width, height, (window, view, garage) =>
    {
        Assert.True(garage.Selecting);
        SamePlaces(window, view, SelectRows, $"{width} x {height}",
        [
            ("Stock", () => Click(window, Named<Button>(view, "ShelfStock"))),
            ("Favourites", () => Click(window, Named<Button>(view, "ShelfFavourites"))),
            ("Custom", () => Click(window, Named<Button>(view, "ShelfCustom"))),
            ("Stock again", () => Click(window, Named<Button>(view, "ShelfStock"))),
        ], () =>
        {
            // the roster's first tile: in one place and of one size on every shelf (a shelf of a few keeps the size)
            var tiles = view.RosterCards().ToList();
            Assert.NotEmpty(tiles);
            return new() { ["first tile"] = In(window, tiles[0]) };
        });
    });

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public Task PreviewKeepsEveryRowInItsPlaceFromStockToCustom(int width, int height) => OnAWindow(width, height, (window, view, garage) =>
    {
        var builder = garage.Builder;
        void Open(string shelf, Func<Button> tile)
        {
            if (garage.Previewing)
            {
                Click(window, Named<Button>(view, "BackButton"));
                PumpFor(100);
            }
            Click(window, Named<Button>(view, shelf));
            PumpFor(100);
            Click(window, tile());
            PumpFor(100);
            Assert.True(garage.Previewing);
        }
        var states = new List<(string Name, Action Go)>
        {
            ("Stock", () => Open("ShelfStock", () => CardOf(view, 11)!)),
            ("Favourites", () => Open("ShelfFavourites", () => CardOf(view, 6)!)),
            ("Custom, a saved machine", () => Open("ShelfCustom", () => Card(view, 1))),
            ("Custom, a new machine", () => Open("ShelfCustom", () => Named<Button>(view, "NewMachineTile"))),
            ("Custom, a change not saved", () =>
            {
                Open("ShelfCustom", () => Card(view, 0));
                builder.PickPartCommand.Execute(builder.Columns[0].Choices[5]);
            }),
        };
        foreach (var tab in garage.Sections)
        {
            states.Add(($"Custom, {tab.Label}", () => Click(window, Tab(view, tab.Index))));
        }
        states.Add(("Stock again, Stats", () =>
        {
            Open("ShelfStock", () => CardOf(view, 11)!);
            Click(window, Tab(view, GarageViewModel.StatsSection));
        }));
        SamePlaces(window, view, PreviewRows, $"{width} x {height}", states, () =>
        {
            // the tabs: always the six, in one order, each in its place; a stock machine's parts dim
            var tabs = view.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is SectionTab).ToList();
            Assert.Equal(["Body", "Cockpit", "Booster", "Pilot", "Colour", "Stats"], tabs.Select(t => ((SectionTab)t.DataContext!).Label));
            Assert.All(tabs, t => Assert.True(t.IsEffectivelyVisible, "a tab is hidden"));
            Assert.All(tabs.Take(4), t => Assert.Equal(garage.CustomShown, t.IsEffectivelyEnabled));
            Assert.Single(tabs, t => t.Classes.Contains("on"));
            return tabs.Select((t, i) => (t, i)).ToDictionary(x => $"tab {x.i}", x => In(window, x.t));
        });
    });

    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5;

    /// <summary>A control's place in the window.</summary>
    private static Rect In(Window window, Control control) =>
        new(control.TranslatePoint(default, window)!.Value, control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value);

    /// <summary>A custom machine's tile.</summary>
    private static Button Card(GarageView view, int index) =>
        view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is CustomCard c && c.Index == index && b.IsEffectivelyVisible);

    private static Button Tab(GarageView view, int index) =>
        view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is SectionTab t && t.Index == index);
}
