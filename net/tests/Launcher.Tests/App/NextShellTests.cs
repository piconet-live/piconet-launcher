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
/// The redesigned interface's shell ("Experimental UI" on) on the stand-in service, pressed with the headless
/// window's mouse, its keys and the pad's intents: it opens on the hub; the top bar's areas and Settings lead to
/// their pages and Back, at its own place, leads out of a page inside one, as Escape and the pad's B do; nothing in
/// the two bars moves when the page or its state changes; a lobby is left only after its question, and its pill
/// leads back to it from anywhere; the hints name the keys, or the pad's buttons once the pad is used; unticking
/// the box brings the classic launcher back at once. With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs
/// (1280 x 720, and at 125%).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private AppState State(string more = "\"experimentalUi\": true, ")
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, """ +
            more + "\"discPath\": " + System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(Data, standIn: true);
    }

    /// <summary>One of the top bar's areas, or its Settings icon.</summary>
    private static Button Area(NextShell shell, string name) => name == "Settings"
        ? Named<Button>(shell, "Settings")
        : Named<StackPanel>(shell, "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static string[] HintGlyphs(MainViewModel main) => [.. main.Shell.Hints.Select(h => h.Glyph)];

    private static readonly string[] BarTools = ["Online", "Profile", "Bell", "Chat", "Settings"];

    private static readonly string[] AreasRound = ["Garage", "Replays", "More", "Settings", "Play"];

    /// <summary>Where the top bar's parts stand, left edges in the window: the areas, the profile, the bell, the chat,
    /// Settings.</summary>
    private static double[] BarPlaces(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        var shell = Shell(window);
        var parts = Named<StackPanel>(shell, "Areas").GetVisualDescendants().OfType<Button>()
            .Concat(BarTools.Select(name => Named<Control>(shell, name)));
        return [.. parts.Select(part => Math.Round(part.TranslatePoint(default, window)!.Value.X, 1))];
    }

    [Fact]
    public async Task ItOpensOnTheHubAndTheBarsKeepTheirPlaces()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Assert.IsType<HubViewModel>(main.Current);
            Assert.Single(shell.GetVisualDescendants().OfType<HubView>());
            Assert.Equal(ShellViewModel.Play, main.Shell.Area);
            Assert.Contains("on", Area(shell, "Play").Classes);
            // the launcher's name at the top left, always; Back's place beside it, kept empty on an area's first page
            Assert.False(Named<Button>(shell, "Back").IsEffectivelyVisible);
            Assert.True(Named<StackPanel>(shell, "Wordmark").IsEffectivelyVisible);
            Assert.DoesNotContain(shell.GetVisualDescendants().OfType<Button>(), b => b.Name == "MyMachine");   // (the Garage has the machine)
            Assert.Equal(["Select", "Sections"], Hints(main));
            Assert.Equal(["Enter", "Q  E", "X"], HintGlyphs(main));          // (X: the chat, a key too)
            // the four entries, the big one first and with the focus
            var entries = shell.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("entry")).ToList();
            Assert.Equal(["Quick Play", "Custom lobby", "Practice", "Start F-Zero GX"], entries.Select(AutomationProperties.GetName));
            Assert.Contains("big", entries[0].Classes);
            Pump(() => entries[0].IsFocused, "the hub did not open on its main action");
            Assert.Equal("Race people online. Vote for the course, then drive.", main.Shell.Hub.Line);
            Shot(window, state, "next-hub");

            var places = BarPlaces(window);
            var barHeight = Named<Grid>(shell, "TopBar").Bounds.Height;
            var hintTop = Named<Border>(shell, "HintBar").TranslatePoint(default, window)!.Value.Y;
            foreach (var area in AreasRound)
            {
                Click(window, Area(shell, area));
                Assert.Equal(places, BarPlaces(window));
                Assert.Equal(barHeight, Named<Grid>(shell, "TopBar").Bounds.Height);
                Assert.Equal(hintTop, Named<Border>(shell, "HintBar").TranslatePoint(default, window)!.Value.Y);
                Shot(window, state, "next-area-" + area.ToLowerInvariant());
            }
            // inside an area (Back shows in its place) the bars are where they were
            Click(window, Said(shell, "Practice"));
            Assert.IsType<PracticeViewModel>(main.Current);
            Assert.True(Named<Button>(shell, "Back").IsEffectivelyVisible);
            Assert.True(Named<StackPanel>(shell, "Wordmark").IsEffectivelyVisible);
            Assert.Equal(places, BarPlaces(window));
            // the name pressed: the main menu (nothing to lose here: no question)
            Click(window, Named<Button>(shell, "Home"));
            Assert.IsType<HubViewModel>(main.Current);
            Assert.False(main.Shell.AskingConfirm);
            Click(window, Said(shell, "Practice"));
            Shot(window, state, "next-practice");

            // at 125% too
            state.Update(s => s with { InterfaceScale = 125 });
            Pump(() => Math.Abs(main.Scale - 1.25) < 0.001, "the scale was not taken");
            Click(window, Named<Button>(shell, "Back"));
            Assert.IsType<HubViewModel>(main.Current);
            Shot(window, state, "next-hub-125");
        });
    }

    [Fact]
    public async Task TheAreasLeadToTheirPagesAndBackLeadsOut()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            var back = Named<Button>(shell, "Back");

            // the hub's entries: a page inside Play each, Back to the hub
            Click(window, Said(shell, "Practice"));
            Assert.Same(main.Practice, main.Current);
            Assert.Equal(("Back", false), (main.Shell.BackText, main.Shell.BackLeaves));
            Click(window, back);
            Assert.IsType<HubViewModel>(main.Current);
            Click(window, Said(shell, "Custom lobby"));
            Assert.Same(main.Lobbies, main.Current);
            Press(window, Key.Escape, PhysicalKey.Escape);                // Escape is the pad's B
            Assert.IsType<HubViewModel>(main.Current);
            // Quick Play: the servers first, a page inside Play as the others; a server's "See its lobbies": the
            // lobbies and RACE
            Click(window, Said(shell, "Quick Play"));
            Assert.Same(main.Shell.Servers, main.Current);
            Assert.Equal(["Join", "Back"], Hints(main));
            Click(window, back);
            Assert.IsType<HubViewModel>(main.Current);
            Click(window, Said(shell, "Quick Play"));
            Click(window, Said(Page<ServersView>(window), StandInService.Name));
            Click(window, Said(Page<ServerSheetView>(window), "See its lobbies"));
            Assert.Same(main.Home, main.Current);
            Assert.Equal(["Join", "Race", "Back"], Hints(main));
            // (Play keeps it as the page last shown there)
            Click(window, Area(shell, "Replays"));
            Click(window, Area(shell, "Play"));
            Assert.Same(main.Home, main.Current);
            Click(window, back);
            Assert.IsType<HubViewModel>(main.Current);
            Click(window, Area(shell, "Settings"));
            Assert.Same(main.Settings, main.Current);
            Assert.Equal(ShellViewModel.SettingsArea, main.Shell.Area);
            Assert.Contains("on", Area(shell, "Settings").Classes);
            Assert.False(back.IsEffectivelyVisible);

            // Settings in steps: a group, and its Controller row a level to go into; Back a step at a time
            Click(window, Said(shell, "Basics"));
            Assert.True(back.IsEffectivelyVisible);
            Click(window, Said(shell, "Controller"));
            Assert.Same(main.Controller, main.Current);
            Click(window, back);
            Assert.Same(main.Settings, main.Current);
            Click(window, back);
            Assert.False(main.Shell.SettingsPage.IsOpen);
            Assert.False(back.IsEffectivelyVisible);

            // More: Mods and Stats
            Click(window, Area(shell, "More"));
            Assert.Same(main.Shell.More, main.Current);
            Assert.Equal(["Open", "Back"], Hints(main));
            Click(window, Said(shell, "Stats"));
            Assert.Same(main.Stats, main.Current);
            Assert.Equal(ShellViewModel.MoreArea, main.Shell.Area);
            Click(window, back);
            Click(window, Said(shell, "Mods"));
            Assert.Same(main.Mods, main.Current);
            // an area keeps the page last shown in it
            Click(window, Area(shell, "Garage"));
            Assert.Same(main.Garage, main.Current);
            Assert.Equal(["Select", "Sort & filter", "Shelves", "Back"], Hints(main));
            Click(window, Area(shell, "More"));
            Assert.Same(main.Mods, main.Current);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Same(main.Shell.More, main.Current);
            // from an area's first page B goes to Play; from the hub nowhere
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.IsType<HubViewModel>(main.Current);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.IsType<HubViewModel>(main.Current);

            // Q and E step through the areas and stop at the ends
            Press(window, Key.Q, PhysicalKey.Q);
            Assert.Equal(ShellViewModel.Play, main.Shell.Area);
            foreach (var area in new[] { ShellViewModel.Garage, ShellViewModel.Replays, ShellViewModel.MoreArea, ShellViewModel.SettingsArea, ShellViewModel.SettingsArea })
            {
                Press(window, Key.E, PhysicalKey.E);
                Assert.Equal(area, main.Shell.Area);
            }
            Press(window, Key.Q, PhysicalKey.Q);
            Assert.Equal(ShellViewModel.MoreArea, main.Shell.Area);

            // Backspace goes back as Escape does; so does the mouse's back button, and its forward button shows the
            // page Back left again; Ctrl+Tab and Ctrl+Shift+Tab step through the areas
            Click(window, Said(shell, "Stats"));
            Assert.Same(main.Stats, main.Current);
            Press(window, Key.Back, PhysicalKey.Backspace);
            Assert.Same(main.Shell.More, main.Current);
            var page = Named<ContentControl>(shell, "NextPageHost");
            Click(window, page, MouseButton.XButton2);
            Assert.Same(main.Stats, main.Current);
            Click(window, page, MouseButton.XButton1);
            Assert.Same(main.Shell.More, main.Current);
            Click(window, Area(shell, "Garage"));
            Click(window, page, MouseButton.XButton2);                // (another way to a page forgets what Back left)
            Assert.Same(main.Garage, main.Current);
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
            Assert.Equal(ShellViewModel.Replays, main.Shell.Area);
            Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(ShellViewModel.Garage, main.Shell.Area);
        });
    }

    [Fact]
    public async Task ALobbyIsLeftOnlyAfterItsQuestionAndItsPillLeadsBackToIt()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            var back = Named<Button>(shell, "Back");
            var pill = Named<Button>(shell, "LobbyPill");
            var question = Named<Panel>(shell, "ConfirmQuestion");
            Assert.False(pill.IsEffectivelyVisible);

            // into a custom lobby of the stand-in's
            main.Current = main.Lobbies;
            PumpTelling(state, () => main.Lobbies.Rows.Count == 3, "the browser listed nothing");
            main.Lobbies.ShowCreateCommand.Execute(null);
            main.Lobbies.CreateCommand.Execute(null);
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Chat.InLobby, "the lobby was not made");
            Assert.Equal(("Leave", true), (main.Shell.BackText, main.Shell.BackLeaves));
            Assert.Contains("danger", back.Classes);
            Assert.False(pill.IsEffectivelyVisible);
            Assert.Equal(["Remove racer", "Start race", "Rules", "Leave"], Hints(main));   // (the host's room)
            Shot(window, state, "next-lobby");

            // on another page the pill says the lobby and leads back
            Click(window, Area(shell, "Garage"));
            PumpTelling(state, () => pill.IsEffectivelyVisible, "the lobby's pill did not show");
            Assert.StartsWith("Gathering", main.Shell.LobbyPillText, StringComparison.Ordinal);
            Shot(window, state, "next-lobby-pill");
            Click(window, pill);
            Assert.Same(main.Lobby, main.Current);
            // Play is the lobby while the player is in one
            Click(window, Area(shell, "Replays"));
            Click(window, Area(shell, "Play"));
            Assert.Same(main.Lobby, main.Current);

            // Back asks; staying is the first answer, and Escape is staying
            Click(window, back);
            Pump(() => question.IsEffectivelyVisible, "the question did not show");
            Assert.Equal("Leave the lobby?", main.Shell.ConfirmTitle);
            Assert.Equal(["Select", "Cancel"], Hints(main));
            Pump(() => Said(question, "Stay").IsFocused, "the focus was not on Stay");
            Shot(window, state, "next-leave-question");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Pump(() => !question.IsEffectivelyVisible, "Escape did not close the question");
            Assert.Same(main.Lobby, main.Current);
            Assert.True(main.Chat.InLobby);
            Click(window, back);
            Pump(() => question.IsEffectivelyVisible, "the question did not show again");
            Click(window, Said(question, "Stay"));
            Assert.Same(main.Lobby, main.Current);

            // "Leave" leaves, and the hub shows
            Click(window, back);
            Pump(() => question.IsEffectivelyVisible, "the question did not show a third time");
            Click(window, Said(question, "Leave"));
            PumpTelling(state, () => main.Current is not LobbyViewModel && !main.Chat.InLobby, "the lobby was not left");
            Assert.False(question.IsEffectivelyVisible);
            Assert.Equal(ShellViewModel.Play, main.Shell.Area);
            Assert.False(pill.IsEffectivelyVisible);
        });
    }

    [Fact]
    public async Task ThePadGoesThroughTheAreasAndTheHintsShowWhatIsInUse()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            var pad = window.Pad!;
            void Do(PadIntent intent)
            {
                pad.Act(intent);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.False(main.Shell.PadInUse);
            Assert.Equal(("Q", "E"), (main.Shell.LeftKey, main.Shell.RightKey));
            Do(new PadMoved(PadMove.Down, Repeat: false));              // the first press shows where the pad is
            Assert.True(pad.Showing);
            Assert.True(main.Shell.PadInUse);
            Assert.Equal(["A", "L  R", "X"], HintGlyphs(main));                // (X: the chat; no Y on the main menu itself)
            Assert.Equal([true, false, true], main.Shell.Hints.Select(h => h.Round));
            // no pad known: the GameCube's own names, in its own look (the big green A, the grey shoulders)
            Assert.Equal(["A", "LR", "X"], main.Shell.Hints.Select(h => h.Cube));
            Assert.True(main.Shell.Hints[0].CubeA);
            Assert.True(main.Shell.Hints[1].CubeGrey);
            Assert.Equal(("L", "R"), (main.Shell.LeftKey, main.Shell.RightKey));
            Assert.True(shell.IsVisualAncestorOf(pad.On!));

            Do(new PadPressed(PadButton.L));
            Assert.Equal(ShellViewModel.Play, main.Shell.Area);
            foreach (var area in new[] { ShellViewModel.Garage, ShellViewModel.Replays, ShellViewModel.MoreArea, ShellViewModel.SettingsArea, ShellViewModel.SettingsArea })
            {
                Do(new PadPressed(PadButton.R));
                Assert.Equal(area, main.Shell.Area);
            }
            Assert.True(Named<ContentControl>(shell, "NextPageHost").IsVisualAncestorOf(pad.On!), "the pad lands in the page");
            // from the area's first page (Settings, one page) B goes to Play
            Assert.Same(main.Settings, main.Current);
            Do(new PadPressed(PadButton.B));
            Assert.IsType<HubViewModel>(main.Current);
            // Z (R1) steps through a page's sub-tabs: the hub has none
            Do(new PadPressed(PadButton.Z));
            Assert.IsType<HubViewModel>(main.Current);
            // X opens and closes the chat; Y is the main menu from anywhere (on the main menu itself, nothing)
            Do(new PadPressed(PadButton.X));
            Assert.True(main.Chat.Open);
            Do(new PadPressed(PadButton.X));
            Assert.False(main.Chat.Open);
            Do(new PadPressed(PadButton.Y));
            Assert.IsType<HubViewModel>(main.Current);
            Do(new PadPressed(PadButton.R));
            Assert.Equal(ShellViewModel.Garage, main.Shell.Area);
            Assert.Equal(["X", "Y"], HintGlyphs(main).TakeLast(2));
            Assert.Equal(["Chat", "Main menu"], main.Shell.Hints.TakeLast(2).Select(h => h.Label));
            Do(new PadPressed(PadButton.Y));
            Assert.IsType<HubViewModel>(main.Current);
            Assert.True(Named<ContentControl>(shell, "NextPageHost").IsVisualAncestorOf(pad.On!), "the pad lands on the main menu");

            // the mouse: the keys again
            window.MouseMove(new Point(300, 300));
            window.MouseMove(new Point(340, 320));
            Dispatcher.UIThread.RunJobs();
            Assert.False(main.Shell.PadInUse);
            Assert.Equal(["Enter", "Q  E", "X"], HintGlyphs(main));
        });
    }

    [Fact]
    public async Task UntickingTheBoxBringsTheClassicLauncherBackAtOnce()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Area(shell, "Settings"));
            var page = shell.GetVisualDescendants().OfType<SettingsPageView>().Single();
            Click(window, Shown(Said(page, "Launcher")));
            Dispatcher.UIThread.RunJobs();
            var row = page.GetVisualDescendants().OfType<Launcher.App.Controls.NextUi.OptionButton>()
                .Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Experimental UI");
            row.BringIntoView();
            row.Focus(Avalonia.Input.NavigationMethod.Directional);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("On", main.Shell.SettingsPage.Focused?.ValueText);
            Shot(window, state, "next-settings-launcher");
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Pump(() => Named<ScaledRoot>(window, "ScaleRoot").Child is not NextShell, "the classic interface did not come back");
            Assert.False(state.Settings.ExperimentalUi);
            Assert.Same(main.Settings, main.Current);
            Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());

            // from the hub, the classic interface shows its Quick Play page (the hub is the new interface's alone)
            state.Update(s => s with { ExperimentalUi = true });
            Pump(() => Named<ScaledRoot>(window, "ScaleRoot").Child is NextShell, "the new interface did not show");
            Click(window, Area(Shell(window), "Play"));
            Assert.IsType<HubViewModel>(main.Current);
            state.Update(s => s with { ExperimentalUi = false });
            Pump(() => Named<ScaledRoot>(window, "ScaleRoot").Child is not NextShell, "the classic interface did not come back again");
            Assert.Same(main.Home, main.Current);
            Assert.Single(window.GetVisualDescendants().OfType<HomeView>());
            // and there the pad's X and Y do nothing, as before (the chat and a second action are the new interface's)
            var pad = window.Pad!;
            pad.Act(new PadMoved(PadMove.Down, Repeat: false));
            pad.Act(new PadPressed(PadButton.X));
            pad.Act(new PadPressed(PadButton.Y));
            Dispatcher.UIThread.RunJobs();
            Assert.False(main.Chat.Open);
            Assert.Same(main.Home, main.Current);
        });
    }

    /// <summary>The plate's shape: two parallel corners cut, the focus outline the same distance off every side.</summary>
    [Fact]
    public void ThePlateCutsTwoParallelCorners()
    {
        Assert.Equal([new Point(10, 0), new Point(100, 0), new Point(100, 40), new Point(90, 50), new Point(0, 50), new Point(0, 10)],
            Chamfer.Outline(new Rect(0, 0, 100, 50), 10));
        // a cut longer than half the shorter side is held to it; none leaves the rectangle
        Assert.Equal(new Point(25, 0), Chamfer.Outline(new Rect(0, 0, 100, 50), 60)[0]);
        Assert.Equal(new Point(0, 0), Chamfer.Outline(new Rect(0, 0, 100, 50), 0)[0]);
        // the outline 3 px off a plate cut by 10: its cut's line 3 px off the plate's cut's line
        var cut = Chamfer.CutOutside(10, 3);
        var (a, b) = (new Point(-3 + cut, -3), new Point(-3, -3 + cut));
        var distance = Math.Abs(a.X + a.Y - 10) / Math.Sqrt(2);
        Assert.Equal(3, distance, 6);
        Assert.Equal(a.X + a.Y, b.X + b.Y, 6);
        Assert.Equal(0, Chamfer.CutOutside(0, 3));
    }

    [Theory]
    [InlineData("GATHERING", "4/8", "Gathering · 4/8")]
    [InlineData("COURSE VOTE", "0:12", "Course vote · 0:12")]
    [InlineData("JOINING", "", "Joining")]
    [InlineData("", "", "Lobby")]
    public void TheLobbysPillSaysItsStepAndItsFigure(string word, string figure, string said) =>
        Assert.Equal(said, ShellViewModel.LobbyWords(word, figure));

    /// <summary>The launcher's sounds heard, muted or not, with the pad working the pages (as the redesigned interface
    /// sets it).</summary>
    private AppState SoundState(bool muted)
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            $$$"""{"noServer": true, "experimentalUi": true, "padNavigation": true, "recordReplays": false, "soundMuted": {{{(muted ? "true" : "false")}}}, "soundVolume": 40, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(Data, standIn: true);
    }

    /// <summary>The pointer moving onto a control gives it the focus and sounds the menu's cursor, as a move of the pad
    /// does; moving about on the same control sounds nothing more. With the launcher's sounds muted, nothing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThePointerOnAControlSoundsTheCursor(bool muted)
    {
        var state = SoundState(muted);
        var cursors = 0;
        void Hear(LauncherSound sound, int volume)
        {
            if (sound == LauncherSound.Cursor)
            {
                cursors++;
            }
        }
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            void Over(Control control, double dx)
            {
                var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseMove(at + new Point(dx, 0));
                Dispatcher.UIThread.RunJobs();
            }
            var garage = Area(shell, "Garage");
            var replays = Area(shell, "Replays");
            Over(garage, -8);   // (the first move is where the pointer comes from)
            Attention.Sounded += Hear;
            try
            {
                Over(garage, 0);
                Assert.True(garage.IsFocused);
                Assert.Equal(muted ? 0 : 1, cursors);
                Over(garage, 6);                                  // (on the same control: no sound)
                Assert.Equal(muted ? 0 : 1, cursors);
                Over(replays, 0);
                Assert.True(replays.IsFocused);
                Assert.Equal(muted ? 0 : 2, cursors);
            }
            finally
            {
                Attention.Sounded -= Hear;
            }
        });
    }

    /// <summary>The owner's round of 2026-11-15: the hub's entries sound the cursor as the pointer comes onto them (the
    /// hub took the focus itself before, without a sound); a press on a large button (a hub entry, More's cards,
    /// Settings' groups) sounds the game's confirm, as "Use this machine" does; X on the keyboard opens and closes the
    /// chat, and the hint bar names it; the count of the players online is only shown, not pressed; the launcher's
    /// name pressed keeps no focus (its look stayed lit); More's cards are not cut at their list's edge; the Garage's
    /// machine raced is marked on its tile (green outline and name: .picked).</summary>
    [Fact]
    public async Task TheHubsEntriesAndLargeButtonsSoundAndTheChatHasItsKey()
    {
        var state = SoundState(muted: false);
        var heard = new List<LauncherSound>();
        void Hear(LauncherSound sound, int volume) => heard.Add(sound);
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            void Over(Control control, double dx)
            {
                var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseMove(at + new Point(dx, 0));
                Dispatcher.UIThread.RunJobs();
            }
            var hub = Page<HubView>(window);
            var entries = hub.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("entry")).ToList();
            Over(entries[1], -8);
            Attention.Sounded += Hear;
            try
            {
                Over(entries[2], 0);
                Assert.True(entries[2].IsFocused);
                Assert.Equal([LauncherSound.Cursor], heard);
                heard.Clear();
                Click(window, entries[2]);                                 // Practice
                Assert.Same(main.Practice, main.Current);
                Assert.Contains(LauncherSound.Confirm, heard);
                heard.Clear();
                Click(window, Area(shell, "More"));
                var more = Page<MenuView>(window);
                Assert.False(Named<ItemsControl>(more, "Rows").ClipToBounds);
                Click(window, Said(more, "Stats"));
                Assert.Contains(LauncherSound.Confirm, heard);
            }
            finally
            {
                Attention.Sounded -= Hear;
            }

            // X on the keyboard: the chat, and its hint after the page's own
            Click(window, Area(shell, "Play"));
            Assert.Equal("Chat", main.Shell.Hints[^1].Label);
            Assert.Equal("X", main.Shell.Hints[^1].Glyph);
            Press(window, Key.X, PhysicalKey.X);
            Assert.True(main.Chat.Open);
            Press(window, Key.X, PhysicalKey.X);
            Assert.False(main.Chat.Open);

            // the players online: shown, not pressed
            Assert.IsNotAssignableFrom<Button>(Named<Control>(shell, "Online"));

            // the launcher's name pressed: the main menu, and the focus on its page, not on the name
            Click(window, Area(shell, "Garage"));
            var picked = Page<GaragePageView>(window).GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("picked")).ToList();
            Assert.Single(picked);
            Assert.Equal(Machines.Names[6], AutomationProperties.GetName(picked[0]));
            var home = Named<Button>(shell, "Home");
            Click(window, home);
            PumpFor(200, state);
            Assert.IsType<HubViewModel>(main.Current);
            Assert.False(home.IsFocused);
        });
    }
}
