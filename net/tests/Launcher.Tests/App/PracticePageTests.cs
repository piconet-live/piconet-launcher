using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// Play's Practice page pressed with the headless
/// window's mouse: the heading under Play, the course picker's chips and cards, the rules panel's rows, the Garage
/// button, and START PRACTICE off while the game has no practice start. Then the two reusable controls on their own:
/// a rules panel with every kind of row, and a course picker as a pool.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class PracticePageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"noServer": true, "soundMuted": true}""");
        return AppState.Load(data);
    }

    private static async Task OnAWindow(AppState state, Action<MainWindow, MainViewModel> steps)
    {
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

    private static async Task OnItsOwn(Func<Control> make, Action<Window, Control> steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                var control = make();
                var window = new Window { Content = control, Width = 900, Height = 760 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    steps(window, control);
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => AutomationProperties.GetName(c) == name && c.IsEffectivelyVisible);

    private static Button Heading(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("section") && b.IsEffectivelyVisible
                                                                    && b.DataContext is SectionLink link && link.Label == label);

    /// <summary>The row of a rules panel by its label: its control's part.</summary>
    private static ContentControl RowOf(Visual root, string label) =>
        root.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("rule")
                                                              && g.Children.OfType<TextBlock>().Any(t => t.Text == label))
            .Children.OfType<ContentControl>().Single();

    /// <summary>The hero's title plate keeps to its share of the hero in a 1280 by 800 window at 100% and 130%
    /// (scaled up, the plate covered half the hero and hid the model): small (the name at 40)
    /// in a narrow hero, and the model framed right of it (<see cref="CourseHero.Covered"/>).</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(130)]
    public Task TheHerosPlateKeepsToItsShareAndTheModelRightOfIt(int scale)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"noServer\": true, \"soundMuted\": true, \"interfaceScale\": " + scale + "}");
        var state = AppState.Load(data);
        return OnAWindow(state, (window, main) =>
        {
            window.MinWidth = window.MinHeight = 0;
            window.Width = 1280;
            window.Height = 800;
            Dispatcher.UIThread.RunJobs();
            Click(window, Heading(window, "Practice"));
            Dispatcher.UIThread.RunJobs();
            var hero = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Hero");
            var plate = hero.GetVisualDescendants().OfType<Plate>().Single(p => p.Name == "TitlePlate");
            var name = hero.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TrackName");
            var narrow = hero.Bounds.Width < CourseShot.NarrowHero;
            Assert.Equal(narrow, hero.Classes.Contains("narrow"));
            Assert.Equal(narrow ? "d40" : "d64", name.Classes.Single(c => c is "d40" or "d64"));
            Assert.True(plate.Bounds.Width <= hero.Bounds.Width * (CourseShot.PlateShare + 0.03),
                $"at {scale}%: the plate is {plate.Bounds.Width:F0} of the hero's {hero.Bounds.Width:F0}");
            Assert.Equal(plate.Bounds.Right, hero.GetVisualDescendants().OfType<CourseHero>().Single().Covered);
        });
    }

    /// <summary>At the launcher's least window, 1280 by 720, and at 1280 by 800, the whole rules panel shows with
    /// START PRACTICE under it, the page's sections headed. With INPUTS_SHOTS naming a folder, the page there.</summary>
    [Theory]
    [InlineData(720)]
    [InlineData(800)]
    public Task TheRulesPanelShowsWholeInTheWindow(int height) => OnAWindow(State(), (window, main) =>
    {
        window.Width = 1280;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        Click(window, Heading(window, "Practice"));
        Dispatcher.UIThread.RunJobs();
        var start = Named<Button>(window, "START PRACTICE");
        var bottom = start.TranslatePoint(new Point(0, start.Bounds.Height), window)!.Value.Y;
        Shot(window, $"practice-1280x{height}");
        Assert.True(bottom <= window.Bounds.Height, $"START PRACTICE ends at {bottom:F0} in a window {window.Bounds.Height:F0} high");
    });

    /// <summary>With INPUTS_SHOTS naming a folder, the window there as a PNG.</summary>
    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("INPUTS_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(300);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [Fact]
    public Task ThePracticePageSetsUpARunAndWaitsForTheGame()
    {
        var state = State();
        return OnAWindow(state, (window, main) => SetsUpARun(state, window, main));
    }

    private static void SetsUpARun(AppState state, MainWindow window, MainViewModel main)
    {
        Click(window, Heading(window, "Practice"));
        Assert.Same(main.Practice, main.Current);
        Assert.Single(window.GetVisualDescendants().OfType<PracticeView>());
        Assert.Equal("play", main.Tab);
        // the set-up as it opens: the first course, the Garage's machine, stock stats, 100, the mode's defaults (unlimited
        // laps)
        var mode = state.PracticeModeFile!;
        Assert.Equal(PracticeSetup.Default with
        {
            Machine = main.Garage.MachineIndex,
            Params = new(mode.Params.Select(p => KeyValuePair.Create(p.Name, p.Default))),
        }, main.Practice.Setup);
        var labels = window.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("rule"))
            .Select(g => g.Children.OfType<TextBlock>().Single().Text).ToList();
        // the machine; under Practice the mode's parameters in the file's order, then its rewind and save; under Race
        // rules the host's other rules
        string[] modeRows = [.. mode.Params.Select(p => p.Label)];
        Assert.Equal(["Machine", "Stats", "Machine setting", .. modeRows, "Rewind", "Save · Load", "Boost from the start", "Mines respawn"], labels);
        Assert.Equal(["Laps", "Restore"], modeRows.Order());
        Assert.Equal(["PRACTICE", "RACE RULES"], window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("grouphead") && t.IsEffectivelyVisible).Select(t => t.Text));
        // the host's rules that mean something alone, from the one list a custom lobby shows too, but those the mode
        // states (the laps) and its own Restore; none of the others
        Assert.Equal(HostRules.Alone.Where(r => r.Reach == RuleReach.Alone && !PracticeMode.States(mode, r)).Select(r => r.Label).Order(),
            labels.Except(["Machine", "Rewind", "Save · Load", .. modeRows]).Order());
        Assert.DoesNotContain(labels, l => HostRules.All.Any(r => r.Reach != RuleReach.Alone && r.Label == l));
        Assert.Contains(RowOf(window, "Rewind").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "5'00\"");
        Assert.Contains(RowOf(window, "Save · Load").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "D-pad right · left");

        // a cup's chip shows its five cards; a card pressed is the course, and the only one framed
        Assert.Equal(30, window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("card")));
        Click(window, Named<Button>(window, "Sapphire"));
        var cards = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card")).ToList();
        Assert.Equal(["Big Blue: Drift Highway", "Port Town: Long Pipe", "Green Plant: Mobius Ring", "Port Town: Aero Dive",
                      "Mute City: Serial Gaps"], cards.Select(AutomationProperties.GetName));
        Click(window, cards[1]);
        Assert.Equal(13, main.Practice.Setup.Course);
        Assert.Equal([13], main.Practice.Courses.Cards.Where(c => c.Picked).Select(c => c.Course));
        Assert.Contains("picked", cards[1].Classes);
        Assert.DoesNotContain("picked", cards[0].Classes);

        // laps: Unlimited as it opens, the game's free run; off, a count: one more; typed, and never past the lap
        // counter's two figures
        var laps = RowOf(window, "Laps");
        var unlimited = laps.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.IsEffectivelyVisible);
        int Laps() => main.Practice.Setup.Params!["lap_count"];
        Assert.True(unlimited.IsChecked);
        Assert.Equal(0, Laps());
        unlimited.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Laps());
        var more = Named<Button>(laps, "More");
        Click(window, more);
        Assert.Equal(2, Laps());
        var typed = Named<TextBox>(laps, "Typed");
        typed.Focus();
        typed.Text = "250";
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Assert.Equal(99, Laps());
        Assert.Equal("99", typed.Text);
        typed.Text = "x";
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Assert.Equal("99", typed.Text);
        typed.Text = "12";
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Assert.Equal(12, Laps());

        // Restore: off as it opens, the mode's rule when on
        Assert.Equal(0, main.Practice.Setup.Params!["restore_on"]);
        Click(window, RowOf(window, "Restore").GetVisualDescendants().OfType<ToggleSwitch>().Single());
        Assert.Equal(1, main.Practice.Setup.Params!["restore_on"]);
        Assert.False(main.Practice.Setup.BoostFromStart);
        Click(window, RowOf(window, "Boost from the start").GetVisualDescendants().OfType<ToggleSwitch>().Single());
        Assert.True(main.Practice.Setup.BoostFromStart);

        // the mines come back after Quick Play's 200 ms; Off: they stay gone, and the delay waits, kept (a lobby's row:
        // its switch, then the delay)
        var mines = RowOf(window, "Mines respawn");
        Assert.Equal(200, main.Practice.Setup.MineRegenMs);
        Assert.Contains(mines.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ms" && t.IsEffectivelyVisible);
        Click(window, Named<ToggleSwitch>(mines, "Mines respawn"));
        Assert.Equal(0, main.Practice.Setup.MineRegenMs);
        Assert.DoesNotContain(mines.GetVisualDescendants().OfType<Stepper>(), t => t.IsEffectivelyVisible);
        Assert.Contains(mines.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Off" && t.IsEffectivelyVisible);
        Click(window, Named<ToggleSwitch>(mines, "Mines respawn"));
        Assert.Equal(200, main.Practice.Setup.MineRegenMs);
        Click(window, Named<Button>(mines, "More"));
        Assert.Equal(250, main.Practice.Setup.MineRegenMs);
        var delay = Named<TextBox>(mines, "Typed");
        delay.Focus();
        delay.Text = "1234";
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Assert.Equal(1250, main.Practice.Setup.MineRegenMs);   // the nearest step of 50 ms
        Assert.Equal("1250", delay.Text);

        // START PRACTICE is on: with no game prepared here, it says why it started none, and the page stays
        var start = Named<Button>(window, "START PRACTICE");
        Assert.True(start.IsEffectivelyEnabled);
        Click(window, start);
        Assert.Same(main.Practice, main.Current);
        Assert.False(string.IsNullOrEmpty(main.Practice.Notice));
        // the practice started is remembered, and a page made again opens on its course and rules
        var started = main.Practice.Setup;
        Assert.Equal(started, state.Settings.Practice);
        using (var again = new MainViewModel(state))
        {
            Assert.Equal(started, again.Practice.Setup);
            Assert.Equal(1250, HostRuleRows.Number(again.Practice.Rule("mine-regen")));
        }

        // the Garage button: the Garage, and back on Practice the machine picked there
        Click(window, Named<Button>(RowOf(window, "Machine"), "Garage"));
        Assert.Same(main.Garage, main.Current);
        main.Garage.MachineIndex = 7;
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab") && Equals(b.Content, "Play")));
        Assert.Same(main.Practice, main.Current);   // the Play tab opens on the page last shown there
        Assert.Equal(7, main.Practice.Setup.Machine);
        Assert.Contains(RowOf(window, "Machine").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == ReplayWords.Machine(7));
    }

    private static IReadOnlyList<RuleGroup> EveryKind(List<string> garage) => RuleGroup.Of(
        [new ValueRule("Rewind", "5'00\""), new MachineRule("Machine", "Blue Falcon", () => garage.Add("garage")),
         new ChoiceRule("Stats", ["Stock", "Pack"]), new SegmentedRule("Courses", ["Vote", "Host picks"])],
        [new StepperRule("Players", 30, 2, 30), new SliderRule("Machine setting", 100, 0, 100), new SwitchRule("Restore", false)],
        [new TextRule("Name", "Lobby name"), new RangeRule("Setting range", 80, 100, 0, 100)]);

    [Fact]
    public async Task ARulesPanelShowsEveryKindOfRowAndKeepsItsValues()
    {
        var garage = new List<string>();
        var groups = EveryKind(garage);
        await OnItsOwn(() => new RulesPanel { Groups = groups, Action = "CREATE LOBBY" }, (window, panel) =>
        {
            Assert.Equal([false, true, true], groups.Select(g => g.Later));
            Assert.Equal(2, panel.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("group") && b.Classes.Contains("later")));
            Assert.Single(RowOf(panel, "Machine").GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Garage"));
            Assert.Single(RowOf(panel, "Stats").GetVisualDescendants().OfType<ComboBox>());
            Assert.Single(RowOf(panel, "Courses").GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("seg"));
            Assert.Single(RowOf(panel, "Players").GetVisualDescendants().OfType<Stepper>());
            Assert.Single(RowOf(panel, "Machine setting").GetVisualDescendants().OfType<Slider>());
            Assert.Single(RowOf(panel, "Restore").GetVisualDescendants().OfType<ToggleSwitch>());
            Assert.Single(RowOf(panel, "Name").GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(2, RowOf(panel, "Setting range").GetVisualDescendants().OfType<Stepper>().Count());
            Assert.Contains(panel.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "CREATE LOBBY"));

            Click(window, Named<Button>(RowOf(panel, "Machine"), "Garage"));
            Assert.Equal(["garage"], garage);
            var players = (StepperRule)groups[1].Rows[0];
            Click(window, Named<Button>(RowOf(panel, "Players"), "More"));
            Assert.Equal(30, players.Value);   // at its highest already
            Click(window, Named<Button>(RowOf(panel, "Players"), "Less"));
            Assert.Equal(29, players.Value);
            var restore = (SwitchRule)groups[1].Rows[2];
            Click(window, RowOf(panel, "Restore").GetVisualDescendants().OfType<ToggleSwitch>().Single());
            Assert.True(restore.On);
            Assert.Equal("On", restore.State);
            var segmented = (SegmentedRule)groups[0].Rows[3];
            Click(window, RowOf(panel, "Courses").GetVisualDescendants().OfType<ListBoxItem>().Last());
            Assert.Equal(1, segmented.Selected);
            // the range: the lowest raised past the highest takes the highest with it
            var range = (RangeRule)groups[2].Rows[1];
            var lowMore = RowOf(panel, "Setting range").GetVisualDescendants().OfType<Stepper>().First()
                .GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "More");
            for (var i = 0; i < 25; i++)
            {
                Click(window, lowMore);
            }
            Assert.Equal((100, 100), (range.Low, range.High));
        });
    }

    [Fact]
    public void RowsKeepTheirValuesInsideTheirRanges()
    {
        var stepper = new StepperRule("Laps", 12, 1, 9);
        Assert.Equal(9, stepper.Value);
        stepper.Value = 0;
        Assert.Equal(1, stepper.Value);
        var slider = new SliderRule("Machine setting", 140, 0, 100);
        Assert.Equal(100, slider.Whole);
        slider.Value = 49.6;
        Assert.Equal(50, slider.Whole);
        var choice = new ChoiceRule("Stats", ["Stock"], 5);
        Assert.Equal("Stock", choice.Chosen);
        choice.Selected = -1;
        Assert.Equal(0, choice.Selected);
        var range = new RangeRule("Range", 90, 70, 0, 100);
        Assert.Equal((70, 90), (range.Low, range.High));
        range.High = 60;
        Assert.Equal((60, 60), (range.Low, range.High));
        range.Low = 200;
        Assert.Equal((100, 100), (range.Low, range.High));
        Assert.Equal(new PracticeSetup(1, 40, null, 0), new PracticeSetup(999, 77, null, -5).Kept());
        var laps = new LapsRule("Laps", 0, 99);
        Assert.Equal(1, laps.Laps);
        laps.Value = 120;
        Assert.Equal(99, laps.Laps);
        laps.Unlimited = true;
        Assert.Equal(0, laps.Laps);
        Assert.False(laps.Counted);
        Assert.Equal("1'00\"", PracticeViewModel.Clock(60));
    }

    [Fact]
    public async Task ACoursePickerAsAPool()
    {
        var model = new CoursePickerModel(PickMode.Pool, Courses.Pool);
        await OnItsOwn(() => new CoursePicker { DataContext = model }, (window, picker) =>
        {
            Assert.Equal($"{Courses.Pool.Count} / 30", model.PoolCount);
            Click(window, Named<Button>(picker, "No course"));
            Assert.Empty(model.Pool);
            Assert.Equal("0 / 30", model.PoolCount);
            Click(window, Named<Button>(picker, "Story"));
            Assert.Equal(4, model.Shown.Count);
            var cards = picker.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card")).ToList();
            Click(window, cards[0]);
            Click(window, cards[2]);
            Assert.Equal([39, 44], model.Pool);
            // a check on each card of the pool, and no frame: the frame is a single pick's
            Assert.Equal([true, false, true, false], model.Shown.Select(c => c.Checked));
            Click(window, cards[0]);
            Assert.Equal([44], model.Pool);
            Click(window, Named<Button>(picker, "Every course"));
            Assert.Equal(30, model.Pool.Count);
            Click(window, Named<Button>(picker, "All"));
            Assert.Equal(30, model.Shown.Count);
        });
        // a single pick never holds two: of the courses asked for, the first in the game's order (Loop Cross before
        // Multiplex), and the first course when none is one
        var one = new CoursePickerModel(PickMode.One, [5, 8]);
        Assert.Equal(8, one.Picked);
        Assert.Single(one.Cards, c => c.Picked);
        Assert.Equal(1, new CoursePickerModel(PickMode.One, [999]).Picked);
    }
}
