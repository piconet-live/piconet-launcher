using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Launcher.Core.Pads;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The pad working the launcher's window, its intents given to the window's navigator as
/// the pad's reading gives them: the first press shows where the pad is and does nothing else; the D-pad moves the
/// focus by position; L and R go through the tabs and stop at their ends; Z through a tab's pages; B goes one step
/// up and closes the question over the page; Start presses the page's main action; left and right change a value
/// and a held direction never leaves it; a text box and the name (it needs the keyboard) are never stopped on; the
/// mouse hides the pad's mark. With "Navigation sounds" on, a move sounds the game's cursor and B's step up its
/// cancel; with them off (the default), and for the mouse always, nothing sounds.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class PadNavigationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(string settings)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), settings);
        return AppState.Load(data);
    }

    private Task OnAWindow(Action<MainWindow, MainViewModel, PadNavigator> steps) =>
        OnAWindow("""{"noServer": true, "soundMuted": true}""", steps);

    private async Task OnAWindow(string settings, Action<MainWindow, MainViewModel, PadNavigator> steps)
    {
        var state = State(settings);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 860 };
                window.Show();
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                try
                {
                    steps(window, main, window.Pad!);
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

    private static void Do(PadNavigator pad, PadIntent intent)
    {
        pad.Act(intent);
        Dispatcher.UIThread.RunJobs();
    }

    private static PadMoved Press(PadMove move) => new(move, Repeat: false);

    private static PadPressed Press(PadButton button) => new(button);

    /// <summary>Settings' "Controller navigation" (Launcher; off by default): off, a press of the pad does nothing to the
    /// window; on, it works the pages, and turning it off again stops it at once.</summary>
    [Fact]
    public Task ThePadWorksThePagesOnlyWithControllerNavigationOn() => OnAWindow((window, main, pad) =>
    {
        static HelperPad Reading(string[]? buttons = null) =>
            new(0, 6, true, new HashSet<string>(buttons ?? []), 128, 128, 128, 128, 0, 0);
        void Read(string button)
        {
            pad.Read(Reading([button]));
            pad.Read(Reading());
            Dispatcher.UIThread.RunJobs();
        }

        Assert.False(main.Settings.PadNavigation);
        Read("Down");
        Read("R");
        Assert.False(pad.Showing);
        Assert.Same(main.Home, main.Current);

        main.Settings.PadNavigation = true;
        Read("Down");
        Assert.True(pad.Showing);
        Read("R");
        Assert.Same(main.Garage, main.Current);

        main.Settings.PadNavigation = false;
        Read("L");
        Assert.Same(main.Garage, main.Current);
    });

    [Fact]
    public Task TheFirstPressShowsThePadAndTheShouldersGoThroughTheTabs() => OnAWindow((window, main, pad) =>
    {
        Assert.False(pad.Showing);
        Do(pad, Press(PadMove.Down));
        Assert.True(pad.Showing);
        Assert.Same(main.Home, main.Current);                       // the first press only shows where the pad is
        var first = Assert.IsAssignableFrom<Control>(pad.On);
        Assert.True(first.IsFocused);

        Do(pad, Press(PadButton.L));
        Assert.Same(main.Home, main.Current);                       // Play is the first tab
        Do(pad, Press(PadButton.R));
        Assert.Same(main.Garage, main.Current);
        Assert.True(window.FindControl<ContentControl>("PageHost")!.IsVisualAncestorOf(pad.On!), "the pad lands in the page");
        Do(pad, Press(PadButton.R));
        Assert.Same(main.Replays, main.Current);
        Do(pad, Press(PadButton.R));
        Assert.Equal("settings", main.Tab);
        var page = main.ShownSettingsTab;
        Do(pad, Press(PadButton.R));
        Assert.Equal(page, main.ShownSettingsTab);                  // the last tab: R does nothing
        Do(pad, Press(PadButton.Z));
        Assert.NotEqual(page, main.ShownSettingsTab);               // Settings' next page

        Do(pad, Press(PadButton.B));
        Assert.Equal("play", main.Tab);
        Do(pad, Press(PadButton.Z));
        Assert.Same(main.Lobbies, main.Current);                    // Play's next page, Custom lobbies (no Local network tab)
        Do(pad, Press(PadButton.B));
        Assert.Same(main.Home, main.Current);                       // one step up
        Do(pad, Press(PadButton.B));
        Assert.Same(main.Home, main.Current);                       // and no further
    });

    /// <summary>The navigation's sounds (the game's cursor and cancel), as Settings' "Navigation sounds" has them: a
    /// move of the pad between controls and the keyboard's Tab sound the cursor, the pad's B that goes a step up the
    /// cancel; the pad's first press, a page it lands on, a B that goes nowhere and a click sound nothing. Off: none
    /// of them sounds.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheNavigationSoundsAreTheCursorAndTheCancelWhenSwitchedOn(bool on) =>
        OnAWindow($$"""{"noServer": true, "navigationSounds": {{(on ? "true" : "false")}}}""", (window, main, pad) =>
    {
        Assert.Equal(on, main.SoundSettings.NavigationSounds);
        var heard = new List<LauncherSound>();
        // (the navigation's two only: a test of another collection may sound its lobby meanwhile)
        void Hear(LauncherSound sound, int volume)
        {
            if (sound is LauncherSound.Cursor or LauncherSound.Back)
            {
                heard.Add(sound);
            }
        }
        Attention.Sounded += Hear;
        try
        {
            Do(pad, Press(PadMove.Down));
            Assert.Empty(heard);                                        // the first press only shows the pad
            var moves = 0;
            foreach (var move in new[] { PadMove.Down, PadMove.Right, PadMove.Down, PadMove.Left })
            {
                var before = pad.On;
                Do(pad, Press(move));
                moves += pad.On != before ? 1 : 0;
            }
            Assert.True(moves > 0, "the pad never moved on Play");
            Assert.Equal(on ? Enumerable.Repeat(LauncherSound.Cursor, moves) : [], heard);

            heard.Clear();
            Do(pad, Press(PadButton.Z));                                // Play's next page: the pad lands there, unheard
            Assert.Same(main.Lobbies, main.Current);                    // (Custom lobbies; no Local network tab for now)
            Do(pad, Press(PadButton.B));                                // one step up
            Assert.Same(main.Home, main.Current);
            Do(pad, Press(PadButton.B));                                // and no further: nothing to hear
            Assert.Equal(on ? [LauncherSound.Back] : [], heard);

            heard.Clear();
            Headless.Press(window, Key.Tab, PhysicalKey.Tab);           // the keyboard's Tab
            Assert.Equal(on ? [LauncherSound.Cursor] : [], heard);

            heard.Clear();
            Click(window, window.FindControl<Button>("SettingsButton")!);  // the mouse: never
            Assert.Equal("settings", main.Tab);
            Assert.Empty(heard);
        }
        finally
        {
            Attention.Sounded -= Hear;
        }
    });

    /// <summary>On the Garage's roster (SELECT): left and right on a tile move to the next tile, a row's
    /// end wrapping to the next row's first (left from a row's first tile leaves the roster, as anywhere), and pick
    /// nothing; L and R on a tile go through the shelves (the pad on the tile of the
    /// machine raced, else the shelf's first), and stop at their ends; A opens PREVIEW of the tile's machine (nothing
    /// raced), B goes back to SELECT, the pad on the machine raced; off the roster the shoulders go through the tabs
    /// as anywhere.</summary>
    [Fact]
    public Task OnTheGaragesRosterTheDPadMovesTheShouldersChangeShelvesAndBGoesBack() => OnAWindow((window, main, pad) =>
    {
        main.Current = main.Garage;
        PumpFor(200);
        var garage = main.Garage;
        var view = window.GetVisualDescendants().OfType<GarageView>().Single();
        Pump(() => PageCards(view).Count == Machines.Names.Count);
        var raced = garage.MachineIndex;
        var cards = PageCards(view);
        var columns = view.RosterColumns;
        var rowEnd = cards[columns - 1];                                        // the first row's last tile
        rowEnd.Focus();
        Do(pad, Press(PadMove.Right));                                          // (the first press shows the pad)
        Assert.Same(rowEnd, pad.On);

        Do(pad, Press(PadMove.Right));                                          // past the row's end: the next row's first
        Assert.Same(cards[columns], pad.On);
        Do(pad, Press(PadMove.Right));
        Assert.Same(cards[columns + 1], pad.On);
        Do(pad, Press(PadMove.Left));
        Assert.Same(cards[columns], pad.On);
        Assert.Equal(raced, garage.MachineIndex);                               // moving picks nothing
        Assert.True(garage.Selecting);

        Do(pad, Press(PadButton.R));                                            // the next shelf (Custom), not the next tab
        Assert.Same(main.Garage, main.Current);
        Pump(() => garage.Shelf == GarageViewModel.CustomShelf && pad.On is Button { DataContext: NewMachineTile });
        Do(pad, Press(PadButton.R));                                            // the last shelf: it stays
        Assert.Equal(GarageViewModel.CustomShelf, garage.Shelf);
        Do(pad, Press(PadButton.L));
        Pump(() => garage.Shelf == GarageViewModel.StockShelf && pad.On is Button { DataContext: GarageCard card } && card.Id == raced,
            "the pad did not land on the machine raced");

        // A on a tile: its PREVIEW; B: SELECT again, nothing raced, the pad on the machine raced
        var other = CardOf(view, raced == 0 ? 1 : 0)!;
        other.Focus();
        Do(pad, Press(PadMove.Right));
        Do(pad, Press(PadMove.Left));
        Assert.Same(other, pad.On);
        Do(pad, Press(PadButton.A));
        Pump(() => garage.Previewing);
        Assert.Equal(((GarageCard)other.DataContext!).Id, garage.MachineIndex);
        Assert.Equal(raced, main.Garage.ChosenIndex);
        Do(pad, Press(PadButton.B));
        Pump(() => garage.Selecting && pad.On is Button { DataContext: GarageCard card } && card.Id == raced);
        Assert.Same(main.Garage, main.Current);                                 // B went back a phase, not a page
        Assert.Equal(raced, garage.MachineIndex);

        // off the roster (the Stats choice), the shoulders are the tabs' again
        var lens = Named<ComboBox>(view, "Lens");
        lens.Focus();
        Do(pad, Press(PadMove.Up));
        Pump(() => pad.On is not null && !GarageView.IsCard(pad.On));
        Do(pad, Press(PadButton.R));
        Assert.Same(main.Replays, main.Current);
    });

    /// <summary>The Garage's Sort &amp; filter panel with the pad: A on its button opens it and the pad lands in it and
    /// stays in it (its stats and letters), A presses a letter, B closes it with the pad back on its button.</summary>
    [Fact]
    public Task TheSortAndFilterPanelOpensWithAAndClosesWithB() => OnAWindow((window, main, pad) =>
    {
        main.Current = main.Garage;
        PumpFor(200);
        var garage = main.Garage;
        var view = window.GetVisualDescendants().OfType<GarageView>().Single();
        var button = Named<Button>(view, "SortFilter");
        var panel = Named<Control>(view, "SortFilterPanel");
        button.Focus();
        Do(pad, Press(PadButton.A));                                            // (the first press shows the pad where the focus is)
        Pump(() => ReferenceEquals(pad.On, button), "the pad is not on Sort & filter");
        Do(pad, Press(PadButton.A));
        Pump(() => garage.SortFilterOpen && pad.On is not null && panel.IsVisualAncestorOf(pad.On), "the pad did not land in the panel");
        for (var i = 0; i < 12; i++)
        {
            Do(pad, Press(PadMove.Down));                                       // down through it: never out of it
            Assert.True(panel.IsVisualAncestorOf(pad.On!), "the pad left the panel");
        }
        Assert.IsType<LetterChip>(pad.On!.DataContext);                         // (the letters are its foot)
        var letter = (LetterChip)pad.On.DataContext!;
        Do(pad, Press(PadButton.A));
        Assert.True(letter.On);
        Do(pad, Press(PadButton.B));
        Pump(() => !garage.SortFilterOpen && ReferenceEquals(pad.On, button), "B did not close the panel onto its button");
        Assert.Same(main.Garage, main.Current);
    });

    [Fact]
    public Task TheDPadMovesByPositionAndAPresses() => OnAWindow((window, main, pad) =>
    {
        // the places in the left column: Play, Garage, Replays, top to bottom (direction D)
        var tabs = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tab")).ToList();
        Assert.Equal(3, tabs.Count);
        tabs[0].Focus();
        Do(pad, Press(PadMove.Down));                                // shows the pad where the focus is
        Assert.Same(tabs[0], pad.On);
        Do(pad, Press(PadMove.Down));
        Assert.Same(tabs[1], pad.On);
        Do(pad, Press(PadMove.Down));
        Assert.Same(tabs[2], pad.On);
        Do(pad, Press(PadButton.A));
        Assert.Same(main.Replays, main.Current);
        Do(pad, Press(PadMove.Up));
        Do(pad, Press(PadButton.A));
        Assert.Same(main.Garage, main.Current);

        // right from the places goes into the page, and left comes back
        tabs[1].Focus();
        Do(pad, Press(PadMove.Right));
        var inPage = pad.On!;
        Assert.True(window.FindControl<ContentControl>("PageHost")!.IsVisualAncestorOf(inPage));
        Do(pad, Press(PadMove.Left));
        Assert.Contains(pad.On!, tabs);
    });

    [Fact]
    public Task TextAndTheNameAreNeverStoppedOnAndTheMouseHidesThePad() => OnAWindow((window, main, pad) =>
    {
        foreach (var show in new Action[]
                 {
                     () => main.ShowSettings(SettingsTab.Online), () => main.ShowSettings(SettingsTab.Launcher),
                     () => main.Current = main.Lan, () => main.Current = main.Garage, () => main.Current = main.Practice,
                 })
        {
            show();
            Dispatcher.UIThread.RunJobs();
            var targets = PadNavigator.TargetsIn(window);
            Assert.NotEmpty(targets);
            Assert.DoesNotContain(targets, t => t is TextBox || t.FindAncestorOfType<TextBox>() is not null);
            Assert.DoesNotContain(targets, t => t.Name == "NameButton");
            Assert.DoesNotContain(targets, t => t.Classes.Contains("chrome"));   // the window's own buttons
        }
        // somewhere to type is on these pages, and the pad passes it by
        Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);

        Do(pad, Press(PadMove.Down));
        Assert.True(pad.Showing);
        window.MouseMove(new Point(400, 400));
        window.MouseMove(new Point(420, 410));
        Dispatcher.UIThread.RunJobs();
        Assert.False(pad.Showing);
    });

    [Fact]
    public Task LeftAndRightChangeAValueAndAHeldOneNeverLeavesIt() => OnAWindow((window, main, pad) =>
    {
        main.ShowSettings(SettingsTab.Game);
        Dispatcher.UIThread.RunJobs();
        var volume = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "GameVolume");
        volume.Value = 90;
        volume.Focus();
        Do(pad, Press(PadMove.Right));                               // shows the pad on it
        Assert.Same(volume, pad.On);
        Do(pad, Press(PadMove.Right));
        Assert.Equal(95, volume.Value);
        Do(pad, new PadMoved(PadMove.Right, Repeat: true));
        Assert.Equal(100, volume.Value);
        Do(pad, new PadMoved(PadMove.Right, Repeat: true));
        Assert.Same(volume, pad.On);                                 // held at its end: it stays
        Do(pad, Press(PadMove.Left));
        Assert.Equal(95, volume.Value);
        Assert.Equal(95, main.Settings.GameVolume);                  // the setting itself, as a drag sets it

        // a row of choices: A steps it on, round to the first
        var sync = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "VerticalSync");
        var before = sync.SelectedIndex;
        sync.ContainerFromIndex(Math.Max(0, before))!.Focus();       // (the pad shows already: A acts at once)
        Do(pad, Press(PadButton.A));
        Assert.Same(sync, pad.On);
        Assert.Equal((before + 1) % sync.ItemCount, sync.SelectedIndex);
        Do(pad, Press(PadButton.A));
        Assert.Equal(before, sync.SelectedIndex);                    // two choices: round to the first
    });

    /// <summary>Practice's rules panel (a custom lobby's for one player): the D-pad reaches every rule's control,
    /// down the panel; A turns the mines' return off and on; right goes on to its delay, and left and right step it
    /// by 50 ms.</summary>
    [Fact]
    public Task EveryPracticeRuleIsWorkedWithThePad() => OnAWindow((window, main, pad) =>
    {
        main.Current = main.Practice;
        Dispatcher.UIThread.RunJobs();
        var panel = window.GetVisualDescendants().OfType<RulesPanel>().Single();
        var targets = PadNavigator.TargetsIn(window).Where(panel.IsVisualAncestorOf).ToList();
        var rows = panel.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("rule")).ToList();
        // every row with something to set has a control the pad stops on (Rewind and Save · Load are values)
        foreach (var row in rows.Where(r => r.Children.OfType<TextBlock>().Single().Text is not ("Rewind" or "Save · Load")))
        {
            Assert.Contains(targets, row.IsVisualAncestorOf);
        }
        // down the panel from its first control: each row in turn, to the action
        targets[0].Focus();
        Do(pad, Press(PadMove.Down));                                 // shows the pad on it
        var visited = new List<Control> { pad.On! };
        for (var i = 0; i < 12 && !Equals((pad.On as Button)?.Content, "START PRACTICE"); i++)
        {
            Do(pad, Press(PadMove.Down));
            visited.Add(pad.On!);
        }
        foreach (var row in rows.Where(r => r.Children.OfType<TextBlock>().Single().Text is not ("Rewind" or "Save · Load")))
        {
            Assert.Contains(visited, row.IsVisualAncestorOf);
        }

        var mines = rows.Single(r => r.Children.OfType<TextBlock>().Single().Text == "Mines return");
        var off = mines.GetVisualDescendants().OfType<ToggleSwitch>().Single();
        off.Focus();
        Do(pad, Press(PadButton.A));
        Assert.Equal(0, main.Practice.Setup.MineRegenMs);
        Do(pad, Press(PadButton.A));
        Assert.Equal(200, main.Practice.Setup.MineRegenMs);
        // right on the switch: on already, so the delay beside it; then a step of 50 ms
        var delay = mines.GetVisualDescendants().OfType<Stepper>().Single();
        Do(pad, Press(PadMove.Right));
        Assert.Same(delay, pad.On);
        Do(pad, Press(PadMove.Right));
        Assert.Equal(250, main.Practice.Setup.MineRegenMs);
        Do(pad, new PadMoved(PadMove.Left, Repeat: true));
        Do(pad, new PadMoved(PadMove.Left, Repeat: true));
        Do(pad, new PadMoved(PadMove.Left, Repeat: true));
        Do(pad, new PadMoved(PadMove.Left, Repeat: true));
        Assert.Equal(50, main.Practice.Setup.MineRegenMs);           // its least: a held left stays on it
        Assert.Same(delay, pad.On);
    });

    [Fact]
    public Task StartPressesTheMainActionAndBClosesTheQuestion() => OnAWindow((window, main, pad) =>
    {
        // Home: the main action is the left column's RACE, which with no server set opens Settings
        Do(pad, Press(PadMove.Down));
        Do(pad, Press(PadButton.Start));
        Assert.Equal("settings", main.Tab);

        // the question before leaving a lobby holds the pad until it is answered; B stays
        main.Current = main.Home;
        main.Control.Asking = true;
        Dispatcher.UIThread.RunJobs();
        Do(pad, Press(PadMove.Down));
        var question = window.GetVisualDescendants().OfType<Panel>().Single(p => PadNav.GetScope(p) && p.IsEffectivelyVisible);
        Assert.True(question.IsVisualAncestorOf(pad.On!));
        Do(pad, Press(PadMove.Up));
        Do(pad, Press(PadMove.Left));
        Assert.True(question.IsVisualAncestorOf(pad.On!));
        Do(pad, Press(PadButton.R));
        Assert.Same(main.Home, main.Current);                        // no tab changes under the question
        Do(pad, Press(PadButton.B));
        Assert.False(main.Control.Asking);
    });
}
