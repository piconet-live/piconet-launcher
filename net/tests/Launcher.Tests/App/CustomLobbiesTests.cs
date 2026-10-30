using Avalonia.Controls;
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
using LobbyView = Launcher.App.Views.LobbyView;

namespace Launcher.Tests;

/// <summary>
/// Custom lobbies in the launcher's own window, pressed as a player does. On the stand-in service (the same session
/// code a server's visit runs): the browser lists its lobbies (no Quick Play queue on such a visit), a locked row
/// asks for the password (a wrong one said in a line, the right one joins), the lobby shows its host with the
/// host's mark and the rules that differ, leaving goes back to the browser; Create opens on the player's lobby's
/// name and Quick Play's rules, Edit rules opens the rules in their groups, CREATE makes the player the host with
/// Edit rules, START RACE and a Kick on the others' rows, and Kick removes one. On a led server: the racer goes
/// with the join, the host's controls are the host's only, and a new host (the old one left) is shown at once.
/// Create's line has Back where the lobby page has Leave, the rules' summary inside the panel under the preset, and
/// one toggle beside the preset that shows or hides every rule; a fixed course is picked in the one-course picker.
/// The lobby's name and lock come from its state: a rename by the host reaches a joiner.
/// With LOBBY_SHOTS naming a folder, the browser, Create, the lobby as host and as a joiner are kept there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class CustomLobbiesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(bool standIn)
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "recordReplays": false, "racer": {"name": "ANA", "machine": 6, "color": {"r": 1, "g": 2, "b": 3}}, "discPath": """ +
            System.Text.Json.JsonSerializer.Serialize(new SyntheticDisc().Write(_dir, "gx.iso")) + "}");
        return AppState.Load(data, standIn: standIn);
    }

    /// <summary>The window, on a 1280 x 800 page; the test's steps on the window's thread.</summary>
    private static async Task InTheWindow(AppState state, Action<MainWindow, MainViewModel> steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                steps(window, main);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }

    /// <summary>The window as it is drawn now, kept as a PNG where LOBBY_SHOTS names a folder.</summary>
    private static void Shot(MainWindow window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("LOBBY_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(400, state);   // (the transitions eased in)
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static T Page<T>(MainWindow window) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible);

    private static Button RowButton(Control view, string name) =>
        Named<ItemsControl>(view, "LobbyList").GetVisualDescendants().OfType<Button>().First(b => ((CustomLobbyRow)b.DataContext!).Name == name);

    private static List<SeatRow> Seated(LobbyViewModel lobby) => [.. lobby.SeatColumns.SelectMany(c => c.Seats).Where(s => !s.Open)];

    [Fact]
    public async Task OnTheStandInTheBrowserJoinsALockedLobbyByItsPasswordAndAHostKicks()
    {
        var state = State(standIn: true);
        await InTheWindow(state, (window, main) =>
        {
            // the browser: the stand-in's three lobbies, no Quick Play queue
            main.Current = main.Lobbies;
            var lobbies = main.Lobbies;
            PumpTelling(state, () => lobbies.Rows.Count == 3, "the browser listed nothing");
            Assert.Equal(["Big Blue nights", "Friday cup", "Mines stay gone"], lobbies.Rows.Select(r => r.Name));
            Assert.Equal(("ZOE", "2/12", true, "Waiting", "12 racers · no CPUs · ping limit 150 ms"),
                lobbies.Rows[1] is var friday ? (friday.Host, friday.Count, friday.Locked, friday.Stage, friday.Rules) : default);
            Assert.Equal(("Racing", "boost from the start · mines stay gone"), (lobbies.Rows[2].Stage, lobbies.Rows[2].Rules));
            Assert.True(state.Session is { Custom: true, Browsing: true, Lobby: null });
            var view = Page<CustomLobbiesView>(window);
            Shot(window, state, "browser");

            // a locked row asks first; a wrong password is said, the right one joins
            Click(window, RowButton(view, "Friday cup"));
            Assert.True(lobbies.Asking);
            Assert.True(Named<Plate>(view, "PasswordStrip").IsEffectivelyVisible);
            Assert.True(state.Session.Lobby is null);
            lobbies.Password = "no";
            Click(window, Named<Button>(view, "JoinLocked"));
            PumpTelling(state, () => lobbies.Notice == "Wrong password", "a wrong password was not said");
            Assert.Same(lobbies, main.Current);
            lobbies.Password = "gx";   // (the stand-in's made-up lobbies' password)
            Click(window, Named<Button>(view, "JoinLocked"));
            PumpTelling(state, () => main.Current is LobbyViewModel, "the right password did not join");

            // a joiner: the host and the host's mark, the rules that differ; no host controls
            var lobby = main.Lobby;
            PumpTelling(state, () => lobby.Custom && lobby.HostName == "ZOE");
            Assert.Equal(("Friday cup", "12 racers · no CPUs · ping limit 150 ms", false, false),
                (lobby.LobbyName, lobby.RulesSummary, lobby.IsHost, lobby.HostCanEdit));
            Assert.True(Seated(lobby).Single(s => s.Name == "ZOE").Host);
            Assert.DoesNotContain(Seated(lobby), s => s.Kickable);
            var page = Page<LobbyView>(window);
            Assert.False(Named<Button>(page, "StartRace").IsEffectivelyVisible);
            Click(window, Named<Button>(page, "AllRules"));
            Assert.True(lobby.AllRulesShown);
            Assert.Contains(lobby.AllRulePanels.SelectMany(p => p.Groups).SelectMany(g => g.Rows).OfType<ValueRule>(),
                r => r is { Label: "Racers", Value: "12", Marked: true });
            Shot(window, state, "lobby-all-rules");
            Click(window, Named<Button>(page, "AllRules"));
            Shot(window, state, "lobby-joiner");

            // leaving: back to the browser, still connected
            Click(window, Named<Button>(page, "LeaveHere"));
            PumpTelling(state, () => main.Current is CustomLobbiesViewModel && lobbies.Rows.Count == 3, "leaving did not go back to the browser");
            Assert.True(state.Session is { Custom: true, ConnectedTo: not null, Lobby: null });

            // Create: the player's lobby's name, Quick Play's rules, the rules behind Edit rules
            view = Page<CustomLobbiesView>(window);
            Click(window, Named<Button>(view, "CreateButton"));
            Assert.True(lobbies.Creating);
            Assert.Equal("ANA's lobby", lobbies.Editor!.Name);
            Assert.True(Named<Button>(view, "Back").IsEffectivelyVisible);
            Assert.Equal("Same as Quick Play", Named<RulesPanel>(view, "CreatePanel").GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Classes.Contains("note") && t.IsVisible).Text);                                // the summary inside the panel
            var toggle = Named<Button>(view, "RulesToggle");
            Assert.Equal("Edit rules", toggle.Content);
            Click(window, toggle);
            Assert.True(lobbies.EditingRules);
            Assert.Equal("Hide rules", toggle.Content);
            Assert.True(Named<ItemsControl>(view, "RulePanels").IsEffectivelyVisible);
            Click(window, toggle);
            Assert.False(Named<ItemsControl>(view, "RulePanels").IsEffectivelyVisible);
            Click(window, toggle);
            ((LapsRule)lobbies.Editor.Rule("laps")).Value = 5;
            lobbies.Editor.PasswordRow.Text = "pw";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5 laps", lobbies.Editor.SummaryShown);
            Shot(window, state, "create");

            // a fixed course: Fixed opens the one-course picker in the rules' place; a course pressed is the lobby's
            var course = lobbies.Editor.CourseRow;
            course.Selected = CourseRule.Fixed;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<StackPanel>(view, "CoursePane").IsEffectivelyVisible);
            Assert.False(Named<ItemsControl>(view, "RulePanels").IsEffectivelyVisible);
            Shot(window, state, "create-picker");
            var tile = Named<StackPanel>(view, "CoursePane").GetVisualDescendants().OfType<Button>()
                .First(b => b.DataContext is CourseCard { Course: 16 });
            Click(window, tile);
            Assert.False(Named<StackPanel>(view, "CoursePane").IsEffectivelyVisible);
            Assert.True(Named<ItemsControl>(view, "RulePanels").IsEffectivelyVisible);
            var fixedName = Launcher.Core.Courses.Name(16);
            Assert.Equal(fixedName, ((TextBlock)Named<Button>(view, "FixedCourse").Content!).Text);
            Assert.Equal(fixedName + " · 5 laps", lobbies.Editor.SummaryShown);
            Shot(window, state, "create2");
            Click(window, Named<Button>(Named<RulesPanel>(view, "CreatePanel"), "ActionButton"));
            PumpTelling(state, () => main.Current is LobbyViewModel && lobby.IsHost, "CREATE made no lobby");
            page = Page<LobbyView>(window);

            // the host: Edit rules, START RACE, a Kick on each other's row; the stand-ins come in
            PumpTelling(state, () => Seated(lobby).Count == 3, "the stand-ins did not come in");
            Assert.Equal(("ANA's lobby", true, "ANA", fixedName + " · 5 laps", true),
                (lobby.LobbyName, lobby.LobbyLocked, lobby.HostName, lobby.RulesSummary, lobby.HostCanEdit));
            Assert.Equal([("ANA", true, false), ("MIRA", false, true), ("JODY", false, true)],
                Seated(lobby).Select(s => (s.Name, s.Host, s.Kickable)));
            Assert.True(Named<Button>(page, "StartRace").IsEffectivelyVisible);
            Assert.True(Named<Button>(page, "EditRules").IsEffectivelyVisible);
            Shot(window, state, "lobby-host");
            var kick = page.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Kick" && b.IsEffectivelyVisible
                && ((SeatRow)b.DataContext!).Name == "MIRA");
            Click(window, kick);
            PumpTelling(state, () => Seated(lobby).Count == 2, "Kick removed nobody");
            Assert.Equal(["ANA", "JODY"], Seated(lobby).Select(s => s.Name));

            // the host's rules: Edit rules opens the panels on the lobby's rules; SAVE RULES sends them
            Click(window, Named<Button>(page, "EditRules"));
            Assert.True(lobby.HostEditing);
            Assert.Equal(5, ((LapsRule)lobby.HostEditor!.Rule("laps")).Value);
            Assert.Equal((CourseRule.Fixed, 16), (lobby.HostEditor.CourseRow.Selected, lobby.HostEditor.CourseRow.Course));
            ((LapsRule)lobby.HostEditor.Rule("laps")).Value = 7;
            lobby.HostEditor.NameRow.Text = "Saturday cup";
            lobby.HostEditor.LockedRow.On = false;
            Shot(window, state, "lobby-host-rules");
            Click(window, Named<Button>(Named<RulesPanel>(page, "HostPanel"), "ActionButton"));
            PumpTelling(state, () => lobby.RulesSummary == fixedName + " · 7 laps", "SAVE RULES changed nothing");
            PumpTelling(state, () => lobby.LobbyName == "Saturday cup" && !lobby.LobbyLocked, "the rename and the lock did not come back");

            // START RACE: the course vote opens
            Click(window, Named<Button>(page, "StartRace"));
            PumpTelling(state, () => lobby.Starting, "START RACE started nothing");
        });
    }

    [Fact]
    public async Task OnALedServerTheHostsControlsAreTheHostsAndANewHostIsShown()
    {
        var state = State(standIn: false);
        await using var server = new LedServer();
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(server.Address, custom: true);
            Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
            server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            PumpFor(200, state);
            Assert.Empty(server.Of<EnterQuickPlay>());                    // a custom visit never queues

            // the racer goes with the join
            var session = state.Session!;
            session.JoinLobbyAsync("l1", "pw").GetAwaiter().GetResult();
            Pump(() => server.Of<JoinLobby>().Count == 1, "the join was not sent");
            Assert.Equal(("l1", "pw", 6, new Rgb(1, 2, 3)), server.Of<JoinLobby>()[0] is var j ? (j.LobbyId, j.Password, j.Machine, j.Color) : default);

            // BEN hosts: his mark, no host controls for ANA
            var rules = new LobbyRules(new Dictionary<string, int> { ["laps"] = 4 });
            server.SendAsync(LedServer.Lobby(LobbyState.Forming) with { Custom = true, HostId = "p2", Rules = rules, Name = "BEN's lobby" })
                .GetAwaiter().GetResult();
            main.Current = main.Lobby;
            var lobby = main.Lobby;
            PumpTelling(state, () => lobby.HostName == "BEN");
            Assert.Equal(("BEN's lobby", false), (lobby.LobbyName, lobby.LobbyLocked));

            // the host renames the lobby and sets a password: the joiner's page says so from the lobby's state
            server.SendAsync(LedServer.Lobby(LobbyState.Forming) with { Custom = true, HostId = "p2", Rules = rules, Name = "Saturday cup", Locked = true })
                .GetAwaiter().GetResult();
            PumpTelling(state, () => lobby.LobbyName == "Saturday cup", "the rename did not reach the joiner");
            Assert.True(lobby.LobbyLocked);
            Assert.Equal("Saturday cup", state.Session!.CustomName);
            var renamed = Page<LobbyView>(window);
            Assert.Equal("Saturday cup", Named<TextBlock>(renamed, "LobbyName").Text);
            Assert.True(Named<Avalonia.Controls.Shapes.Path>(renamed, "LobbyLock").IsEffectivelyVisible);
            Shot(window, state, "joiner-renamed");
            Assert.Equal((false, false, "4 laps"), (lobby.IsHost, lobby.HostCanEdit, lobby.RulesSummary));
            Assert.DoesNotContain(Seated(lobby), s => s.Kickable);
            var page = Page<LobbyView>(window);
            Assert.True(Named<TextBlock>(page, "HostName").IsEffectivelyVisible);
            Assert.False(Named<Button>(page, "StartRace").IsEffectivelyVisible);
            lobby.StartRaceCommand.Execute(null);
            PumpFor(100, state);
            Assert.Empty(server.Of<StartLobbyRace>());                    // (the host's only)

            // BEN left: the server made ANA the host, and the page says so at once
            server.SendAsync(LedServer.Lobby(LobbyState.Forming) with { Custom = true, HostId = "p1", Rules = rules, Name = "Saturday cup", Locked = true })
                .GetAwaiter().GetResult();
            PumpTelling(state, () => lobby.HostName == "ANA");
            Assert.True(lobby.IsHost && lobby.HostCanEdit);
            Assert.Equal([("ANA", true, false), ("BEN", false, true)], Seated(lobby).Select(s => (s.Name, s.Host, s.Kickable)));
            Assert.True(Named<Button>(page, "StartRace").IsEffectivelyVisible);
            Click(window, Named<Button>(page, "StartRace"));
            Pump(() => server.Of<StartLobbyRace>().Count == 1, "START RACE sent nothing");
            var kick = page.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Kick" && b.IsEffectivelyVisible);
            Click(window, kick);
            Pump(() => server.Of<KickPlayer>() is [{ PlayerId: "p2" }], "Kick sent nothing");

            // removed by the host: the browser, and why
            server.SendAsync(new LeftLobby("l1", CustomLobby.Kicked)).GetAwaiter().GetResult();
            PumpTelling(state, () => main.Current is CustomLobbiesViewModel, "the player was left on the lobby page");
            PumpTelling(state, () => main.Lobbies.Notice == "Removed from the lobby by the host");
        });
    }

    /// <summary>The host's own stat pack: Choose file's row takes a .fzstats file, offered by its pack's name, and
    /// CREATE sends the file's text whole with the rules.</summary>
    [Fact]
    public async Task AHostsOwnPackFileGoesWithTheCreate()
    {
        const string Pack = "fzstats 1\nname Heavy Blue\nauthor ANA\n# heavier\nmachine 6 Weight 2520\n";
        var file = Path.Combine(_dir, "heavy-blue.fzstats");
        File.WriteAllText(file, Pack);
        var state = State(standIn: false);
        await using var server = new LedServer();
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(server.Address, custom: true);
            Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
            server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            main.Current = main.Lobbies;
            PumpFor(200, state);
            var view = Page<CustomLobbiesView>(window);
            Click(window, Named<Button>(view, "CreateButton"));
            Click(window, Named<Button>(view, "RulesToggle"));
            var row = Assert.IsType<PackRule>(main.Lobbies.Editor!.Rule(HostRules.StatPack.Name));
            var choose = Named<ItemsControl>(view, "RulePanels").GetVisualDescendants().OfType<Button>()
                .Single(b => b.Name == "PackFile" && b.IsEffectivelyVisible);
            Assert.Equal(("Choose file", row), (choose.Content, choose.DataContext));
            row.Load(file);   // (what Choose file does with the file the system's picker gives)
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Heavy Blue", row.Chosen);
            var box = Named<ItemsControl>(view, "RulePanels").GetVisualDescendants().OfType<ComboBox>().Single(c => c.DataContext == row);
            Assert.Equal("Heavy Blue", box.SelectedItem);
            Assert.Equal("Heavy Blue", main.Lobbies.Editor.SummaryShown);
            Shot(window, state, "create-pack-file");

            Click(window, Named<Button>(Named<RulesPanel>(view, "CreatePanel"), "ActionButton"));
            Pump(() => server.Of<CreateLobby>().Count == 1, "CREATE sent nothing");
            var sent = server.Of<CreateLobby>()[0].Rules;
            Assert.Equal(("Heavy Blue", Pack), (sent.StatPack, sent.PackFile));
            Assert.Equal(new HostPackFile("Heavy Blue", Pack), state.Session!.HostPack);
        });
    }

    /// <summary>The host's own game mode: the Mode row's Choose file takes a .fzmode file, compiled first: one that
    /// does not compile is said under the row with its line and leaves the choice as it was; one that does is offered
    /// by its mode's name, leads the rules' summary, and CREATE sends its text whole. LOBBY_SHOTS keeps both.</summary>
    [Fact]
    public async Task AHostsOwnModeFileIsCompiledFirstAndGoesWithTheCreate()
    {
        var good = File.ReadAllText(TestSupport.RepositoryFolder.Protocol("protocol", "tests", "vectors", "quickplay", "overdrive.fzmode"));
        var goodFile = Path.Combine(_dir, "overdrive.fzmode");
        File.WriteAllText(goodFile, good);
        var badFile = Path.Combine(_dir, "broken.fzmode");
        File.WriteAllText(badFile, "mode \"Broken\" {\n  api 1;\n}\n\nlaps 100;\n");
        var state = State(standIn: false);
        await using var server = new LedServer();
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(server.Address, custom: true);
            Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
            Assert.Equal(ModeWords.Api, server.Of<Hello>()[0].Modes);   // (the hello says the mode api its game runs)
            server.SendAsync(LedServer.Welcome()).GetAwaiter().GetResult();
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            main.Current = main.Lobbies;
            PumpFor(200, state);
            var view = Page<CustomLobbiesView>(window);
            Click(window, Named<Button>(view, "CreateButton"));
            var row = main.Lobbies.Editor!.ModeRow;
            Assert.Equal([GameModeRule.NoMode], row.Options);
            var choose = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ModeFile" && b.IsEffectivelyVisible);
            Assert.Equal(("Choose file", row), (choose.Content, choose.DataContext));

            row.Load(badFile);   // (what Choose file does with the file the system's picker gives)
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Not a mode: line 5: ", row.Note, StringComparison.Ordinal);
            Assert.Equal((0, false), (row.Selected, row.FileChosen));
            Shot(window, state, "create-mode-error");

            row.Load(goodFile);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("", "Overdrive"), (row.Note, row.Options[row.Selected]));
            Assert.StartsWith("Overdrive", main.Lobbies.Editor.SummaryShown, StringComparison.Ordinal);
            Shot(window, state, "create-mode-file");

            Click(window, Named<Button>(Named<RulesPanel>(view, "CreatePanel"), "ActionButton"));
            Pump(() => server.Of<CreateLobby>().Count == 1, "CREATE sent nothing");
            var sent = server.Of<CreateLobby>()[0].Rules;
            Assert.Equal((good, (LobbyMode?)null), (sent.ModeFile, sent.Mode));
        });
    }

    /// <summary>The lobby's mode as the server names it (<see cref="LobbyRules.Mode"/>): the host's rules open on it,
    /// and saving them without a new file sends it back to keep it (no file); None clears it. A member sees it at the
    /// head of the lobby's rules.</summary>
    [Fact]
    public void TheLobbysModeIsKeptUnlessChangedAndShownFirst()
    {
        var mode = new LobbyMode(new string('a', 64), "AAAAAAAA", "Overdrive", 1, "OVERDRIVE", "Speed locked and rising.");
        var rules = new LobbyRules(new Dictionary<string, int> { ["laps"] = 5 }, Mode: mode);
        var editor = new LobbyRulesEditor(rules, null, [], "Friday", editing: true);
        Assert.Equal([GameModeRule.NoMode, "Overdrive"], editor.ModeRow.Options);
        Assert.Equal((mode, (string?)null), (editor.Rules.Mode, editor.Rules.ModeFile));
        Assert.Equal("Overdrive · 5 laps", editor.Summary);
        editor.ModeRow.Selected = 0;
        Assert.Equal((null, (string?)null), (editor.Rules.Mode, editor.Rules.ModeFile));

        var all = CustomRules.AllRules(rules, null);
        var first = Assert.IsType<ValueRule>(Assert.Single(all[0].Rows));
        Assert.Equal(("Mode", "Overdrive", "Speed locked and rising."), (first.Label, first.Value, first.Note));
    }

    /// <summary>A custom lobby's own machine setting rules the Garage and the choice, not the server's Quick Play one
    /// (100 for everyone): a 0 lobby fixes the slider at 0, a lobby of each player's own frees it; the join and the
    /// choice carry the player's own setting, which the server brings into the lobby's range. The lobby's whole list
    /// of rules closes on another page.</summary>
    [Fact]
    public async Task ACustomLobbysOwnMachineSettingRulesTheGarageAndTheChoice()
    {
        var state = State(standIn: false);
        state.Update(s => s with { Racer = s.Racer with { Setting = 50 } });
        await using var server = new LedServer();
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(server.Address, custom: true);
            Pump(() => server.Of<Hello>().Count == 1, "the launcher did not connect");
            server.SendAsync(LedServer.Welcome(setting: SettingRange.QuickPlay)).GetAwaiter().GetResult();
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            var session = state.Session!;
            session.JoinLobbyAsync("l1", null).GetAwaiter().GetResult();
            Pump(() => server.Of<JoinLobby>().Count == 1, "the join was not sent");
            Assert.Equal(50, server.Of<JoinLobby>()[0].Setting);

            static LobbyRules Setting(int low, int high) =>
                new(new Dictionary<string, int> { ["machine-setting"] = low, [LobbyRules.SettingHighKey] = high });
            var garage = main.Garage;
            server.SendAsync(LedServer.Lobby(LobbyState.Forming) with { Custom = true, HostId = "p2", Rules = Setting(0, 0), Name = "Zero" })
                .GetAwaiter().GetResult();
            PumpTelling(state, () => garage.SettingFixed && garage.SettingMost == 0, "the Garage kept Quick Play's setting");
            Assert.Equal(0, garage.ShownSetting);

            server.SendAsync(LedServer.Lobby(LobbyState.Forming) with { Custom = true, HostId = "p2", Rules = Setting(0, 100), Name = "Own" })
                .GetAwaiter().GetResult();
            PumpTelling(state, () => !garage.SettingLimited, "the lobby of each player's own setting kept a limit");
            Assert.Equal(50, garage.ShownSetting);

            // the lobby's whole list of rules closes when the player goes to another page
            main.Current = main.Lobby;
            main.Lobby.ToggleAllRulesCommand.Execute(null);
            Assert.True(main.Lobby.AllRulesShown);
            main.Current = main.Garage;
            Assert.False(main.Lobby.AllRulesShown);

            server.SendAsync(new ChooseMachine("m1", 1, [.. Enumerable.Range(0, GameMachines.Count)], 40_000)).GetAwaiter().GetResult();
            PumpTelling(state, () => session.Choice is not null, "no choice came");
            session.ChooseMachineAsync(6).GetAwaiter().GetResult();
            Pump(() => server.Of<MachineChoice>().Count == 1, "the choice was not sent");
            Assert.Equal(50, server.Of<MachineChoice>()[0].Setting);
        });
    }

    // the overhaul: restore gone, what a mode owns, teams

    [Fact]
    public void RestoreIsNoRowOfALobby()
    {
        var editor = new LobbyRulesEditor(null, null, [], "Friday");
        var labels = editor.RuleGroups.SelectMany(g => g.Rows).Select(r => r.Label).ToList();
        Assert.DoesNotContain("Restore", labels);
        Assert.DoesNotContain(CustomRules.Groups.SelectMany(g => g.Rules), r => r.Name == "restore");
        Assert.DoesNotContain(editor.Rules.Values.Keys, k => k == "restore");
        Assert.DoesNotContain(CustomRules.AllRules(new LobbyRules(new Dictionary<string, int>()), null).SelectMany(g => g.Rows), r => r.Label == "Restore");
    }

    /// <summary>A mode's words win: the rows it owns leave the host's panel (their values kept for when the mode goes),
    /// the whole list says what is raced, and the rules line does not repeat them.</summary>
    [Fact]
    public void WhatTheModeOwnsLeavesThePanelAndTheListSaysIt()
    {
        var terms = ModeTerms.Of(ModeCompiler.Compile("mode \"M\" { api 1; }\nlaps 2;\nracers 2..6;\n").Mode!);
        var mode = new LobbyMode(new string('a', 64), "AAAAAAAA", "M", 1, Terms: terms.Rules());
        var rules = new LobbyRules(new Dictionary<string, int> { ["laps"] = 5, ["finish-limit"] = 200 }, Mode: mode);
        var editor = new LobbyRulesEditor(rules, null, [], "Friday", editing: true);
        string[] Shown() => [.. editor.RuleGroups.SelectMany(g => g.Rows).Select(r => r.Label)];
        foreach (var owned in new[] { "Laps", "Racers", "CPU racers", "Finish limit" })
        {
            Assert.DoesNotContain(owned, Shown());
        }
        Assert.Contains("Mines return", Shown());
        Assert.Contains("Teams", editor.LobbyGroups.SelectMany(g => g.Rows).Select(r => r.Label));
        Assert.Equal(5, editor.Rules.Value("laps"));                   // (the host's value, kept)
        editor.ModeRow.Selected = 0;
        Assert.Contains("Laps", Shown());
        Assert.Contains("Finish limit", Shown());
        Assert.DoesNotContain("Teams", editor.LobbyGroups.SelectMany(g => g.Rows).Select(r => r.Label));

        var all = CustomRules.AllRules(rules, null).SelectMany(g => g.Rows).OfType<ValueRule>().ToList();
        Assert.Equal("2", all.Single(r => r.Label == "Laps").Value);
        Assert.Equal("Off", all.Single(r => r.Label == "Finish limit").Value);
        Assert.Equal("M", CustomRules.Summary(rules, new LobbyRules(new Dictionary<string, int> { ["laps"] = 3, ["finish-limit"] = 90 })));
    }

    [Fact]
    public void TheTeamsRowFollowsTheModesWordAndGoesWithTheRules()
    {
        var path = Path.Combine(_dir, "teams.fzmode");
        File.WriteAllText(path, "mode \"Duel\" { api 1; }\nteams 2..4;\n");
        var editor = new LobbyRulesEditor(null, null, [], "Friday");
        Assert.DoesNotContain("Teams", editor.LobbyGroups.SelectMany(g => g.Rows).Select(r => r.Label));
        editor.ModeRow.Load(path);
        Assert.Equal(["2", "3", "4"], editor.TeamsRow.Options);
        Assert.Contains(editor.TeamsRow, editor.LobbyGroups.SelectMany(g => g.Rows));
        editor.TeamsRow.Selected = 1;
        Assert.Equal(3, editor.Rules.Value(LobbyRules.TeamsKey));

        var free = new LobbyRulesEditor(new LobbyRules(new Dictionary<string, int>(), Mode: new LobbyMode(new string('a', 64), "A", "M", 1)), null, [], "F", editing: true);
        Assert.Equal(("None", 30), (free.TeamsRow.Options[0], free.TeamsRow.Options.Count));
        Assert.Null(free.Rules.Value(LobbyRules.TeamsKey));
    }

    /// <summary>A lobby with teams on the stand-in: the host's panel with a mode and its teams, each seat's team, the
    /// player's own pressed for the next. LOBBY_SHOTS keeps them.</summary>
    [Fact]
    public async Task TeamsShowOnTheSeatsAndTheHostPressesThem()
    {
        var path = Path.Combine(_dir, "duel.fzmode");
        File.WriteAllText(path, "mode \"Duel\" { api 1; badge \"DUEL\"; about \"Two teams.\"; }\nteams 2..4;\nlaps 5;\nracers 2..12;\n");
        var state = State(standIn: true);
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(new Uri(StandInService.Address), custom: true);
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            main.Current = main.Lobbies;
            PumpFor(200, state);
            var view = Page<CustomLobbiesView>(window);
            Click(window, Named<Button>(view, "CreateButton"));
            var editor = main.Lobbies.Editor!;
            editor.ModeRow.Load(path);
            Dispatcher.UIThread.RunJobs();
            editor.TeamsRow.Selected = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Teams" && t.IsEffectivelyVisible);
            Shot(window, state, "create-mode-teams");

            Click(window, Named<Button>(Named<RulesPanel>(view, "CreatePanel"), "ActionButton"));
            PumpTelling(state, () => state.Session?.Lobby is { Custom: true }, "no lobby came");
            PumpTelling(state, () => main.Lobby.SeatColumns.SelectMany(c => c.Seats).Count(s => s.Id is not null) >= 3, "the stand-ins did not come");
            var mine = main.Lobby.SeatColumns.SelectMany(c => c.Seats).Single(s => s.You);
            Assert.Equal(("No team", true), (mine.TeamText, mine.TeamButton));
            main.Lobby.NextTeamCommand.Execute(mine);
            PumpTelling(state, () => main.Lobby.SeatColumns.SelectMany(c => c.Seats).Single(s => s.You).Team == 0, "the team did not change");
            var other = main.Lobby.SeatColumns.SelectMany(c => c.Seats).First(s => s is { You: false, Id: not null });
            main.Lobby.NextTeamCommand.Execute(other);
            PumpTelling(state, () => main.Lobby.SeatColumns.SelectMany(c => c.Seats).First(s => s.Id == other.Id).Team == 0, "the host did not change another's team");
            PumpTelling(state, () => main.Lobby.SeatColumns.SelectMany(c => c.Seats).Single(s => s.You).Machine.Length > 0, "no machine");
            // the host's seat, both marks on it: the machine has its whole name
            var seat = window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("seat") && g.DataContext is SeatRow { You: true });
            var machine = seat.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("machine"));
            machine.Measure(Avalonia.Size.Infinity);
            Assert.True(machine.Bounds.Width >= machine.DesiredSize.Width - 0.5, $"the machine {machine.Text} is cut: {machine.Bounds.Width} of {machine.DesiredSize.Width}");
            Shot(window, state, "lobby-teams");
        });
    }

    private const string WithParams = """
        mode "Cutter" { api 1; about "The last racer is out."; }
        param interval "Cut every" delay 10s..120s step 5s default 30s;
        param keep "Racers left" number 1..29 default 1;
        param rate "Speed rate" number 0.5..2.0 default 1.0;
        param loud "Warnings" switch default on;
        param shape "Order" choice ["Distance", "Time"] default "Time";
        laps 5;
        teams 2..4;
        """;

    /// <summary>A mode's parameters are the lobby's controls, from the compiled package: each kind its row on its
    /// default, what the host moves goes in the rules in the package's order, into the summary, and comes back kept
    /// from the mode the lobby has; another mode, or none, takes the rows away.</summary>
    [Fact]
    public void AModesParametersAreRowsThatGoWithTheRules()
    {
        var path = Path.Combine(_dir, "cutter.fzmode");
        File.WriteAllText(path, WithParams);
        var editor = new LobbyRulesEditor(null, null, [], "Friday");
        Assert.Null(editor.Rules.ModeValues);
        editor.ModeRow.Load(path);
        var rows = editor.ParamRows;
        Assert.Equal(["Cut every", "Racers left", "Speed rate", "Warnings", "Order"], rows.Select(r => r.Row.Label));
        Assert.All(rows, r => Assert.Contains(r.Row, editor.LobbyGroups.SelectMany(g => g.Rows)));
        Assert.Equal(rows.Select(r => r.Param.Default), editor.Rules.ModeValues!);
        var cut = Assert.IsType<ChoiceRule>(rows[0].Row);
        Assert.Equal(("10 s", "120 s", 23, "30 s"), (cut.Options[0], cut.Options[^1], cut.Options.Count, cut.Chosen));
        Assert.IsType<StepperRule>(rows[1].Row);
        Assert.Equal("1", Assert.IsType<ChoiceRule>(rows[2].Row).Chosen);
        Assert.True(Assert.IsType<SwitchRule>(rows[3].Row).On);
        Assert.Equal("Time", Assert.IsType<ChoiceRule>(rows[4].Row).Chosen);

        cut.Selected = cut.Options.ToList().IndexOf("15 s");
        ((StepperRule)rows[1].Row).Value = 4;
        Assert.Equal(900, editor.Rules.ModeValues![0]);
        Assert.Equal(4, editor.Rules.ModeValues![1]);
        Assert.Contains("Cut every 15 s · Racers left 4", editor.SummaryShown, StringComparison.Ordinal);

        var package = ModeCompiler.Compile(WithParams).Mode!;
        Assert.Null(package.ValuesRefusal(editor.Rules.ModeValues!));
        var named = new LobbyMode(new string('a', 64), "A", "Cutter", 1, Params: package.Params, Values: editor.Rules.ModeValues);
        var again = new LobbyRulesEditor(new LobbyRules(new Dictionary<string, int>(), Mode: named), null, [], "F", editing: true);
        Assert.Equal(editor.Rules.ModeValues, again.Rules.ModeValues!);
        Assert.Equal(editor.Rules.ModeValues, again.Rules.Mode!.Values!);
        again.ModeRow.Selected = 0;
        Assert.Empty(again.ParamRows);
        Assert.Null(again.Rules.ModeValues);

        var all = CustomRules.AllRules(new LobbyRules(new Dictionary<string, int>(), Mode: named), null).SelectMany(g => g.Rows).OfType<ValueRule>().ToList();
        Assert.Equal(("15 s", true), (all.Single(r => r.Label == "Cut every").Value, all.Single(r => r.Label == "Cut every").Marked));
        Assert.Equal(("Time", false), (all.Single(r => r.Label == "Order").Value, all.Single(r => r.Label == "Order").Marked));
    }

    [Fact]
    public void AStepperOnStepsKeepsATypedValueOnThem()
    {
        var row = new StepperRule("N", 4, 2, 9) { Step = 2 };
        row.Value = 5;
        Assert.True(row.Value is 4 or 6);
        row.Value = 100;
        Assert.Equal(8, row.Value);
    }

    /// <summary>A value a server sends outside the declaration is no control and no value; a seat's teams likewise.</summary>
    [Fact]
    public void AModesParametersFromAServerAreCheckedBeforeTheyAreShown()
    {
        var package = ModeCompiler.Compile(WithParams).Mode!;
        LobbyMode Mode(params int[] values) => new(new string('a', 64), "A", "Cutter", 1, Params: package.Params, Values: values);
        LobbyState Of(LobbyMode mode) => LedServer.Lobby(LobbyState.Forming) with { Rules = new LobbyRules(new Dictionary<string, int>(), Mode: mode) };
        var good = package.Params.Select(p => p.Default).ToArray();
        Assert.Equal(good, Assert.IsType<LobbyState>(ServerMessages.Checked(Of(Mode(good)))).Rules!.Mode!.Values!);
        var bad = (int[])good.Clone();
        bad[0] = 1;
        Assert.Null(Assert.IsType<LobbyState>(ServerMessages.Checked(Of(Mode(bad)))).Rules!.Mode!.Params);
        Assert.Null(Assert.IsType<LobbyState>(ServerMessages.Checked(Of(Mode(4)))).Rules!.Mode!.Values);

        var race = new LobbyRace("m", 1, 3, [], DateTimeOffset.UnixEpoch, Teams: [0, 1]);
        LobbyState Raced(LobbyRace r) => LedServer.Lobby(LobbyState.Racing) with { Race = r };
        Assert.Equal([0, 1], Assert.IsType<LobbyState>(ServerMessages.Checked(Raced(race))).Race!.Teams!);
        Assert.Null(Assert.IsType<LobbyState>(ServerMessages.Checked(Raced(race with { Teams = [0, 99] }))).Race!.Teams);
    }

    /// <summary>The Create page with a mode's settings, and a lobby with them in All rules. LOBBY_SHOTS keeps them.</summary>
    [Fact]
    public async Task AModesSettingsShowInThePanelAndInTheLobbysRules()
    {
        var path = Path.Combine(_dir, "cutter.fzmode");
        File.WriteAllText(path, WithParams);
        var state = State(standIn: true);
        await InTheWindow(state, (window, main) =>
        {
            var joining = state.JoinAsync(new Uri(StandInService.Address), custom: true);
            PumpTelling(state, () => joining.IsCompleted && state.Session?.PlayerId is not null);
            main.Current = main.Lobbies;
            PumpFor(200, state);
            var view = Page<CustomLobbiesView>(window);
            Click(window, Named<Button>(view, "CreateButton"));
            var editor = main.Lobbies.Editor!;
            editor.ModeRow.Load(path);
            Dispatcher.UIThread.RunJobs();
            var cut = (ChoiceRule)editor.ParamRows[0].Row;
            cut.Selected = cut.Options.ToList().IndexOf("15 s");
            Dispatcher.UIThread.RunJobs();
            foreach (var label in (string[])["Cut every", "Racers left", "Speed rate", "Warnings", "Order"])
            {
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == label && t.IsEffectivelyVisible);
            }
            Shot(window, state, "create-mode-params");

            Click(window, Named<Button>(Named<RulesPanel>(view, "CreatePanel"), "ActionButton"));
            PumpTelling(state, () => state.Session?.Lobby is { Custom: true }, "no lobby came");
            var mode = state.Session!.Lobby!.Rules!.Mode!;
            Assert.Equal(900, mode.Values![0]);
            PumpFor(300, state);
            Shot(window, state, "lobby-params");
            main.Lobby.ToggleAllRulesCommand.Execute(null);
            PumpFor(300, state);
            Shot(window, state, "lobby-params-all-rules");
        });
    }
}
