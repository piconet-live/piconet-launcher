using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
/// The Garage's stats and its order, used as a player uses them: the launcher's window on a made-up disc
/// (<see cref="MadeUpStats"/>), the words on the heading row pressed, then a stat's row, then a machine's cell, with
/// the headless window's mouse. The stats come the whole way: the disc's car data, the skin's reading in the
/// background, the page. The order is kept in the settings and is the cells' only: the machine a cell stands for is
/// its id wherever the cell is.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class GarageSortTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets the window's thread work until <paramref name="done"/>, ten seconds at most (the stats are read
    /// on another thread and come back to this one).</summary>
    private static void Until(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        Dispatcher.UIThread.RunJobs();
        while (!done() && DateTime.UtcNow < limit)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), "the page did not get there in ten seconds");
    }

    /// <summary>The machine on show's PREVIEW, on its Stats tab, where a row pressed sorts by it.</summary>
    private static void StatsTab(GarageView view)
    {
        var garage = (GarageViewModel)view.DataContext!;
        if (!garage.Previewing)
        {
            garage.PreviewMachineCommand.Execute(garage.MachineIndex);
        }
        garage.Section = GarageViewModel.StatsSection;
        PumpFor(50);
    }

    private static Button Row(GarageView view, MachineStat stat) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is StatRow row && row.Stat == stat && b.Classes.Contains("stat"));

    private static int[] Shown(GarageView view) => [.. ShelfMachines(view)];

    /// <summary>The launcher's window on the Garage, as a start makes it; null stats until they are read.</summary>
    private static (MainWindow Window, MainViewModel Main, GarageView View) Open(AppState state)
    {
        GameSkin.Load(state.Settings.DiscPath);
        var main = new MainViewModel(state);
        var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
        window.Show();
        main.Current = main.Garage;
        Dispatcher.UIThread.RunJobs();
        return (window, main, window.GetVisualDescendants().OfType<GarageView>().Single());
    }

    private string DataWith(SyntheticDisc disc, string more = "")
    {
        var image = disc.Write(_dir, "garage.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(image));
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        var racer = """{"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}""";
        File.WriteAllText(Path.Combine(data, "settings.json"),
            "{\"discPath\": " + System.Text.Json.JsonSerializer.Serialize(image) + ", \"noServer\": true, \"racer\": " + racer + more + "}");
        return data;
    }

    private static async Task OnTheApplication(Action steps)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            var failure = await session.Dispatch(() =>
            {
                try
                {
                    steps();
                    return null;
                }
#pragma warning disable CA1031 // any failure is the test's result
                catch (Exception e)
#pragma warning restore CA1031
                {
                    return System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                }
                finally
                {
                    GameSkin.Load(null);                                       // the disc's file is let go
                }
            }, TestContext.Current.CancellationToken);
            failure?.Throw();
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    // the made-up weights: 400.5 + (7 * id) % 41, so the heaviest is 35 and the lightest 0
    private static readonly int[] HeaviestFirst = [.. Enumerable.Range(0, 41).OrderByDescending(m => (m * 7) % 41)];

    [Fact]
    public async Task AStatsRowPressedSortsTheCellsAndTheOrderIsKeptAndNoIdChanges()
    {
        var data = DataWith(MadeUpStats.Disc(MadeUpStats.CarData(), null));

        await OnTheApplication(() =>
        {
            var state = AppState.Load(data);
            var (window, main, view) = Open(state);
            var garage = main.Garage;
            Until(() => garage.HasStats);
            Assert.Equal("Sorted by number", garage.SortText);
            Assert.Equal(Enumerable.Range(0, 41), Shown(view));
            Assert.Equal(6, garage.MachineIndex);                              // the machine last raced
            Assert.False(garage.StatsShown);

            StatsTab(view);                                                    // the machine's PREVIEW, on its Stats tab
            Assert.True(garage.StatsShown);
            Assert.Equal(1, garage.Sections.Single(t => t.On).Index - GarageViewModel.ColourSection);
            var weight = Row(view, MachineStat.Weight);
            Assert.Equal("401.5 kg", garage.Stats.Single(s => s.Stat == MachineStat.Weight).Value);   // machine 6: 400.5 + 42 % 41
            Assert.Equal("", garage.Stats.Single(s => s.Stat == MachineStat.BodyRating).Value);       // no letters on this disc

            Click(window, weight);                                             // by weight: the heaviest first
            Assert.Equal("Sorted by weight, high to low", garage.SortText);
            Assert.Equal(HeaviestFirst, Shown(view));
            // no stat stands on a machine's cell: a cell is the picture, the name, the heart
            ForEachCard(view, cell => Assert.DoesNotContain(cell.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text != Machines.Names[((GarageCard)cell.DataContext!).Id]));
            // the weight of the machine selected is over the 3D view, as ever; sorted by another stat, that stat's
            // value for the machine selected stands there too, as a label and a number
            Assert.Equal([new StatFigure("", "401.5 kg")], garage.Summary);
            var summary = view.FindControl<ItemsControl>("Summary")!;
            Click(window, Row(view, MachineStat.MaxSpeed));
            Assert.Equal(MachineStat.MaxSpeed, garage.Sort.By);
            Assert.Equal([new StatFigure("", "401.5 kg"), new StatFigure(MachineStats.Of(MachineStat.MaxSpeed).Label, garage.Stats.Single(s => s.Stat == MachineStat.MaxSpeed).Value)],
                garage.Summary);
            Until(() => summary.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == MachineStats.Of(MachineStat.MaxSpeed).Label));
            Click(window, weight);                                             // and back to the weight, heaviest first
            Assert.Equal([new StatFigure("", "401.5 kg")], garage.Summary);
            Assert.Equal(new MachineSort(MachineStat.Weight, Descending: true), state.Settings.GarageSort);
            Assert.Equal(6, garage.MachineIndex);                              // the machine being set up is the same one

            Click(window, CardOf(view, Shown(view)[0])!);                                     // the first cell is machine 35, not machine 0
            Assert.Equal(35, garage.MachineIndex);
            Assert.Equal("440.5 kg", garage.Stats.Single(s => s.Stat == MachineStat.Weight).Value);   // its own value
            Assert.Equal(6, state.Settings.Racer.Machine);                     // shown in its PREVIEW, not raced

            Click(window, Row(view, MachineStat.Weight));                      // pressed again: the other way round
            Assert.Equal("Sorted by weight, low to high", garage.SortText);
            Assert.Equal(HeaviestFirst.Reverse(), Shown(view));
            Assert.Equal(new MachineSort(MachineStat.Weight), state.Settings.GarageSort);
            window.Close();
            main.Dispose();
            state.Shutdown();

            // the next start: the order is the player's, from the file
            Assert.Contains("\"by\": \"weight\"", File.ReadAllText(Path.Combine(data, "settings.json")), StringComparison.Ordinal);
            var again = AppState.Load(data);
            var (window2, main2, view2) = Open(again);
            Until(() => main2.Garage.HasStats);
            Assert.Equal("Sorted by weight, low to high", main2.Garage.SortText);
            Assert.Equal(HeaviestFirst.Reverse(), Shown(view2));

            StatsTab(view2);
            Click(window2, Row(view2, MachineStat.Number));                    // back to the game's order
            Assert.Equal("Sorted by number", main2.Garage.SortText);
            Assert.Equal(Enumerable.Range(0, 41), Shown(view2));
            Assert.Equal(MachineSort.ByNumber, again.Settings.GarageSort);
            Click(window2, Row(view2, MachineStat.Number));
            Assert.Equal("Sorted by number, 41 to 1", main2.Garage.SortText);
            Assert.Equal(Enumerable.Range(0, 41).Reverse(), Shown(view2));
            window2.Close();
            main2.Dispose();
            again.Shutdown();
        });
    }

    /// <summary>A disc whose car data is damaged: the Garage opens and works, says where the stats come from, and
    /// keeps the game's order whatever the settings ask for.</summary>
    [Fact]
    public async Task WithADamagedCarDataFileTheGarageStillWorksWithoutStats()
    {
        var damaged = MadeUpStats.CarData();
        damaged[4] = damaged[5] = damaged[6] = damaged[7] = 0xFF;              // machine 0's weight: not a number
        var data = DataWith(MadeUpStats.Disc(damaged, null), """, "garageSort": {"by": "maxSpeed", "descending": true}""");

        await OnTheApplication(() =>
        {
            var state = AppState.Load(data);
            Assert.Equal(new MachineSort(MachineStat.MaxSpeed, Descending: true), state.Settings.GarageSort);
            var (window, main, view) = Open(state);
            var garage = main.Garage;
            Until(() => GameSkin.MachineStatsAsync().IsCompleted);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(GameSkin.Problem);
            Assert.False(garage.HasStats);
            Assert.Empty(garage.Summary);
            Assert.Equal(Enumerable.Range(0, 41), Shown(view));                // no values: by number
            StatsTab(view);
            Assert.True(garage.StatsShown);
            Click(window, CardOf(view, Shown(view)[40])!);                                    // a machine is still chosen and set up
            Assert.Equal(40, garage.MachineIndex);
            Assert.Equal("Rainbow Phoenix", garage.MachineName);
            Click(window, Row(view, MachineStat.Number));
            Click(window, Row(view, MachineStat.Number));
            Assert.Equal(Enumerable.Range(0, 41).Reverse(), Shown(view));
            window.Close();
            main.Dispose();
            state.Shutdown();
        });
    }

    /// <summary>The player names another disc image in Settings while the launcher runs: the Garage shows that disc's
    /// stats and orders its cells by them, and with a disc that has none it goes back to the game's order. The
    /// pages are made as a start makes them (<see cref="Launcher.App.App.Pages"/>): the Garage reads the new disc's
    /// stats after the look has loaded it, which is the order that method keeps.</summary>
    [Fact]
    public async Task AnotherDiscNamedInSettingsGivesItsOwnStatsAndOrder()
    {
        var data = DataWith(MadeUpStats.Disc(MadeUpStats.CarData(), null), """, "garageSort": {"by": "weight", "descending": true}""");
        // a second disc whose weights fall with the number (machine 0 the heaviest), and a third without car data
        var second = MadeUpStats.Disc(MadeUpStats.CarData((m, s) => s == MachineStat.Weight ? 3000 - m : MadeUpStats.Value(m, s)), null)
            .Write(_dir, "second.iso");
        var third = new SyntheticDisc().Write(_dir, "third.iso");

        await OnTheApplication(() =>
        {
            var state = AppState.Load(data);
            var main = Launcher.App.App.Pages(state);
            var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
            window.Show();
            main.Current = main.Garage;
            var garage = main.Garage;
            var view = window.GetVisualDescendants().OfType<GarageView>().Single();
            string Weight() => garage.Stats.Single(s => s.Stat == MachineStat.Weight).Value;
            Until(() => garage.HasStats);
            Assert.Equal("401.5 kg", Weight());                                // machine 6 on the first disc
            Assert.Equal(HeaviestFirst, Shown(view));

            state.Update(s => s with { DiscPath = second });                   // as the Settings page keeps a new path
            Until(() => Weight() == "2994 kg");                                // machine 6 on the second: 3000 - 6
            Assert.True(garage.HasStats);
            Assert.Equal(Enumerable.Range(0, 41), Shown(view));                // its heaviest first is machine 0, 1, 2..
            Assert.Equal("Sorted by weight, high to low", garage.SortText);    // the player's choice stands
            StatsTab(view);
            Click(window, Row(view, MachineStat.Weight));                      // and turned round, by the second disc's values
            Assert.Equal(Enumerable.Range(0, 41).Reverse(), Shown(view));

            state.Update(s => s with { DiscPath = third });                    // a disc that gives no stats
            Until(() => !garage.HasStats);
            Assert.Equal("", Weight());
            Assert.Empty(garage.Summary);
            Assert.Equal(Enumerable.Range(0, 41), Shown(view));                // no values: the game's order
            Assert.Equal(new MachineSort(MachineStat.Weight), state.Settings.GarageSort);   // the choice is kept for a disc that has them

            state.Update(s => s with { DiscPath = second });                   // back: the stats and the order again
            Until(() => garage.HasStats && Weight() == "2994 kg");
            Assert.Equal(Enumerable.Range(0, 41).Reverse(), Shown(view));
            window.Close();
            main.Dispose();
            state.Shutdown();
        });
    }
}
