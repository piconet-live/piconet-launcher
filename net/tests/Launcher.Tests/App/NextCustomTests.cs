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
/// The redesigned interface's custom lobbies and Practice ("Experimental UI" on), in the launcher's own window on the
/// stand-in service: the list with Create lobby; Create as an option page (the basic rows, the mode's own, More rules
/// folded; left and right change the row the focus is on, its line in the help panel; a fixed course in a panel over
/// the page, Back closing that first); the room as its host (the rules in a line and all of them in a panel, Edit
/// rules as the same page and Back without a change, a racer removed after a question, Start race the main action);
/// Practice with its courses as cards and its rows. By themselves: what left, right and A do to each kind of row.
/// With <c>NEXT_SHOTS</c> naming a folder, the pages there as PNGs (1280 x 720).
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextCustomTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State()
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data, standIn: true);
    }

    [Fact]
    public async Task ALobbyIsCreatedOnAnOptionPageAndItsHostKeepsTheRoom()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Said(shell, "Custom lobby"));
            Assert.Same(main.Lobbies, main.Current);
            var list = Page<CustomListView>(window);
            Assert.Equal("CUSTOM LOBBIES", Named<TextBlock>(list, "Title").Text);
            PumpTelling(state, () => main.Lobbies.Status.Length == 0, "the stand-in's list did not come", 15);
            Assert.Equal(["Join", "Create lobby", "Back"], Hints(main));
            Shot(window, state, "next-custom-list");

            // Create: the rules as an option page, on the player's lobby's name
            Press(window, Key.S, PhysicalKey.S);                    // (S is the page's main action: Create lobby)
            Assert.True(main.Lobbies.Creating);
            Assert.Equal(["Change", "Select", "Next", "Back"], Hints(main));
            var rules = Assert.IsType<RulesPageViewModel>(main.Shell.CreateRules);
            var page = Page<RulesPageView>(window);
            Assert.Equal("NEW LOBBY", Named<TextBlock>(page, "Title").Text);
            // its first step: the lobby (its name, the password, the racers)
            Assert.Equal(0, rules.Step);
            Assert.Equal(["Name", "Password", "Racers"], rules.Shown.SelectMany(g => g.Items).Select(i => i.Label));
            Assert.Equal("ANA's lobby", rules.Shown[0].Items[0].ValueText);
            Assert.Equal("NEXT", Named<Button>(page, "Act").Content);
            // a row with the focus: the help panel says what it is, and left and right change it
            var racers = Row(page, "Racers");
            racers.Focus(NavigationMethod.Directional);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Racers", rules.Focused?.Label);
            Assert.Equal("The most racers in one race.", Named<TextBlock>(page, "HelpText").Text);
            var before = rules.Focused!.ValueText;
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.NotEqual(before, rules.Focused.ValueText);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(before, rules.Focused.ValueText);
            Shot(window, state, "next-custom-create");
            // NEXT (Start): the race (the mode, the course), the focus on its first row
            Press(window, Key.S, PhysicalKey.S);
            Assert.Equal(1, rules.Step);
            Assert.Equal(["Mode", "Course"], rules.Shown[0].Items.Select(i => i.Label));
            Assert.Equal(["Change", "Select", "Create", "Back"], Hints(main));
            Assert.Equal("CREATE LOBBY", Named<Button>(page, "Act").Content);
            Pump(() => rules.Focused?.Label == "Mode", "the focus did not go on to the race's first row");
            // More rules: a step of their own, and B a step back
            Assert.True(rules.HasMore);
            Click(window, Said(page, "More rules"));
            Assert.Equal(2, rules.Step);
            Assert.Contains(rules.Shown.SelectMany(g => g.Items), i => i.Label == "Latency limit");
            Shot(window, state, "next-custom-more");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal(1, rules.Step);
            Assert.True(main.Lobbies.Creating);
            // the launcher's name: the main menu, after a question (the new lobby's rules are not kept); Stay keeps them
            Click(window, Named<Button>(shell, "Home"));
            Assert.True(main.Shell.AskingConfirm);
            Assert.Equal("Leave the new lobby?", main.Shell.ConfirmTitle);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Assert.True(main.Lobbies.Creating);
            Assert.Same(main.Lobbies, main.Current);
            // a fixed course: its picker over the page, and Back closes that first
            var course = Row(page, "Course");
            course.Focus(NavigationMethod.Directional);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Dispatcher.UIThread.RunJobs();
            Assert.True(rules.Picking);
            Assert.True(Named<Panel>(page, "CoursePanel").IsEffectivelyVisible);
            Shot(window, state, "next-custom-course");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(rules.Picking);
            Assert.True(main.Lobbies.Creating);
            Assert.StartsWith("Fixed · ", rules.Basic[1].Items.Single(i => i.Label == "Course").ValueText, StringComparison.Ordinal);

            // CREATE LOBBY: the room, as its host
            Click(window, Said(page, "CREATE LOBBY"));
            PumpTelling(state, () => main.Current is LobbyViewModel { Phase: LobbyPhase.Room }, "the lobby was not made", 15);
            var room = Page<RoomView>(window);
            Assert.Equal("ANA'S LOBBY", Named<TextBlock>(room, "Title").Text);
            Assert.True(main.Lobby.HostCanEdit);
            Assert.True(Said(room, "Start race").IsEffectivelyVisible);
            Assert.Equal(["Remove racer", "Start race", "Rules", "Leave"], Hints(main));
            // all the rules in a panel (Y)
            Press(window, Key.Y, PhysicalKey.Y);
            var sheet = Assert.IsType<RulesSheetViewModel>(main.Shell.Sheet);
            Assert.Contains(sheet.Groups.SelectMany(g => g.Items), i => i.Label == "Course" && i.Marked);
            Shot(window, state, "next-custom-rules-panel");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(main.Shell.Sheet);
            // a racer comes; the host removes them after a question (keeping them is the green answer)
            PumpTelling(state, () => main.Lobby.People.Any(p => p.Kickable), "nobody came to the room", 15);
            Shot(window, state, "next-custom-room");
            var other = main.Lobby.People.First(p => p.Kickable);
            Click(window, Said(room, other.Name));
            Assert.True(main.Shell.AskingConfirm);
            Assert.Equal($"Remove {other.Name}?", main.Shell.ConfirmTitle);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Shell.AskingConfirm);
            Click(window, Said(room, other.Name));
            Click(window, Said(shell, "Remove"));
            PumpTelling(state, () => main.Lobby.People.All(p => p.Name != other.Name), "the racer stayed", 15);
            // Edit rules: the same page on the lobby's rules; Back keeps them as they were
            Click(window, Said(room, "Edit rules"));
            Assert.True(main.Lobby.HostEditing);
            var edit = Page<HostRulesView>(window);
            Assert.Equal("EDIT RULES", Named<TextBlock>(edit, "Title").Text);
            Assert.Equal(["Change", "Select", "Next", "Cancel"], Hints(main));
            Assert.Contains(main.Shell.HostRules!.Basic[0].Items, i => i.Label == "Password");   // (the lock's switch)
            Shot(window, state, "next-custom-edit");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(main.Lobby.HostEditing);
            Page<RoomView>(window);
        });
    }

    [Fact]
    public async Task PracticeShowsItsCoursesAsCardsAndItsRows()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Said(shell, "Practice"));
            Assert.Same(main.Practice, main.Current);
            var page = Page<PracticeSetupView>(window);
            // its first step: the course, the cups as chips and the courses as cards
            Assert.False(main.Practice.SettingUp);
            Assert.Equal(["Pick", "Cups", "Back"], Hints(main));
            var cards = Named<ItemsControl>(page, "Courses").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(main.Practice.Courses.Shown.Count, cards.Count);
            Assert.False(Named<Grid>(page, "SetUpStep").IsEffectivelyVisible);
            Shot(window, state, "next-practice-courses");
            // a cup's chip shows its courses
            var chips = Named<ItemsControl>(page, "Cups").GetVisualDescendants().OfType<Button>().ToList();
            Click(window, chips[1]);
            Assert.All(main.Practice.Courses.Shown, c => Assert.Equal(main.Practice.Courses.Cup, c.Cup));
            // a cup chosen: the next step is its courses, the focus on the one picked or the first
            Pump(() => Named<ItemsControl>(page, "Courses").GetVisualDescendants().OfType<Button>().Any(b => b.IsFocused), "the focus did not go on to the courses");
            // R1 (the GameCube's Z) steps through the cups as L2 and R2 step through the areas; Ctrl and the arrows too
            var cup = main.Practice.Courses.Cup;
            window.Pad!.Act(new PadPressed(PadButton.Z));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(cup, main.Practice.Courses.Cup);
            Assert.Same(main.Practice, main.Current);
            Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control);
            Assert.Equal(cup, main.Practice.Courses.Cup);
            // a card pressed: the course, and its set-up on a page of its own, the focus on START PRACTICE
            cards = [.. Named<ItemsControl>(page, "Courses").GetVisualDescendants().OfType<Button>()];
            Click(window, cards[3]);
            Assert.Equal(main.Practice.Courses.Shown[3].Course, main.Practice.Courses.Picked);
            Assert.True(main.Practice.SettingUp);
            Assert.False(Named<Grid>(page, "CourseStep").IsEffectivelyVisible);
            Assert.Equal(main.Practice.Courses.Shown[3].Track.ToUpperInvariant(), Named<TextBlock>(page, "Track").Text);
            Assert.Equal(["Change", "Select", "Start practice", "Back"], Hints(main));
            Pump(() => Named<Button>(page, "Start").IsFocused, "the focus did not go on to START PRACTICE");
            // the rows: the machine first; a row's value steps with left and right
            var rows = Named<OptionList>(page, "Rows");
            Assert.Equal("Machine", rows.GetVisualDescendants().OfType<OptionButton>().First(b => b.IsEffectivelyVisible).DataContext is OptionItem first ? first.Label : "");
            var step = rows.GetVisualDescendants().OfType<OptionButton>().Where(b => b.IsEffectivelyVisible).Select(b => (OptionItem)b.DataContext!)
                .First(i => i.Row is StepperRule or LapsRule or SwitchRule or ChoiceRule);
            var was = step.ValueText;
            Assert.True(step.Step(1) || step.Step(-1));
            Assert.NotEqual(was, step.ValueText);
            Shot(window, state, "next-practice");
            Focus(Row(page, "Machine"));
            // Back (Escape, as B): first the courses with the focus on the course chosen, then the hub
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.IsType<PracticeViewModel>(main.Current);
            Assert.False(main.Practice.SettingUp);
            Pump(() => TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() is Button { Classes: var classes } && classes.Contains("picked"),
                "the focus did not go back to the course chosen");
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.IsType<HubViewModel>(main.Current);
        });
    }

    [Fact]
    public void LeftRightAndAWorkEachKindOfRow()
    {
        // a choice: steps stop at its ends, and so does A (never round); its arrows say where the ends are
        var choice = new ChoiceRule("Stats", ["Stock", "GX+", "Mine"], 2);
        Assert.Equal((true, false), OptionSteps.Room(choice));
        Assert.False(OptionSteps.Step(choice, 1));
        Assert.True(OptionSteps.Step(choice, -1));
        Assert.Equal("GX+", OptionSteps.Words(choice));
        OptionSteps.Press(choice);
        OptionSteps.Press(choice);
        Assert.Equal("Mine", OptionSteps.Words(choice));
        Assert.True(OptionSteps.Step(choice, -1));
        Assert.True(OptionSteps.Step(choice, -1));
        Assert.Equal((false, true), OptionSteps.Room(choice));
        // a choice of the settings: the same
        var at = 0;
        var row = new ChoiceRow("Full screen mode", () => ["Window", "Borderless", "Full"], () => at, v => at = v);
        row.Press();
        row.Press();
        row.Press();
        Assert.Equal((2, (true, false)), (at, row.Room));
        // a number keeps its range and its step
        var stepper = new StepperRule("Vote", 30, 5, 120) { Step = 5, Unit = "s" };
        Assert.True(OptionSteps.Step(stepper, 1));
        Assert.Equal("35 s", OptionSteps.Words(stepper));
        // laps: past the most, Unlimited; back, the most again
        var laps = new LapsRule("Laps", 9, 9);
        Assert.True(OptionSteps.Step(laps, 1));
        Assert.Equal("Unlimited", OptionSteps.Words(laps));
        Assert.False(OptionSteps.Step(laps, 1));
        Assert.True(OptionSteps.Step(laps, -1));
        Assert.Equal("9", OptionSteps.Words(laps));
        // a number with "Off" below its least
        var mines = new NamedNumberRule("Mines respawn", "Off", false, 50, 50, 5000) { Step = 50, Unit = "ms" };
        Assert.True(OptionSteps.Step(mines, -1));
        Assert.Equal("Off", OptionSteps.Words(mines));
        Assert.True(OptionSteps.Step(mines, 1));
        Assert.Equal("50 ms", OptionSteps.Words(mines));
        // a switch: right on, left off, A flips
        var boost = new SwitchRule("Boost from the start", false);
        Assert.True(OptionSteps.Step(boost, 1));
        Assert.False(OptionSteps.Step(boost, 1));
        OptionSteps.Press(boost);
        Assert.Equal("Off", OptionSteps.Words(boost));
        // the course: Fixed names its course; A on it opens the picker
        var course = new CourseRule("Course", 0, 1);
        Assert.Equal("Vote", OptionSteps.Words(course));
        OptionSteps.Step(course, 2);
        Assert.True(course.Picking);
        course.ClosePickerCommand.Execute(null);
        Assert.StartsWith("Fixed · ", OptionSteps.Words(course), StringComparison.Ordinal);
        OptionSteps.Press(course);
        Assert.True(course.Picking);
        // the help: a rule's own line, else a general one; the item follows its row
        Assert.Equal("The most racers in one race.", RuleHelp.For(new StepperRule("Racers", 8, 2, 30)));
        Assert.Equal("A setting of this mode.", RuleHelp.For(new SwitchRule("Shields", true)));
        var item = new OptionItem(boost, "Race");
        boost.On = true;
        Assert.Equal("On", item.ValueText);
        Assert.Equal(OptionKind.Text, new OptionItem(new TextRule("Name", "Lobby name"), "").Kind);
        Assert.Equal(OptionKind.Value, new OptionItem(new ValueRule("Laps", "3"), "").Kind);
    }

    [Fact]
    public async Task FromACustomLobbyQuickPlayShowsTheServersAndAServerAsksBeforeTheLobbyIsLeft()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            // a custom lobby of the player's own, then the hub
            main.Current = main.Lobbies;
            PumpTelling(state, () => main.Lobbies.Rows.Count > 0, "the browser listed nothing");
            main.Lobbies.ShowCreateCommand.Execute(null);
            main.Lobbies.CreateCommand.Execute(null);
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Lobby.Phase == LobbyPhase.Room, "the lobby was not made");
            main.Current = main.Shell.Hub;
            Dispatcher.UIThread.RunJobs();

            // Quick Play: the servers, not the custom lobby
            Click(window, Said(shell, "Quick Play"));
            Assert.IsType<ServersViewModel>(main.Current);
            // a server pressed: asked first; "Leave and join" leaves the custom lobby and joins Quick Play
            Click(window, Said(Page<ServersView>(window), StandInService.Name));
            Click(window, Said(Page<ServerSheetView>(window), "Join a race"));
            Assert.Equal("Leave the custom lobby?", main.Shell.ConfirmTitle);
            Assert.True(main.Shell.AskingConfirm);
            Click(window, Named<Button>(shell, "ConfirmDo"));
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Lobby.Phase == LobbyPhase.Gather && state.Session is { Custom: false },
                "Quick Play was not joined", 15);
        });
    }

    /// <summary>A lobby joined while it races is the player's from then on: back on the list its row says so, and
    /// pressed it is their lobby's page, with no question. Another lobby pressed from it asks first, and then joins
    /// that one on the same visit (the list stays); so does Create. (Before, the question left the whole visit and
    /// joined nothing: the list stood empty.)</summary>
    [Fact]
    public async Task ALobbyJoinedWhileItRacesIsYoursAndAnotherIsJoinedAfterAQuestion()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            main.Current = main.Lobbies;
            PumpTelling(state, () => main.Lobbies.Rows.Count > 0, "the browser listed nothing");
            var racing = main.Lobbies.Rows.Single(r => r.Racing);
            Click(window, Said(Page<CustomListView>(window), racing.Name));
            PumpTelling(state, () => main.Current is LobbyViewModel && state.Session?.Lobby?.LobbyId == racing.Id, "the racing lobby was not joined", 15);
            Assert.False(main.Shell.AskingConfirm);

            // back on the list: its row is the player's; pressed, their lobby's page, nothing asked or left
            main.Current = main.Lobbies;
            PumpTelling(state, () => main.Lobbies.Rows.Any(r => r is { Mine: true }), "the list did not mark the player's lobby");
            Assert.Equal(racing.Id, main.Lobbies.Rows.Single(r => r.Mine).Id);
            var list = Page<CustomListView>(window);
            Assert.Single(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Yours" && t.IsEffectivelyVisible);
            Click(window, Said(list, racing.Name));
            Assert.False(main.Shell.AskingConfirm);
            Assert.Same(main.Lobby, main.Current);
            Assert.Equal(racing.Id, state.Session?.Lobby?.LobbyId);

            // another lobby: asked first (Stay keeps the player where they are), then joined on the same visit
            main.Current = main.Lobbies;
            Dispatcher.UIThread.RunJobs();
            var other = main.Lobbies.Rows.First(r => !r.Mine && !r.Locked);
            Click(window, Said(Page<CustomListView>(window), other.Name));
            Assert.True(main.Shell.AskingConfirm);
            Assert.Equal($"Leave {racing.Name}?", main.Shell.ConfirmTitle);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal(racing.Id, state.Session?.Lobby?.LobbyId);
            var visit = state.Session;
            Click(window, Said(Page<CustomListView>(window), other.Name));
            Click(window, Named<Button>(shell, "ConfirmDo"));
            PumpTelling(state, () => main.Current is LobbyViewModel && state.Session?.Lobby?.LobbyId == other.Id, "the other lobby was not joined", 15);
            Assert.Same(visit, state.Session);

            // Create from a lobby: asked first too, then the create face (the list still there behind it)
            main.Current = main.Lobbies;
            Dispatcher.UIThread.RunJobs();
            main.Lobbies.ShowCreateCommand.Execute(null);
            Assert.Equal($"Leave {other.Name}?", main.Shell.ConfirmTitle);
            Click(window, Named<Button>(shell, "ConfirmDo"));
            Assert.True(main.Lobbies.Creating);
            Assert.Same(visit, state.Session);
            Assert.NotEmpty(main.Lobbies.Rows);
        });
    }

    /// <summary>A server's "Host a lobby" in Quick Play is a Quick Play lobby (Quick Play's queue), never a custom one
    /// (the owner, 2026-11-15: the two are kept apart, as the classic launcher keeps RACE and the custom lobbies):
    /// the player is in Quick Play's lobby, not on the custom lobbies' Create.</summary>
    [Fact]
    public async Task AServersHostALobbyIsAQuickPlayLobby()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Click(window, Said(shell, "Quick Play"));
            Click(window, Said(Page<ServersView>(window), StandInService.Name));
            Click(window, Said(Page<ServerSheetView>(window), "Host a lobby"));
            PumpTelling(state, () => main.Current is LobbyViewModel && main.Lobby.Phase == LobbyPhase.Gather, "Host a lobby joined no Quick Play lobby", 15);
            var session = Assert.IsType<QuickPlaySession>(state.Session);
            Assert.False(session.Custom);
            Assert.False(session.Lobby?.Custom ?? true);
            Assert.False(main.Lobbies.Creating);
            Assert.Null(main.Shell.Sheet);
        });
    }
}
