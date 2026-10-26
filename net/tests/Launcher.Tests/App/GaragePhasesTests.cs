using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The Garage in two phases: a tile pressed opens PREVIEW of its machine and races
/// nothing; USE THIS MACHINE races it and returns to SELECT with it ringed; Back (its button, Escape, the SELECT step)
/// returns with nothing changed; New machine, the Custom shelf's last tile, opens the builder on a new build; a saved
/// custom machine's tile opens it unraced, and its primary races it; the keys move across the roster, a letter jumps
/// to a machine, and nothing is picked by moving.</summary>
[Collection(HeadlessApp.Name)]
public sealed class GaragePhasesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task OnAWindow(int width, int height, Action<MainWindow, GarageView, GarageViewModel, AppState> steps)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "favouriteMachines": [3, 30], "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3},""" +
            """ "customs": [{"body": 2, "cockpit": 25, "booster": 50, "pilot": 29}, {"body": 4, "cockpit": 26, "booster": 51, "pilot": 6}]}}""");
        var state = AppState.Load(data);
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
                var view = window.GetVisualDescendants().OfType<GarageView>().Single();
                try
                {
                    steps(window, view, main.Garage, state);
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

    private static Button Tile(GarageView view, Func<object?, bool> which) =>
        view.RosterCards().First(b => which(b.DataContext));

    [Fact]
    public Task ATilePressedOpensItsPreviewAndRacesNothing() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        Assert.True(garage.Selecting);
        Assert.Contains("on", CardOf(view, 6)!.Classes);                                  // the machine raced, ringed
        Click(window, CardOf(view, 11)!);
        Assert.True(garage.Previewing);
        Assert.Equal(11, garage.MachineIndex);                                            // on the stage
        Assert.Equal(6, garage.ChosenIndex);
        Assert.Equal(6, state.Settings.Racer.Machine);                                    // not raced
        Assert.Equal([false, true], garage.PhaseSteps.Select(s => s.On));
        PumpFor(100);
        Assert.True(Named<Control>(view, "Stage").IsEffectivelyVisible);
        Assert.False(Named<Control>(view, "RosterPlace").IsEffectivelyVisible);
        Assert.True(Named<Button>(view, "Use").IsEffectivelyVisible);
    });

    [Fact]
    public Task UseThisMachineRacesItAndReturnsToSelectWithItRinged() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        Click(window, CardOf(view, 11)!);
        PumpFor(100);
        Click(window, Named<Button>(view, "Use"));
        Assert.True(garage.Selecting);
        Assert.Equal((11, 11), (garage.MachineIndex, state.Settings.Racer.Machine));
        Assert.Null(state.Settings.Racer.Custom);
        PumpFor(100);
        Assert.Contains("on", CardOf(view, 11)!.Classes);
        Assert.DoesNotContain("on", CardOf(view, 6)!.Classes);
        Assert.Single(view.RosterCards(), b => b.Classes.Contains("on"));
    });

    [Fact]
    public Task BackChangesNothing() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        var racer = state.Settings.Racer;
        // Back's button, Escape and the SELECT step
        foreach (var back in (Action[])[
            () => Click(window, Named<Button>(view, "BackButton")),
            () => Press(window, Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape),
            () => Click(window, view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is PhaseStep { Phase: GarageViewModel.SelectPhase })),
        ])
        {
            Click(window, Named<Button>(view, "ShelfFavourites"));
            Click(window, CardOf(view, 30)!);
            PumpFor(100);
            Assert.True(garage.Previewing);
            back();
            PumpFor(100);
            Assert.True(garage.Selecting);
            Assert.Equal(6, garage.MachineIndex);
            Assert.Equal(GarageViewModel.FavouritesShelf, garage.Shelf);                 // the shelf it stood on
            Assert.Equal(racer.Machine, state.Settings.Racer.Machine);
            Assert.Equal(racer.Custom, state.Settings.Racer.Custom);
        }
        // a saved custom machine looked at and Back: not raced either
        Click(window, Named<Button>(view, "ShelfCustom"));
        Click(window, Tile(view, c => c is CustomCard { Index: 1 }));
        Assert.True(garage.Previewing && garage.CustomShown);
        Click(window, Named<Button>(view, "BackButton"));
        Assert.Null(state.Settings.Racer.Custom);
    });

    [Fact]
    public Task NewMachineOpensTheBuilderOnANewBuild() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        Click(window, Named<Button>(view, "ShelfCustom"));
        PumpFor(100);
        var tiles = view.RosterCards().ToList();
        Assert.Equal(3, tiles.Count);                                                     // two saved, and New machine last
        Assert.IsType<NewMachineTile>(tiles[^1].DataContext);
        Click(window, tiles[^1]);
        Assert.True(garage.Previewing && garage.CustomShown);
        Assert.Null(garage.Builder.Editing);                                              // a new machine
        Assert.Equal((CustomBuild.First.Body, CustomBuild.First.Cockpit, CustomBuild.First.Booster),
            (garage.Builder.Build.Body, garage.Builder.Build.Cockpit, garage.Builder.Build.Booster));
        Assert.True(garage.Builder.Unsaved);
        // its primary keeps it and races it
        Click(window, Named<Button>(view, "Use"));
        Assert.True(garage.Selecting);
        Assert.Equal(3, state.Settings.Racer.Customs!.Count);
        Assert.Equal(CustomBuild.First.Body, state.Settings.Racer.Custom!.Body);
        PumpFor(100);
        Assert.Single(view.RosterCards(), b => b.Classes.Contains("on"));                // its tile ringed
        Assert.Contains("on", Tile(view, c => c is CustomCard { Index: 2 }).Classes);
    });

    [Fact]
    public Task ASavedCustomMachinesPrimaryRacesIt() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        Click(window, Named<Button>(view, "ShelfCustom"));
        Click(window, Tile(view, c => c is CustomCard { Index: 1 }));
        Assert.Equal(1, garage.Builder.Editing);
        Assert.Null(state.Settings.Racer.Custom);
        Click(window, Named<Button>(view, "Use"));
        Assert.Equal((4, 26, 51), (state.Settings.Racer.Custom!.Body, state.Settings.Racer.Custom.Cockpit, state.Settings.Racer.Custom.Booster));
        Assert.True(garage.Selecting);
        Assert.Equal(GarageViewModel.CustomShelf, garage.Shelf);
        PumpFor(100);
        Assert.Contains("on", Tile(view, c => c is CustomCard { Index: 1 }).Classes);
        Assert.DoesNotContain(view.RosterCards(), b => b.DataContext is GarageCard && b.Classes.Contains("on"));
    });

    /// <summary>The game's confirm sounds when a machine is confirmed (the same sound as the game's): USE
    /// THIS MACHINE, and Save racing a build; once for a new machine USE THIS MACHINE saves; never for a tile pressed
    /// or Back, which are navigation.</summary>
    [Fact]
    public Task TheConfirmSoundsOnUseAndSaveAndNotOnATile() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        var heard = 0;
        void Hear(Launcher.Core.LauncherSound sound, int volume)
        {
            if (sound == Launcher.Core.LauncherSound.Confirm)
            {
                heard++;
            }
        }
        Launcher.App.Attention.Sounded += Hear;
        try
        {
            Click(window, CardOf(view, 11)!);                                             // a tile: navigation
            Click(window, Named<Button>(view, "BackButton"));
            Assert.Equal(0, heard);
            Click(window, CardOf(view, 11)!);
            PumpFor(100);
            Click(window, Named<Button>(view, "Use"));                                    // USE THIS MACHINE
            Assert.Equal(1, heard);
            Click(window, Named<Button>(view, "ShelfCustom"));
            Click(window, Tile(view, c => c is NewMachineTile));
            Assert.Equal(1, heard);
            Click(window, Named<Button>(view, "Use"));                                    // a new machine: saved and raced, once
            Assert.Equal(2, heard);
            Click(window, Tile(view, c => c is CustomCard { Index: 0 }));
            garage.Builder.PickPartCommand.Execute(garage.Builder.Columns[0].Choices[7]);
            PumpFor(100);
            Click(window, Named<Button>(view, "Save"));                                   // Save: the build raced
            Assert.Equal(3, heard);
            Click(window, Named<Button>(view, "BackButton"));
            Assert.Equal(3, heard);
        }
        finally
        {
            Launcher.App.Attention.Sounded -= Hear;
        }
    });

    [Fact]
    public Task TheKeysMoveAcrossTheRosterAndALetterJumps() => OnAWindow(1280, 800, (window, view, garage, state) =>
    {
        var cards = PageCards(view);
        var columns = view.RosterColumns;
        Assert.True(columns >= 8, $"{columns} columns at 1280");
        Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
        cards[columns - 1].Focus();
        Press(window, Avalonia.Input.Key.Right, Avalonia.Input.PhysicalKey.ArrowRight);   // a row's end: the next row's first
        Assert.Same(cards[columns], Focused());
        Press(window, Avalonia.Input.Key.Up, Avalonia.Input.PhysicalKey.ArrowUp);
        Assert.Same(cards[0], Focused());
        Press(window, Avalonia.Input.Key.Left, Avalonia.Input.PhysicalKey.ArrowLeft);     // the first: it stays
        Assert.Same(cards[0], Focused());
        Assert.Equal(6, garage.MachineIndex);                                             // moving picks nothing
        Assert.True(garage.Selecting);
        // a letter: the next machine of it, in the roster's order, round again
        var bs = cards.Where(c => Machines.Names[((GarageCard)c.DataContext!).Id].StartsWith('B')).ToList();
        Assert.True(bs.Count >= 2);
        Press(window, Avalonia.Input.Key.B, Avalonia.Input.PhysicalKey.B);
        Assert.Same(bs[0], Focused());
        Press(window, Avalonia.Input.Key.B, Avalonia.Input.PhysicalKey.B);
        Assert.Same(bs[1], Focused());
        // Enter on a tile: its PREVIEW; Enter on the stage: the primary
        Press(window, Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
        var id = ((GarageCard)bs[1].DataContext!).Id;
        Assert.True(garage.Previewing);
        Assert.Equal(id, garage.MachineIndex);
        PumpFor(100);
        Press(window, Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
        Assert.True(garage.Selecting);
        Assert.Equal(id, state.Settings.Racer.Machine);
    });

    /// <summary>Every tile of every shelf at once at the least sizes and full HD: the roster does not scroll, and a
    /// wider window takes more columns.</summary>
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public Task TheWholeRosterShowsWithoutScrolling(int width, int height) => OnAWindow(width, height, (window, view, garage, state) =>
    {
        var page = view.FindAncestorOfType<ScrollViewer>()!;
        var foot = page.TranslatePoint(new Point(0, page.Bounds.Height), window)!.Value.Y;
        Assert.Equal(41, PageCards(view).Count);
        Assert.All(PageCards(view), t => Assert.True(t.TranslatePoint(new Point(0, t.Bounds.Height), window)!.Value.Y <= foot + 0.5,
            $"{width} x {height}: a tile ends under the page's foot"));
        Assert.True(page.Extent.Height <= page.Viewport.Height + 0.5, $"{width} x {height}: the page scrolls");
        Assert.True(view.RosterColumns >= (width >= 1920 ? 9 : 8), $"{width} x {height}: {view.RosterColumns} columns");
    });
}
