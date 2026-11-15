using Avalonia;
using Avalonia.Automation;
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
using Launcher.App.Views.NextUi;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The launcher's redesigned interface behind its box (Settings, Launcher, "Try something new"), pressed with the
/// headless window's mouse: off by default and absent from the file; ticking "Experimental UI" asks first and changes
/// nothing until "Turn it on" ("Not now" and Escape untick it); on, the window shows the new shell in the classic
/// interface's place, at once and at the next start; unticking the box there is at once. "Developer mode"
/// is a setting of its own. Each view model's view comes from the locator, the classic one where the new interface
/// has none. With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs (1280 x 720, and at 125%).
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class ExperimentalUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private string SettingsFile => Path.Combine(Data, "settings.json");

    private AppState State(string settings)
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(SettingsFile, settings);
        return AppState.Load(Data);
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
                var window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
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

    /// <summary>With NEXT_SHOTS naming a folder, the window there as a PNG.</summary>
    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("NEXT_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(300);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>The interface the window shows, under the interface scale.</summary>
    private static Control? Shell(MainWindow window) => Named<ScaledRoot>(window, "ScaleRoot").Child;

    /// <summary>The Settings tab's Launcher page, scrolled to its foot, where "Try something new" is.</summary>
    private static SettingsView LauncherPage(MainWindow window, MainViewModel main)
    {
        main.ShowSettings(SettingsTab.Launcher);
        Dispatcher.UIThread.RunJobs();
        var scroll = Named<ScrollViewer>(window, "PageScroll");
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<SettingsView>().Single();
    }

    private static Button Answer(MainWindow window, string word) =>
        Named<Panel>(window, "ExperimentalQuestion").GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, word));

    [Fact]
    public void BothAreOffByDefaultAndLeftOutOfTheFileWhileOff()
    {
        var state = State("""{"noServer": true, "inputDelay": 1}""");
        try
        {
            Assert.False(state.Settings.ExperimentalUi);
            Assert.False(state.Settings.DeveloperMode);
            state.Update(s => s with { InputDelay = 0 });
        }
        finally
        {
            state.Shutdown();
        }
        var written = File.ReadAllText(SettingsFile);
        Assert.DoesNotContain("experimentalUi", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developerMode", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A value that is neither true nor false (a hand's edit) reads as off, and the rest of the file stays.</summary>
    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[true]")]
    [InlineData("{\"on\": true}")]
    public void AValueThatIsNoYesOrNoReadsAsOff(string written)
    {
        var state = State("{\"server\": \"ws://coordinator.test:47810/ws\", \"inputDelay\": 1, \"experimentalUi\": " + written
            + ", \"developerMode\": " + written + "}");
        try
        {
            Assert.Equal((false, false, "ws://coordinator.test:47810/ws", 1),
                (state.Settings.ExperimentalUi, state.Settings.DeveloperMode, state.Settings.Server, state.Settings.InputDelay));
        }
        finally
        {
            state.Shutdown();
        }
    }

    [Fact]
    public async Task TickingTheBoxAsksFirstAndOnlyTurnItOnShowsTheNewInterface()
    {
        var state = State("""{"noServer": true}""");
        await OnAWindow(state, (window, main) =>
        {
            Assert.Same(Named<Grid>(window, "ClassicShell"), Shell(window));
            var view = LauncherPage(window, main);
            var box = Named<CheckBox>(view, "ExperimentalUi");
            var question = Named<Panel>(window, "ExperimentalQuestion");
            Assert.False(box.IsChecked);
            Assert.False(question.IsEffectivelyVisible);
            Shot(window, "classic-try-something-new");

            // ticked: the question, and nothing set
            Click(window, box);
            Pump(() => question.IsEffectivelyVisible, "the question did not show");
            Assert.False(state.Settings.ExperimentalUi);
            Assert.IsNotType<NextShell>(Shell(window));
            Assert.Contains(question.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == SettingsViewModel.AskExperimentalTitle);
            Shot(window, "classic-question");

            // "Not now": the box unticked, the classic interface as it was
            Click(window, Answer(window, "Not now"));
            Pump(() => !question.IsEffectivelyVisible, "the question stayed");
            Assert.False(box.IsChecked);
            Assert.False(state.Settings.ExperimentalUi);

            // Escape is "Not now"
            Click(window, box);
            Pump(() => question.IsEffectivelyVisible, "the question did not show again");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Pump(() => !question.IsEffectivelyVisible, "Escape did not close the question");
            Assert.False(box.IsChecked);
            Assert.False(state.Settings.ExperimentalUi);

            // "Turn it on": the new shell in the classic one's place, the classic pages out of the window
            Click(window, box);
            Pump(() => question.IsEffectivelyVisible, "the question did not show a third time");
            Click(window, Answer(window, "Turn it on"));
            Pump(() => Shell(window) is NextShell, "the new interface did not show");
            Assert.True(state.Settings.ExperimentalUi);
            Assert.True(main.ExperimentalUi);
            Assert.False(main.Settings.AskingExperimental);
            Assert.False(window.IsVisualAncestorOf(question));
            // the new interface is made for the controller: its navigation comes on with it
            Assert.True(state.Settings.PadNavigation);
            Assert.True(main.Settings.PadNavigation);
            Shot(window, "next-turned-on");

            // the page that was showing shows in the new interface, and unticking its box there brings the classic
            // interface back at once, the page as it was left
            Assert.Same(main.Settings, main.Current);
            var shell = Assert.IsType<NextShell>(Shell(window));
            var page = shell.GetVisualDescendants().OfType<SettingsPageView>().Single();
            main.Shell.SettingsPage.ShowCategory("Launcher");
            Dispatcher.UIThread.RunJobs();
            var there = page.GetVisualDescendants().OfType<Launcher.App.Controls.NextUi.OptionButton>()
                .Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Experimental UI");
            Assert.Equal("On", main.Shell.SettingsPage.More.SelectMany(g => g.Items).Single(i => i.Label == "Experimental UI").ValueText);
            there.BringIntoView();
            Click(window, there);
            Pump(() => ReferenceEquals(Shell(window), Named<Grid>(window, "ClassicShell")), "the classic interface did not come back");
            Assert.False(state.Settings.ExperimentalUi);
            Assert.Same(main.Settings, main.Current);
            Assert.False(Named<CheckBox>(window.GetVisualDescendants().OfType<SettingsView>().Single(), "ExperimentalUi").IsChecked);
            Assert.Empty(window.GetVisualDescendants().OfType<NextShell>());
        });
        Assert.DoesNotContain("experimentalUi", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheNewInterfaceIsKeptForTheNextStartAndSwitchedBackAtOnce()
    {
        var state = State("""{"noServer": true}""");
        await OnAWindow(state, (window, main) =>
        {
            Click(window, Named<CheckBox>(LauncherPage(window, main), "ExperimentalUi"));
            Pump(() => Named<Panel>(window, "ExperimentalQuestion").IsEffectivelyVisible, "the question did not show");
            Click(window, Answer(window, "Turn it on"));
            Pump(() => Shell(window) is NextShell, "the new interface did not show");
        });
        Assert.Contains("\"experimentalUi\": true", File.ReadAllText(SettingsFile), StringComparison.Ordinal);

        var again = AppState.Load(Data);
        Assert.True(again.Settings.ExperimentalUi);
        await OnAWindow(again, (window, main) =>
        {
            // the next start opens on it, at 100% and at 125%
            Assert.IsType<NextShell>(Shell(window));
            Assert.Empty(window.GetVisualDescendants().OfType<HomeView>());
            again.Update(s => s with { InterfaceScale = 125 });
            Pump(() => Math.Abs(main.Scale - 1.25) < 0.001, "the scale was not taken");
            Assert.IsType<NextShell>(Shell(window));
            Shot(window, "next-start-125");
            again.Update(s => s with { InterfaceScale = 100 });
            Pump(() => Math.Abs(main.Scale - 1) < 0.001, "the scale did not go back");

            again.Update(s => s with { ExperimentalUi = false });
            Pump(() => Shell(window) is not NextShell, "the classic interface did not come back");
            Assert.Single(window.GetVisualDescendants().OfType<HomeView>());
        });
        Assert.False(AppState.Load(Data).Settings.ExperimentalUi);
    }

    [Fact]
    public async Task DeveloperModeIsASettingOfItsOwn()
    {
        var state = State("""{"noServer": true}""");
        await OnAWindow(state, (window, main) =>
        {
            var view = LauncherPage(window, main);
            var box = Named<CheckBox>(view, "DeveloperMode");
            Assert.False(box.IsChecked);
            Click(window, box);
            Pump(() => state.Settings.DeveloperMode, "developer mode was not set");
            Assert.True(main.DeveloperMode);
            // it asks nothing and changes no interface
            Assert.False(Named<Panel>(window, "ExperimentalQuestion").IsEffectivelyVisible);
            Assert.False(state.Settings.ExperimentalUi);
            Assert.IsNotType<NextShell>(Shell(window));
            Shot(window, "classic-developer-mode");
        });
        Assert.Contains("\"developerMode\": true", File.ReadAllText(SettingsFile), StringComparison.Ordinal);

        var again = AppState.Load(Data);
        await OnAWindow(again, (window, main) =>
        {
            var box = Named<CheckBox>(LauncherPage(window, main), "DeveloperMode");
            Assert.True(box.IsChecked);
            Click(window, box);
            Pump(() => !again.Settings.DeveloperMode, "developer mode was not switched off");
        });
        Assert.DoesNotContain("developerMode", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Each page's view from the locator: the classic interface's as before; the new interface's locator gives
    /// its own view of a page it has one for (every page but Home network's, which stays hidden), and
    /// the classic view of every other.</summary>
    [Fact]
    public async Task TheNewInterfaceTakesTheClassicViewOfAPageItHasNoneFor()
    {
        var state = State("""{"noServer": true}""");
        await OnAWindow(state, (window, main) =>
        {
            var classic = new ViewLocator();
            var next = new ViewLocator { Next = true };
            Assert.False(classic.Next);
            foreach (var page in new ViewModelBase[] { main.Lan })
            {
                Assert.True(classic.Match(page));
                Assert.True(next.Match(page));
                var view = Assert.IsAssignableFrom<UserControl>(classic.Build(page));
                Assert.IsType(view.GetType(), next.Build(page));
            }
            Assert.IsType<HomeView>(classic.Build(main.Home));
            Assert.IsType<LobbiesView>(next.Build(main.Home));
            Assert.IsType<LobbyView>(classic.Build(main.Lobby));
            Assert.IsType<QuickPlayView>(next.Build(main.Lobby));
            Assert.IsType<PracticeView>(classic.Build(main.Practice));
            Assert.IsType<PracticeSetupView>(next.Build(main.Practice));
            Assert.IsType<CustomLobbiesView>(classic.Build(main.Lobbies));
            Assert.IsType<CustomListView>(next.Build(main.Lobbies));
            Assert.IsType<GarageView>(classic.Build(main.Garage));
            Assert.IsType<GaragePageView>(next.Build(main.Garage));
            Assert.IsType<ReplaysView>(classic.Build(main.Replays));
            Assert.IsType<ReplaysListView>(next.Build(main.Replays));
            Assert.IsType<StatsView>(classic.Build(main.Stats));
            Assert.IsType<StatsPageView>(next.Build(main.Stats));
            Assert.IsType<ModsView>(classic.Build(main.Mods));
            Assert.IsType<ModsListView>(next.Build(main.Mods));
            Assert.IsType<SettingsView>(classic.Build(main.Settings));
            Assert.IsType<SettingsPageView>(next.Build(main.Settings));
            Assert.IsType<ControllerView>(classic.Build(main.Controller));
            Assert.IsType<ControllerPageView>(next.Build(main.Controller));
            Assert.IsType<DiscView>(classic.Build(main.Disc));
            Assert.IsType<FirstStartView>(next.Build(main.Disc));
        });
    }
}
