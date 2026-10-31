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
/// The Settings tab's four pages, Game, Online, Controller and Launcher, pressed by their headings with the headless
/// window's mouse: each shows its own rows and no other's; the page last chosen is the one the tab opens on, in this
/// run and the next (the settings file's <c>"settingsTab"</c>, read defensively); and every way that sends a player
/// to Settings to set something lands on the page that holds it: RACE without a disc and Home's "Choose your disc…"
/// on Launcher, RACE without a server on Online.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class SettingsTabsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private AppState State(string settings)
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), settings);
        return AppState.Load(Data);
    }

    /// <summary>A game that is "installed" (a script stands for the runner): RACE then says what it lacks of the
    /// settings, not that the game's files are to be prepared.</summary>
    private void Installed(AppState state)
    {
        var runner = Path.Combine(_dir, "runner.sh");
        File.WriteAllText(runner, "#!/bin/sh\nexit 0\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var module = Path.Combine(_dir, "m.dll");
        File.WriteAllText(module, "");
        Directory.CreateDirectory(Path.Combine(_dir, "game", "sys"));
        File.WriteAllText(Path.Combine(_dir, "game", "sys", "main.dol"), "");
        state.Update(s => s with { Recomp = new RecompSettings(Runner: runner, Module: module, GameFolder: Path.Combine(_dir, "game")) });
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
                var window = new MainWindow { DataContext = main, Width = 1116, Height = 789 };
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

    /// <summary>A tab of the window's top row by its word.</summary>
    /// <summary>A tab above the page; Settings: its button at the left column's foot.</summary>
    private static Button TopTab(MainWindow window, string word) => word == "Settings"
        ? window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsButton")
        : window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab") && Equals(b.Content, word));

    /// <summary>The headings above the Settings tab's page.</summary>
    private static List<Button> Headings(MainWindow window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("section") && b.IsEffectivelyVisible)];

    private static Button Heading(MainWindow window, string label) =>
        Headings(window).Single(b => b.DataContext is SectionLink link && link.Label == label);

    /// <summary>The labels of the rows that show on the Settings page.</summary>
    private static string[] Rows(MainWindow window) =>
        [.. window.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("label") && t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    [Fact]
    public Task TheSettingsTabHasFourPagesEachWithItsOwnRows() => OnAWindow(State("""{"noServer": true}"""), (window, main) =>
    {
        Click(window, TopTab(window, "Settings"));
        Assert.Equal(["Game", "Online", "Controller", "Launcher"], Headings(window).Select(b => ((SectionLink)b.DataContext!).Label));
        // a first start: Game
        Assert.Same(main.Settings, main.Current);
        Assert.Equal(["Widescreen", "Full screen mode", "Vertical sync", "Refresh rate (experimental)", "Frame rate", "Volume", "Other racers' sounds", "Rumble", "Show inputs"], Rows(window));
        Assert.Contains("on", Heading(window, "Game").Classes);

        Click(window, Heading(window, "Online"));
        Assert.Equal(["Input delay", "Server", "Account", "Notices", "Activity", "Chat"], Rows(window));
        Assert.Equal(["Online"], Headings(window).Where(b => b.Classes.Contains("on")).Select(b => ((SectionLink)b.DataContext!).Label));

        Click(window, Heading(window, "Controller"));
        Assert.Same(main.Controller, main.Current);
        Assert.Single(window.GetVisualDescendants().OfType<ControllerView>());
        Assert.Equal(["Controller"], Headings(window).Where(b => b.Classes.Contains("on")).Select(b => ((SectionLink)b.DataContext!).Label));

        Click(window, Heading(window, "Launcher"));
        Assert.Same(main.Settings, main.Current);
        // (Updates shown since 0.5.0: NotYet; Older copy as this launcher is no portable copy: DataMoveViewModel.Offered)
        Assert.Equal(["Disc image", "Sounds", "Menus", "Interface scale", "Updates", "Recording", "Folder", "Older copy"], Rows(window));
        Assert.Equal(["Launcher"], Headings(window).Where(b => b.Classes.Contains("on")).Select(b => ((SectionLink)b.DataContext!).Label));

        Click(window, Heading(window, "Game"));
        Assert.Equal("Widescreen", Rows(window)[0]);
    });

    [Fact]
    public async Task TheTabOpensOnThePageLastChosenInThisRunAndTheNext()
    {
        await OnAWindow(State("""{"noServer": true}"""), (window, main) =>
        {
            Click(window, TopTab(window, "Settings"));
            Click(window, Heading(window, "Launcher"));
            Click(window, TopTab(window, "Garage"));
            Assert.Same(main.Garage, main.Current);
            Click(window, TopTab(window, "Settings"));
            Assert.Equal((SettingsTab.Launcher, "Disc image"), (main.ShownSettingsTab, Rows(window)[0]));
        });
        var again = AppState.Load(Data);
        Assert.Equal(SettingsTab.Launcher, again.Settings.SettingsTab);
        await OnAWindow(again, (window, main) =>
        {
            Click(window, TopTab(window, "Settings"));
            Assert.Equal((SettingsTab.Launcher, "Disc image"), (main.ShownSettingsTab, Rows(window)[0]));
            Click(window, Heading(window, "Controller"));
        });
        again = AppState.Load(Data);
        Assert.Equal(SettingsTab.Controller, again.Settings.SettingsTab);
        await OnAWindow(again, (window, main) =>
        {
            Click(window, TopTab(window, "Settings"));
            Assert.Same(main.Controller, main.Current);
            Click(window, Heading(window, "Online"));
        });
        Assert.Equal(SettingsTab.Online, AppState.Load(Data).Settings.SettingsTab);
        Assert.Contains("\"settingsTab\": \"online\"", File.ReadAllText(Path.Combine(Data, "settings.json")), StringComparison.Ordinal);
    }

    /// <summary>A page the file names that is none of the four (a hand's edit, a newer launcher's): Game, and the
    /// rest of the file stays.</summary>
    [Theory]
    [InlineData("\"sound\"")]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("{\"page\": 1}")]
    [InlineData("[\"online\"]")]
    [InlineData("null")]
    [InlineData("true")]
    public async Task APageThatIsNoneOfTheFourOpensGame(string written)
    {
        var state = State("{\"server\": \"ws://coordinator.test:47810/ws\", \"inputDelay\": 1, \"settingsTab\": " + written + "}");
        Assert.Equal((SettingsTab.Game, "ws://coordinator.test:47810/ws", 1), (state.Settings.SettingsTab, state.Settings.Server, state.Settings.InputDelay));
        await OnAWindow(state, (window, main) =>
        {
            Click(window, TopTab(window, "Settings"));
            Assert.Equal((SettingsTab.Game, "Widescreen"), (main.ShownSettingsTab, Rows(window)[0]));
        });
    }

    [Fact]
    public async Task EveryWayToSettingsLandsOnThePageThatHoldsTheFix()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the game is a shell script here");
        var image = new SyntheticDisc().Write(_dir, "gx.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(image));
        var state = State("""{"noServer": true, "settingsTab": "controller"}""");
        Installed(state);
        await OnAWindow(state, (window, main) =>
        {
            // RACE is on Quick Play's page (direction D), drawn anew each time the page shows
            Button Race()
            {
                Dispatcher.UIThread.RunJobs();
                return window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("go") && b.IsEffectivelyVisible);
            }

            // no disc: RACE reads CHOOSE YOUR DISC and opens Launcher, where the disc image is
            Assert.Equal("CHOOSE YOUR DISC", main.Control.GoText);
            Click(window, Race());
            Assert.Equal(SettingsTab.Launcher, main.ShownSettingsTab);
            Assert.True(Named<TextBox>(window, "DiscImage").IsEffectivelyVisible);

            // Home's "Choose your disc…" (shown on a race's map without a disc) too
            Click(window, Heading(window, "Game"));
            main.Current = main.Home;
            main.Home.ChooseDiscCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsTab.Launcher, main.ShownSettingsTab);

            // a disc and no server: no big button, "No server" and a link that opens Online,
            // where the server is
            Click(window, Heading(window, "Controller"));
            main.Current = main.Home;
            state.Update(s => s with { DiscPath = image });
            Pump(() => main.Control.NoServer, "the page did not say there is no server");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("go") && b.IsEffectivelyVisible);
            Click(window, Named<Button>(window, "ServerSettings"));
            Assert.Equal(SettingsTab.Online, main.ShownSettingsTab);
            Assert.True(Named<TextBox>(window, "ServerBox").IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Use the official server") && b.IsEffectivelyVisible);
            Assert.Equal(SettingsTab.Online, state.Settings.SettingsTab);
        });
    }
}
