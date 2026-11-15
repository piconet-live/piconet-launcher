using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>One of the hint bar's hints: the controller's button, or the key that does the same while the keyboard
/// and the mouse are in use, and what it does on this page. <see cref="Round"/>: a face button of the controller,
/// drawn round. <see cref="Everywhere"/>: one of the hints every page has (the chat, the main menu), after the page's own.</summary>
public sealed record Hint(string Glyph, string Label, bool Round, string Cube = "", bool Everywhere = false)
{
    /// <summary>The GameCube controller's own look for the button (<see cref="Cube"/>: "A", "B", "X", "Y", "Z", "Start",
    /// "LR"): the big green A, the small red B, the grey X and Y, the violet Z, the grey Start and shoulders.</summary>
    public bool CubeA => Cube == "A";

    public bool CubeB => Cube == "B";

    public bool CubeXY => Cube is "X" or "Y";

    public bool CubeZ => Cube == "Z";

    public bool CubeGrey => Cube is "Start" or "LR";
}

/// <summary>
/// The redesigned interface's shell (Views/NextUi/NextShell): where the window is among its areas (Play, Garage,
/// Replays, More; Settings as the fifth), the way back from a page inside one, the hints for the page showing and
/// the states the top bar shows (the lobby the player is in, the graphics being prepared, an update). A page inside
/// an area is a level one goes into: Back, at a place of its own in the top bar, and B take the same way out. The
/// pages themselves, and everything they do, are the classic launcher's (<see cref="MainViewModel"/>).
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    public const string Play = "play", Garage = "garage", Replays = "replays", MoreArea = "more", SettingsArea = "settings";

    /// <summary>The areas as L and R go through them, Settings last (its icon is at the top bar's right end).</summary>
    public static IReadOnlyList<string> AreaOrder { get; } = [Play, Garage, Replays, MoreArea, SettingsArea];

    private readonly MainViewModel _main;
    private readonly AppState _state;
    private readonly Dictionary<string, ViewModelBase> _last = [];
    private readonly DispatcherTimer _recallClock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _quickPlayAsked;
    private bool _lobbiesFromServers;
    private ServersViewModel? _servers;

    public ShellViewModel(MainViewModel main, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(state);
        _main = main;
        _state = state;
        Hub = new HubViewModel(main, this, state);
        More = new MenuViewModel("More",
        [
            new MenuRow("Mods", "Add-ons for the game, in the order they apply", () => main.Current = main.Mods),
            new MenuRow("Stats", "Your races, wins and best times", () => main.Current = main.Stats),
            new MenuRow("About", "The version, updates, developer mode and the licences", () => main.Current = About),
        ]);
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Current) or nameof(MainViewModel.ExperimentalUi))
            {
                Paged();
            }
            else if (e.PropertyName == nameof(MainViewModel.DeveloperMode))
            {
                ShowDevLines();
            }
        };
        main.Control.PropertyChanged += (_, _) => Refresh();
        main.Graphics.PropertyChanged += (_, _) => Refresh();
        main.Update.PropertyChanged += (_, _) => Refresh();
        main.Chat.PropertyChanged += (_, _) => Refresh();
        main.Notices.PropertyChanged += (_, _) => Refresh();
        main.Practice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PracticeViewModel.SettingUp))
            {
                Refresh();   // (the course and the set-up have hints of their own)
            }
        };
        main.Lobbies.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CustomLobbiesViewModel.Editor))
            {
                CreateRules = main.Lobbies.Editor is { } editor
                    ? new RulesPageViewModel(editor, "CUSTOM LOBBY", "New lobby", "CREATE LOBBY", main.Lobbies.CreateCommand, main.Lobbies.BackCommand)
                    : null;
            }
            if (e.PropertyName is nameof(CustomLobbiesViewModel.Notice) or nameof(CustomLobbiesViewModel.Editor) && CreateRules is { } rules)
            {
                rules.Notice = main.Lobbies.Notice;
            }
            if (e.PropertyName is nameof(CustomLobbiesViewModel.Creating) or nameof(CustomLobbiesViewModel.Asking) or nameof(CustomLobbiesViewModel.InQuickPlay))
            {
                Refresh();
            }
        };
        main.Lobby.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LobbyViewModel.HostEditor))
            {
                HostRules = main.Lobby.HostEditor is { } editor
                    ? new RulesPageViewModel(editor, "CUSTOM LOBBY", "Edit rules", "SAVE RULES", main.Lobby.SaveRulesCommand, main.Lobby.CancelRulesCommand)
                    : null;
            }
            if (e.PropertyName is nameof(LobbyViewModel.Phase) or nameof(LobbyViewModel.FullGarage) or nameof(LobbyViewModel.HostEditing)
                or nameof(LobbyViewModel.HostCanEdit))
            {
                Phased();
            }
            else if (e.PropertyName == nameof(LobbyViewModel.BackdropCourse))
            {
                Refresh();
            }
        };
        state.Changed += () => OnUi(() =>
        {
            Raced();
            Refresh();
        });
        state.Recall.Changed += () => OnUi(ShowRecall);
        _recallClock.Tick += (_, _) => ShowRecall();
        Raced();
        Paged();
    }

    /// <summary>Quick Play's first page: the servers to race on (made when first shown; its servers are asked only
    /// while it shows).</summary>
    public ServersViewModel Servers => _servers ??= MakeServers();

    private ServersViewModel MakeServers()
    {
        var servers = new ServersViewModel(_state, _main.StatusAnswers, () => _main.Control.GoCommand.ExecuteAsync(null))
        {
            ShowSheet = sheet => Sheet = sheet,
            Hosting = _main.Control.HostAsync,
            Browsing = ShowLobbiesPage,
        };
        servers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServersViewModel.Adding))
            {
                Refresh();   // (the address box has hints of its own)
            }
        };
        return servers;
    }

    /// <summary>Play's first page: the hub.</summary>
    public HubViewModel Hub { get; }

    /// <summary>The Settings area's page: every setting as option rows, made when first shown.</summary>
    public SettingsPageViewModel SettingsPage => _settingsPage ??= new SettingsPageViewModel(_main, this, _state);

    /// <summary>A page stepped inside itself (Settings: a group opened or closed): its way back and its hints again.</summary>
    public void Stepped() => Refresh();

    private SettingsPageViewModel? _settingsPage;

    /// <summary>Settings › Controller as option rows, made when first shown.</summary>
    public ControllerPageViewModel ControllerPage => _controllerPage ??= new ControllerPageViewModel(_main.Controller, _main.Settings);

    private ControllerPageViewModel? _controllerPage;

    /// <summary>Developer mode's Diagnostics (the DEV chip in the top bar).</summary>
    [RelayCommand]
    private void OpenDiagnostics() => Sheet = new DiagnosticsSheetViewModel(_main, this, _state);

    /// <summary>The control the focus is on, by its name (Developer mode's line about input says it).</summary>
    [ObservableProperty]
    public partial string FocusName { get; set; } = "";

    partial void OnFocusNameChanged(string value) => ShowDevLines();

    /// <summary>Developer mode's two lines in the hint bar: the place the window is at, and the input and focus.</summary>
    [ObservableProperty]
    public partial string DevPlace { get; private set; } = "";

    [ObservableProperty]
    public partial string DevInput { get; private set; } = "";

    private void ShowDevLines() =>
        (DevPlace, DevInput) = _main.DeveloperMode ? DevLines.Of(_main, _state, this) : ("", "");

    /// <summary>The first start in three steps (the classic first screen's), made when first shown.</summary>
    public FirstStartViewModel FirstStart => _firstStart ??= MakeFirstStart();

    private FirstStartViewModel? _firstStart;

    private FirstStartViewModel MakeFirstStart()
    {
        var first = new FirstStartViewModel(_main, this);
        first.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstStartViewModel.Step))
            {
                Refresh();   // (Back, and its hint, from the second step on)
            }
        };
        return first;
    }

    /// <summary>More › About, made when first shown.</summary>
    public AboutViewModel About => _about ??= new AboutViewModel(_main, this);

    private AboutViewModel? _about;

    /// <summary>The profile (the top bar's chip), made when first shown.</summary>
    public ProfileViewModel Profile => _profile ??= new ProfileViewModel(_main, this, _state);

    private ProfileViewModel? _profile;

    // the page the profile was opened from: its Back goes there
    private ViewModelBase? _beforeProfile;

    /// <summary>A replay's row pressed: its panel.</summary>
    [RelayCommand]
    private void OpenReplay(ReplayItem? item)
    {
        if (item is not null)
        {
            Sheet = new ReplaySheetViewModel(_main.Replays, item, this);
        }
    }

    /// <summary>A mod's row pressed: its panel.</summary>
    [RelayCommand]
    private void OpenMod(ModCard? card)
    {
        if (card is not null)
        {
            Sheet = new ModSheetViewModel(_main.Mods, card, this);
        }
    }

    /// <summary>The top bar's profile chip: the profile.</summary>
    [RelayCommand]
    private void ShowProfile()
    {
        if (_main.Navigation && _main.Current is not ProfileViewModel)
        {
            _beforeProfile = _main.Current;
            _main.Current = Profile;
        }
    }

    /// <summary>The Garage's page (the Garage area's, and a lobby's whole Garage: the same Garage), made when first
    /// shown.</summary>
    public GaragePageViewModel GaragePage => _garagePage ??= MakeGaragePage();

    private GaragePageViewModel? _garagePage;

    private GaragePageViewModel MakeGaragePage()
    {
        var page = new GaragePageViewModel(_main.Garage, this);
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GaragePageViewModel.Building))
            {
                Refresh();   // (the builder has a way back and hints of its own)
            }
        };
        return page;
    }

    /// <summary>A Garage shows: the Garage area's page, or a lobby's whole Garage.</summary>
    private static bool GarageShows(ViewModelBase? page) => page is GarageViewModel or LobbyViewModel { FullGarage: true };

    /// <summary>The builder shows over a Garage.</summary>
    private bool Building(ViewModelBase? page) => GarageShows(page) && _garagePage is { Building: true };

    /// <summary>The More area's first page: its places as rows.</summary>
    public MenuViewModel More { get; }

    public MainViewModel Main => _main;

    /// <summary>The area showing, by its name; none on the first screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnSettings))]
    public partial string Area { get; set; } = "";

    public bool OnSettings => Area == SettingsArea;

    /// <summary>Back shows (the page is inside an area, or a step into a page); its place is kept either way.</summary>
    [ObservableProperty]
    public partial bool BackShown { get; set; }

    /// <summary>Back's word: Back, or what leaving this page means (Leave, Leave the race, Leave the queue).</summary>
    [ObservableProperty]
    public partial string BackText { get; set; } = "Back";

    /// <summary>Back leaves something (a lobby, a race, the queue): its word is red.</summary>
    [ObservableProperty]
    public partial bool BackLeaves { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Hint> Hints { get; set; } = [];

    /// <summary>The pad was used last (not the mouse or the keyboard): the hints show its buttons, else the keys.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftKey), nameof(RightKey))]
    public partial bool PadInUse { get; set; }

    /// <summary>What steps to the area before and to the one after: L and R as the pad's family names them (a pad's
    /// triggers, LT and RT), or Q and E.</summary>
    public string LeftKey => PadInUse ? PadIcons.Shoulders(PadLook().Family).Left : "Q";

    public string RightKey => PadInUse ? PadIcons.Shoulders(PadLook().Family).Right : "E";

    partial void OnPadInUseChanged(bool value) => Refresh();

    /// <summary>The page shows the background as it is (the hub, which is made for it); every other page has it
    /// turned down and soft behind its rows.</summary>
    [ObservableProperty]
    public partial bool Clear { get; set; }

    /// <summary>The page is made for the room it has (it scrolls its own lists, its main action stays in sight): the
    /// shell does not scroll it as a whole. The classic pages scroll as a whole.</summary>
    [ObservableProperty]
    public partial bool PageFits { get; set; }

    /// <summary>The top bar's pill for the lobby the player is in, on every page but the lobby's: back to it.</summary>
    [ObservableProperty]
    public partial bool LobbyPillShown { get; set; }

    [ObservableProperty]
    public partial string LobbyPillText { get; set; } = "";

    /// <summary>The panel open over the page, from the window's right edge (the lobby's, the race's racers); null:
    /// none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetOpen))]
    public partial ViewModelBase? Sheet { get; set; }

    public bool SheetOpen => Sheet is not null;

    partial void OnSheetChanged(ViewModelBase? value) => Refresh();

    partial void OnSheetChanged(ViewModelBase? oldValue, ViewModelBase? newValue) => (oldValue as IDisposable)?.Dispose();

    /// <summary>Something is open over the page (a question, a panel, the notices, an unpinned chat): B closes it first.</summary>
    public bool OverPage => KeyboardOpen || AskingConfirm || _main.Settings.AskingExperimental || _main.Graphics.Asking || _main.Control.Asking
        || SheetOpen || _main.Notices.ListOpen || _main.Chat.Overlay;

    /// <summary>The area a page belongs to; none for the first screen.</summary>
    public static string AreaOf(ViewModelBase? page) => page switch
    {
        HubViewModel or ServersViewModel or HomeViewModel or LobbyViewModel or LanViewModel or PracticeViewModel or CustomLobbiesViewModel => Play,
        GarageViewModel => Garage,
        ReplaysViewModel => Replays,
        StatsViewModel or ModsViewModel or AboutViewModel => MoreArea,
        MenuViewModel => MoreArea,
        SettingsViewModel or ControllerViewModel => SettingsArea,
        _ => "",
    };

    /// <summary>The player is in a Quick Play or custom lobby, or on the way into one (not on a visit to the custom
    /// lobbies' browser, which is in none).</summary>
    private bool InLobby => _main.Control.InQuickPlay;

    /// <summary>The player is in Quick Play (its queue or one of its lobbies), not in a custom lobby.</summary>
    private bool InQuickPlayItself => InLobby && _state.Session is not { Custom: true };

    /// <summary>An area pressed: the page last shown there, its first page at first.</summary>
    [RelayCommand]
    public void ShowArea(string area)
    {
        if (!_main.Navigation || !AreaOrder.Contains(area))
        {
            return;
        }
        var page = _last.TryGetValue(area, out var last) ? last : First(area);
        Show(page is LobbyViewModel && !InLobby ? Hub : page);
    }

    /// <summary>A page shown by the shell itself. (The classic Quick Play page shows only when the shell asks for it:
    /// shown by the classic launcher's own ways, it gives way to the hub, <see cref="Paged"/>.)</summary>
    private void Show(ViewModelBase page)
    {
        _quickPlayAsked = page is HomeViewModel;
        if (page is HomeViewModel)
        {
            _lobbiesFromServers = _main.Current is ServersViewModel;   // (Back goes where the player came from)
        }
        _main.Current = page;
    }

    /// <summary>L (-1) and R (1): the area before or after the one showing; at either end nothing changes. False:
    /// nothing changed.</summary>
    public bool StepArea(int by)
    {
        var at = AreaOrder.ToList().IndexOf(Area);
        var to = Math.Clamp(at + by, 0, AreaOrder.Count - 1);
        if (!_main.Navigation || at < 0 || to == at)
        {
            return false;
        }
        ShowArea(AreaOrder[to]);
        return true;
    }

    private ViewModelBase First(string area) => area switch
    {
        Garage => _main.Garage,
        Replays => _main.Replays,
        MoreArea => More,
        SettingsArea => _main.Settings,
        _ => InLobby ? _main.Lobby : Hub,
    };

    /// <summary>Quick Play: its lobby while the player is in one, else the servers to race on first (also from a custom
    /// lobby: joining a server there asks before the custom lobby is left).</summary>
    public void ShowQuickPlay() => Show(InQuickPlayItself ? _main.Lobby : Servers);

    /// <summary>The lobbies of the server set (a server's panel's "See its lobbies"), and RACE; the lobby while the
    /// player is in one.</summary>
    public void ShowLobbiesPage() => Show(InQuickPlayItself ? _main.Lobby : _main.Home);

    /// <summary>The lobbies' "Change server": the servers.</summary>
    [RelayCommand]
    private void ChangeServer() => Show(Servers);

    /// <summary>The gathering's "Practice while you wait": the Practice page; the player's seat is kept, and the lobby
    /// calls them back when it moves on (AppState.Recall).</summary>
    [RelayCommand]
    private void PracticeWhileWaiting() => _main.Current = _main.Practice;

    /// <summary>The pages Back left, the last on top: the mouse's forward button shows them again, as a browser does.
    /// Any other way to a page forgets them.</summary>
    private readonly Stack<ViewModelBase> _ahead = new();

    /// <summary>Back or Forward is changing the page (it does not forget the pages ahead).</summary>
    private bool _stepping;

    /// <summary>Back, the pad's B, Escape, Backspace and the mouse's back button: what is open over the page closes;
    /// else out of the page showing, one level up (a lobby is left only after its question); from an area's first
    /// page to Play. False: there is no way up (the hub, the first screen).</summary>
    public bool Back()
    {
        var before = _main.Current;
        _stepping = true;
        try
        {
            var done = Up();
            if (done && before is not (null or LobbyViewModel or DiscViewModel) && !ReferenceEquals(before, _main.Current))
            {
                _ahead.Push(before);
            }
            return done;
        }
        finally
        {
            _stepping = false;
        }
    }

    /// <summary>The mouse's forward button: the page Back left last, again (not over a question or a panel, and never a
    /// lobby). False: there is none.</summary>
    public bool Forward()
    {
        if (OverPage || !_main.Navigation || !_main.ExperimentalUi)
        {
            return false;
        }
        while (_ahead.TryPop(out var page))
        {
            if (page is LobbyViewModel || ReferenceEquals(page, _main.Current))
            {
                continue;
            }
            _stepping = true;
            try
            {
                Show(page);
            }
            finally
            {
                _stepping = false;
            }
            return true;
        }
        return false;
    }

    private bool Up()
    {
        if (CloseOverPage() || StepInPage())
        {
            return true;
        }
        switch (_main.Current)
        {
            case HubViewModel or DiscViewModel or null:
                return false;
            case GarageViewModel or LobbyViewModel { FullGarage: true } when _garagePage is { Building: true } garage:
                garage.CloseBuilder();   // the builder: back to the Garage
                return true;
            case LobbyViewModel { FullGarage: true } lobby:
                lobby.FullGarage = false;   // the whole Garage, opened from the machine stage: back to the stage
                return true;
            case LobbyViewModel { HostEditing: true } lobby:
                lobby.CancelRulesCommand.Execute(null);   // the host's rules: back to the room, nothing changed
                return true;
            case LobbyViewModel when _state.Session?.Lobby is null:
                _main.Lobby.LeaveCommand.Execute(null);   // the queue: left at once
                return true;
            case LobbyViewModel:
                AskLeave();
                return true;
            case CustomLobbiesViewModel { Creating: true } lobbies:
                lobbies.BackCommand.Execute(null);   // Create: back to the browser
                return true;
            case LanViewModel or PracticeViewModel or CustomLobbiesViewModel:
                // (a visit to the custom lobbies' browser, in no lobby, has no lobby page)
                _main.Current = InLobby && _state.Session is not { Custom: true, Lobby: null } ? _main.Lobby : Hub;
                return true;
            case HomeViewModel:
                _main.Current = _lobbiesFromServers ? Servers : Hub;
                return true;
            case ServersViewModel servers when servers.Adding:
                servers.CancelAddCommand.Execute(null);   // the address box: closed, the list stays
                return true;
            case ServersViewModel:
                _main.Current = Hub;
                return true;
            case StatsViewModel or ModsViewModel or AboutViewModel:
                _main.Current = More;
                return true;
            case ProfileViewModel:
                _main.Current = _beforeProfile is { } before && before is not ProfileViewModel ? before : Hub;
                return true;
            case ControllerViewModel { Capturing: true } pad:
                pad.CancelCaptureCommand.Execute(null);   // (a button being set: not set)
                return true;
            case ControllerViewModel:
                _main.Current = _main.Settings;   // the controller's page: back to Settings
                return true;
            default:
                ShowArea(Play);
                return true;
        }
    }

    /// <summary>A step back inside the page showing, before it is left: the first start's step before, a group of
    /// settings back to the groups. False: none.</summary>
    private bool StepInPage()
    {
        switch (_main.Current)
        {
            case DiscViewModel when _firstStart is { CanBack: true } first:
                first.Back();   // the first start: the step before
                return true;
            case SettingsViewModel:
                return _settingsPage?.CloseCategory() == true;   // a group of settings: back to the groups
            case GarageViewModel or LobbyViewModel { FullGarage: true }:
                return _garagePage?.StepBack() == true;   // the builder's colours: its parts; a machine's page: the machines
            case PracticeViewModel practice:
                return practice.StepBack();   // the set-up: back to the courses
            case CustomLobbiesViewModel { Creating: true }:
                return CreateRules?.StepBack() == true;   // a new lobby's rules: the step before
            case LobbyViewModel { HostEditing: true }:
                return HostRules?.StepBack() == true;
            default:
                return false;
        }
    }

    /// <summary>The top bar's Back pressed.</summary>
    [RelayCommand]
    private void GoBack() => Back();

    /// <summary>What is open over the page, closed: the top one. False: nothing was open.</summary>
    public bool CloseOverPage()
    {
        if (Keyboard is { } keyboard)
        {
            keyboard.Cancel();
        }
        else if (AskingConfirm)
        {
            AskingConfirm = false;
        }
        else if (CreateRules is { Picking: true } && _main.Current is CustomLobbiesViewModel { Creating: true })
        {
            CreateRules.ClosePickerCommand.Execute(null);   // (the course panel over the rules)
        }
        else if (HostRules is { Picking: true } && _main.Current is LobbyViewModel { HostEditing: true })
        {
            HostRules.ClosePickerCommand.Execute(null);
        }
        else if (_main.Current is CustomLobbiesViewModel { Asking: true } lobbies)
        {
            lobbies.CancelPasswordCommand.Execute(null);   // (the password's question)
        }
        else if (_main.Settings.AskingExperimental)
        {
            _main.Settings.KeepClassicCommand.Execute(null);
        }
        else if (_main.Graphics.Asking)
        {
            _main.Graphics.CancelCommand.Execute(null);
        }
        else if (_main.Control.Asking)
        {
            _main.Control.StayCommand.Execute(null);
        }
        else if (SheetOpen)
        {
            if (!GaragePageViewModel.StepBack(Sheet))
            {
                Sheet = null;   // (a panel's second step goes back to its first)
            }
        }
        else if (_main.Notices.ListOpen)
        {
            _main.Notices.ListOpen = false;
        }
        else if (_main.Chat.Overlay)
        {
            _main.Chat.Open = false;
        }
        else
        {
            return false;
        }
        return true;
    }

    /// <summary>Before a lobby or a race is left (as the classic launcher leaves it): staying is the green answer.</summary>
    private void AskLeave()
    {
        var racing = _state.Session is { Racing: true };
        Ask(racing ? "Leave the race?" : "Leave the lobby?",
            racing ? "Your machine stops, and the race goes on without you." : "You can join another race right away.",
            "Leave", () => _main.Lobby.LeaveCommand.Execute(null), keep: "Stay", holds: () => _main.Current is LobbyViewModel);
    }

    /// <summary>The panel that prepares the game's files from the disc image (the hub's "Prepare the game",
    /// Settings' "Game files"), over the page showing; it closes once the game is ready.</summary>
    public void OpenPrepare()
    {
        PrepareSheetViewModel? sheet = null;
        sheet = new PrepareSheetViewModel(_state, _main.Current ?? Hub, () =>
        {
            if (ReferenceEquals(Sheet, sheet))
            {
                Sheet = null;
            }
        });
        Sheet = sheet;
    }

    /// <summary>A panel's Close.</summary>
    [RelayCommand]
    private void CloseSheet() => Sheet = null;

    /// <summary>The lobby's panel: its rules, its racers and watchers, and what else a player can do in it.</summary>
    [RelayCommand]
    private void OpenLobbySheet() => Sheet = new LobbySheetViewModel(_main, this);

    /// <summary>The race's panel: every racer, or every result.</summary>
    [RelayCommand]
    private void OpenRacersSheet() => Sheet = new RacersSheetViewModel(_main, this);

    /// <summary>The lobby's panel's "Leave lobby": the panel closes, and the question is asked.</summary>
    [RelayCommand]
    private void LeaveFromSheet()
    {
        Sheet = null;
        AskLeave();
    }

    /// <summary>The lobby's panel's "Start F-Zero GX instead": the panel closes, and the classic launcher's question
    /// before leaving a lobby for the game is asked.</summary>
    [RelayCommand]
    private void PlayGameFromSheet()
    {
        Sheet = null;
        _main.Control.PlayGameCommand.Execute(null);
    }

    /// <summary>The Replays page (the result's "Replays").</summary>
    [RelayCommand]
    private void ShowReplays() => _main.Current = _main.Replays;

    /// <summary>The lobby's pill pressed: its page.</summary>
    [RelayCommand]
    private void ShowLobby() => _main.Current = _main.Lobby;

    /// <summary>The update's pill pressed: where the update is (Settings, Launcher).</summary>
    [RelayCommand]
    private void ShowUpdate()
    {
        SettingsPage.ShowCategory("Launcher");   // (the redesigned Settings: its Launcher group)
        _main.ShowSettings(SettingsTab.Launcher);
    }

    /// <summary>The pad's Y: the page's own second action (the hub: the lobbies; the Garage: Sort &amp; filter).
    /// False: the page has none.</summary>
    public bool Context()
    {
        if (SheetOpen && !AskingConfirm && !_main.Control.Asking)
        {
            Sheet = null;   // Y again closes the panel it opened
            return true;
        }
        if (OverPage)
        {
            return false;
        }
        switch (_main.Current)
        {
            case GarageViewModel or LobbyViewModel { FullGarage: true } when Building(_main.Current) || GaragePage.Detail:
                return false;
            case GarageViewModel or LobbyViewModel { FullGarage: true }:
                GaragePage.OpenSort();
                return true;
            case LobbyViewModel { Phase: LobbyPhase.Race or LobbyPhase.Results }:
                OpenRacersSheet();
                return true;
            case LobbyViewModel { Phase: LobbyPhase.Room, HostEditing: false }:
                OpenRulesSheet();
                return true;
            case LobbyViewModel { Phase: LobbyPhase.Gather or LobbyPhase.Vote or LobbyPhase.Starting } or LobbyViewModel { Phase: LobbyPhase.Machine, FullGarage: false }:
                OpenLobbySheet();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The page changed (or the interface did): the pages that are the redesigned interface's own never show
    /// in the classic one, and the classic Quick Play page, shown by the classic launcher's own ways (a lobby left,
    /// the first screen passed), gives way to the hub.</summary>
    private void Paged()
    {
        var page = _main.Current;
        JoinCheck.Hear(this, _main, _state);   // (the redesigned interface says why a race cannot be joined, and shows the way)
        if (!_main.ExperimentalUi)
        {
            if (page is HubViewModel || ReferenceEquals(page, More))
            {
                _main.Current = _main.Home;
            }
            return;
        }
        if (page is HomeViewModel && !_quickPlayAsked && !InLobby)
        {
            _main.Current = Hub;
            return;
        }
        _quickPlayAsked = false;
        if (!_stepping)
        {
            _ahead.Clear();   // (a page shown another way than Back or Forward)
        }
        if (AreaOf(page) is { Length: > 0 } area)
        {
            _last[area] = page;
        }
        if (Sheet is MotdSheetViewModel ? page is not HubViewModel
            : Sheet is IGarageSheet ? !GarageShows(page)
            : Sheet is IOwnedSheet owned ? !ReferenceEquals(owned.Owner, page)
            : page is not LobbyViewModel)
        {
            Sheet = null;   // (a panel is its page's)
        }
        StillAsked();
        if (page is ProfileViewModel profile)
        {
            profile.Shown();   // (its figures and races read again)
        }
        if (_servers is not null || page is ServersViewModel)
        {
            Servers.Watching = page is ServersViewModel;   // (its servers are asked only while it shows)
        }
        Refresh();
    }

    /// <summary>The lobby went to another phase: a panel of the phase before is closed (the race's racers are not
    /// the vote's lobby).</summary>
    private void Phased()
    {
        if (Sheet is RacersSheetViewModel && _main.Lobby.Phase is not (LobbyPhase.Race or LobbyPhase.Results))
        {
            Sheet = null;
        }
        else if (Sheet is LobbySheetViewModel && _main.Lobby.Phase is LobbyPhase.Finding or LobbyPhase.Room)
        {
            Sheet = null;
        }
        else if (Sheet is RulesSheetViewModel && (_main.Lobby.Phase != LobbyPhase.Room || _main.Lobby.HostEditing))
        {
            Sheet = null;
        }
        else if (Sheet is IGarageSheet && !GarageShows(_main.Current))
        {
            Sheet = null;   // (the lobby's whole Garage closed)
        }
        StillAsked();
        Refresh();
    }

    private void Refresh()
    {
        var page = _main.Current;
        var area = AreaOf(page);
        Area = area;
        (BackShown, BackText, BackLeaves) = page switch
        {
            LobbyViewModel { FullGarage: true } => (true, "Back", false),
            GarageViewModel when Building(page) || _garagePage is { Detail: true } => (true, "Back", false),
            LobbyViewModel when _state.Session?.Lobby is null => (true, "Leave the queue", true),
            LobbyViewModel when _state.Session is { Racing: true } => (true, "Leave the race", true),
            LobbyViewModel => (true, "Leave", true),
            SettingsViewModel when _settingsPage is { IsOpen: true } => (true, "Back", false),
            ServersViewModel or HomeViewModel or LanViewModel or PracticeViewModel or CustomLobbiesViewModel or StatsViewModel
                or ModsViewModel or ControllerViewModel or AboutViewModel or ProfileViewModel => (true, "Back", false),
            _ => (false, "Back", false),
        };
        LobbyPillShown = InLobby && page is not (LobbyViewModel or DiscViewModel);
        Backdrop = page is LobbyViewModel lobby ? lobby.BackdropCourse : -1;
        LobbyPillText = LobbyWords(_main.Control.Word, _main.Control.Figure);
        Clear = page is HubViewModel;
        PageFits = page is ServersViewModel or HomeViewModel or CustomLobbiesViewModel or PracticeViewModel or LobbyViewModel or GarageViewModel
            or ReplaysViewModel or StatsViewModel or ModsViewModel or AboutViewModel or ProfileViewModel or SettingsViewModel or ControllerViewModel;
        // (the same hints are left as they are: the bar is not made again at every tick of a lobby's clock)
        if (HintsFor(page) is var hints && !hints.SequenceEqual(Hints))
        {
            Hints = hints;
        }
        OnPropertyChanged(nameof(OverPage));
        ShowDevLines();
    }

    /// <summary>A new custom lobby's rules as an option page (Create); null while the list shows.</summary>
    [ObservableProperty]
    public partial RulesPageViewModel? CreateRules { get; set; }

    /// <summary>The host's own lobby's rules as an option page (Edit rules); null while the room shows.</summary>
    [ObservableProperty]
    public partial RulesPageViewModel? HostRules { get; set; }

    partial void OnCreateRulesChanged(RulesPageViewModel? value) => HearPicking(value);

    partial void OnHostRulesChanged(RulesPageViewModel? value) => HearPicking(value);

    /// <summary>The course's panel over a rules page has hints of its own.</summary>
    private void HearPicking(RulesPageViewModel? rules)
    {
        if (rules is not null)
        {
            rules.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RulesPageViewModel.Picking) or nameof(RulesPageViewModel.Step))
                {
                    Refresh();
                }
            };
        }
    }

    /// <summary>A racer's tile pressed by the host: asked first; "Remove" takes the racer out of the lobby (the
    /// server tells them why).</summary>
    [RelayCommand]
    private void AskKick(SeatRow? row)
    {
        if (row is { Kickable: true } && _main.Lobby.HostCanEdit)
        {
            Ask($"Remove {row.Name}?", "They leave the lobby and are told the host removed them.", "Remove",
                () => _main.Lobby.KickCommand.Execute(row), keep: "Keep them",
                holds: () => _main.Current is LobbyViewModel && _main.Lobby is { Phase: LobbyPhase.Room, HostCanEdit: true });
        }
    }

    /// <summary>The keyboard on the screen, while it shows (a text box the pad pressed, a row that types).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardOpen))]
    public partial KeyboardViewModel? Keyboard { get; private set; }

    public bool KeyboardOpen => Keyboard is not null;

    partial void OnKeyboardChanged(KeyboardViewModel? value) => Refresh();

    /// <summary>The keyboard on the screen for a text: <paramref name="done"/> is given what was typed, unless it is
    /// cancelled.</summary>
    public void OpenKeyboard(string title, string text, int most, bool masked, Action<string> done) =>
        Keyboard = new KeyboardViewModel(title, text, most, masked, done, () => Keyboard = null);

    /// <summary>The question over the page before what cannot be taken back (a lobby or a race left, a racer removed, a
    /// machine, a replay or a mod deleted, the game ended): keeping things as they are is the green answer.</summary>
    [ObservableProperty]
    public partial bool AskingConfirm { get; set; }

    partial void OnAskingConfirmChanged(bool value) => Refresh();

    [ObservableProperty]
    public partial string ConfirmTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmLine { get; set; } = "";

    /// <summary>The red answer's word ("Delete", "Remove", "Leave").</summary>
    [ObservableProperty]
    public partial string ConfirmWord { get; set; } = "";

    /// <summary>The green answer's word ("Keep it", "Stay").</summary>
    [ObservableProperty]
    public partial string KeepWord { get; set; } = "";

    private Action? _confirm;
    private Func<bool>? _holds;

    /// <summary>Asks before <paramref name="act"/>. The question goes once <paramref name="holds"/> is false (another
    /// page, another phase of the lobby, the game ended); by default once another page shows.</summary>
    public void Ask(string title, string line, string word, Action act, string keep = "Keep it", Func<bool>? holds = null)
    {
        var page = _main.Current;
        (ConfirmTitle, ConfirmLine, ConfirmWord, KeepWord, _confirm) = (title, line, word, keep, act);
        _holds = holds ?? (() => ReferenceEquals(_main.Current, page));
        AskingConfirm = true;
    }

    /// <summary>A question whose matter has gone goes with it.</summary>
    private void StillAsked() => AskingConfirm = AskingConfirm && _holds?.Invoke() != false;

    /// <summary>The green answer: nothing is done.</summary>
    [RelayCommand]
    private void KeepIt() => AskingConfirm = false;

    /// <summary>The red answer: done.</summary>
    [RelayCommand]
    private void Confirm()
    {
        AskingConfirm = false;
        var act = _confirm;
        _confirm = null;
        act?.Invoke();
    }

    /// <summary>The Garage's "Remove": asked first (the Garage closed drops the question).</summary>
    public void AskRemoveMachine(string name) =>
        Ask($"Remove {name}?", "It leaves your garage. If you race it, your stock machine races again.", "Remove",
            () => _garagePage?.Remove(), holds: () => GarageShows(_main.Current));

    /// <summary>The room's "All rules" (and Y): every rule of the lobby, the ones that differ from Quick Play's marked.</summary>
    [RelayCommand]
    private void OpenRulesSheet()
    {
        if (_state.Session is { Lobby: { } lobby } session)
        {
            Sheet = new RulesSheetViewModel(_main.Lobby.LobbyName, OptionGroup.Of(CustomRules.AllRules(lobby.Rules, session.QuickPlayRules), "Rules"));
        }
    }

    /// <summary>A game of this launcher runs: the hub's machine card says so and offers to end it.</summary>
    [ObservableProperty]
    public partial bool GameRunning { get; set; }

    /// <summary>Whether a game runs, as the state says it now; a question about a game that has ended goes.</summary>
    private void Raced()
    {
        GameRunning = _state.Racing;
        StillAsked();
    }

    /// <summary>The hub's "End game": asked first; then the game is asked to stop, and ended.</summary>
    [RelayCommand]
    private void AskEndGame()
    {
        if (GameRunning)
        {
            Ask("End the game?", "F-Zero GX closes. Anything not saved in the game is lost.", "End game", _state.EndGame,
                keep: "Keep it open", holds: () => GameRunning);
        }
    }

    /// <summary>The course whose picture stands behind the page in place of the flag (the lobby's course drawn, its
    /// start, its race); -1: the flag.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackdrop))]
    public partial int Backdrop { get; set; } = -1;

    public bool HasBackdrop => Backdrop >= 0;

    /// <summary>The lobby calls the player back from their practice: the band under the top bar, with the seconds
    /// left and "Back now".</summary>
    [ObservableProperty]
    public partial bool RecallShown { get; set; }

    [ObservableProperty]
    public partial string RecallText { get; set; } = "";

    /// <summary>The seconds left of the recall (0: none runs): the shell's view sounds the last three.</summary>
    [ObservableProperty]
    public partial int RecallSeconds { get; set; }

    /// <summary>A recall began: the window is to be noticed (the shell's view flashes it).</summary>
    public event Action? RecallBegan;

    /// <summary>"Back now": the practice ends at once.</summary>
    [RelayCommand]
    private void RecallNow() => _state.Recall.Now();

    private void ShowRecall()
    {
        var at = _state.Recall.At;
        var shown = at is not null;
        if (shown && !RecallShown)
        {
            RecallBegan?.Invoke();
        }
        RecallShown = shown;
        if (at is { } end)
        {
            RecallSeconds = (int)Math.Ceiling(Math.Max(0, (end - DateTimeOffset.UtcNow).TotalSeconds));
            RecallText = LobbyRecall.Line(RecallSeconds);
            if (!_recallClock.IsEnabled)
            {
                _recallClock.Start();
            }
        }
        else
        {
            _recallClock.Stop();
            RecallSeconds = 0;
        }
    }

    public void Dispose()
    {
        _recallClock.Stop();
        _servers?.Dispose();
        _about?.Dispose();
        _profile?.Dispose();
        _settingsPage?.Dispose();
        _controllerPage?.Dispose();
        _firstStart?.Dispose();
    }

    /// <summary>The lobby's step and its figure as the pill says them: "Gathering · 4/8".</summary>
    public static string LobbyWords(string word, string figure)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(figure);
        var step = word.Length == 0 ? "Lobby" : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        return figure.Length == 0 ? step : $"{step} · {figure}";
    }

    private IReadOnlyList<Hint> HintsFor(ViewModelBase? page)
    {
        if (KeyboardOpen)
        {
            return [A("Type"), Start("Done"), B("Cancel")];
        }
        if (AskingConfirm || _main.Settings.AskingExperimental || _main.Graphics.Asking || _main.Control.Asking)
        {
            return [A("Select"), B("Cancel")];
        }
        if (Building(page) && !SheetOpen)
        {
            return [Dpad("Change"), A("Select"), Start(_garagePage is { BuilderOnParts: true } ? "Next" : "Save"), B("Back")];
        }
        if (!SheetOpen && StepHints(page) is { } stepped)
        {
            return WithPadKeys(page, stepped);
        }
        if (SheetOpen || _main.Notices.ListOpen || _main.Chat.Overlay)
        {
            return [A("Select"), B("Close")];
        }
        return WithPadKeys(page, PageHints(page));
    }

    /// <summary>After a page's own hints: X, the chat (the pad's button or the key), and with the pad in use its Y, the
    /// main menu (not on the main menu itself); none on the first start.</summary>
    private IReadOnlyList<Hint> WithPadKeys(ViewModelBase? page, IReadOnlyList<Hint> hints) =>
        page is null or DiscViewModel || !_main.Navigation ? hints
        : !PadInUse || page is HubViewModel ? [.. hints, Of("X", "X", "Chat") with { Everywhere = true }]
        : [.. hints, Of("X", "X", "Chat") with { Everywhere = true }, Of("Y", "", "Main menu") with { Everywhere = true }];

    /// <summary>X, the pad's or the key (the owner, 2026-11-15: the chat's key did nothing on the keyboard): the chat
    /// opened or closed from anywhere past the first start; not while a question or the keyboard on the screen is
    /// open. False: nothing changed.</summary>
    public bool ToggleChat()
    {
        if (!_main.Navigation || AskingConfirm || KeyboardOpen)
        {
            return false;
        }
        _main.Chat.ToggleCommand.Execute(null);
        return true;
    }

    private IReadOnlyList<Hint> PageHints(ViewModelBase? page)
    {
        return page switch
        {
            HubViewModel => [A("Select"), Lr("Sections")],
            ServersViewModel { Adding: true } => [A("Select"), B("Cancel")],
            CustomLobbiesViewModel { Creating: true } when CreateRules is { Picking: true } => [A("Pick"), B("Done")],
            LobbyViewModel { HostEditing: true } when HostRules is { Picking: true } => [A("Pick"), B("Done")],
            ServersViewModel => [A("Join"), B("Back")],
            HomeViewModel => [A("Join"), Start("Race"), B("Back")],
            LobbyViewModel lobby => LobbyHints(lobby),
            CustomLobbiesViewModel { Asking: true } => [A("Select"), B("Cancel")],
            CustomLobbiesViewModel { Creating: true } => [Dpad("Change"), A("Select"), Start("Create"), B("Back")],
            CustomLobbiesViewModel { InQuickPlay: true } => [A("Select"), B("Back")],
            CustomLobbiesViewModel => [A("Join"), Start("Create lobby"), B("Back")],
            GarageViewModel when PadInUse => [A("Select"), Tab("Shelves"), B("Back")],   // (Sort & filter: its button)
            GarageViewModel => [A("Select"), Y("Sort & filter"), Tab("Shelves"), B("Back")],
            ReplaysViewModel => [A("Open"), Tab("Filter"), Lr("Sections"), B("Back")],
            StatsViewModel => [A("Select"), Tab("Best times"), B("Back")],
            ModsViewModel => [A("Options"), B("Back")],
            AboutViewModel => [Dpad("Change"), A("Select"), B("Back")],
            SettingsViewModel when _settingsPage is not { IsOpen: true } => [A("Open"), B("Back")],
            SettingsViewModel or ControllerViewModel => [Dpad("Change"), A("Select"), B("Back")],
            MenuViewModel => [A("Open"), B("Back")],
            DiscViewModel when _firstStart is { CanBack: true } => [A("Select"), Start("Next"), B("Back")],
            DiscViewModel => [A("Select"), Start("Next")],
            null => [A("Select")],
            _ => [A("Select"), B("Back")],
        };
    }

    /// <summary>The hints of a page gone through in steps, by its step: the Garage's machines and a machine's page,
    /// Practice's courses and set-up, a lobby's rules (NEXT on the lobby's step). Null: the page has no steps.</summary>
    private IReadOnlyList<Hint>? StepHints(ViewModelBase? page) => page switch
    {
        GarageViewModel when _garagePage is { Detail: true } => [Dpad("Change"), A("Select"), Start("Use"), B("Back")],
        PracticeViewModel { SettingUp: true } => [Dpad("Change"), A("Select"), Start("Start practice"), B("Back")],
        PracticeViewModel => [A("Pick"), Tab("Cups"), B("Back")],
        CustomLobbiesViewModel { Creating: true, Asking: false } when CreateRules is { Picking: false, Last: false } =>
            [Dpad("Change"), A("Select"), Start("Next"), B("Back")],
        LobbyViewModel { HostEditing: true } when HostRules is { Picking: false, Last: false } =>
            [Dpad("Change"), A("Select"), Start("Next"), B("Cancel")],
        _ => null,
    };

    /// <summary>The lobby's hints, a phase its own (the second action: the lobby's panel, the room's rules, or the
    /// race's racers; Y on the keyboard, R1 on the pad, whose Y is the main menu).</summary>
    private IReadOnlyList<Hint> LobbyHints(LobbyViewModel lobby) => lobby.Phase switch
    {
        LobbyPhase.Finding => [B("Leave the queue")],
        LobbyPhase.Gather => [A("Select"), Second("Lobby"), B("Leave")],
        LobbyPhase.Room when lobby.HostEditing => [Dpad("Change"), A("Select"), Start("Save"), B("Cancel")],
        LobbyPhase.Room when lobby.HostCanEdit => [A("Remove racer"), Start("Start race"), Second("Rules"), B("Leave")],
        LobbyPhase.Room => [A("Select"), Start("Vote to start"), Second("Rules"), B("Leave")],
        LobbyPhase.Vote => [A("Vote"), Second("Lobby"), B("Leave")],
        LobbyPhase.Machine when lobby.FullGarage && PadInUse => [A("Use"), B("Back")],
        LobbyPhase.Machine when lobby.FullGarage => [A("Use"), Y("Sort & filter"), B("Back")],
        LobbyPhase.Machine => [A("Select"), Start("Ready"), Second("Lobby"), B("Leave")],
        LobbyPhase.Starting => [Second("Lobby"), B("Leave")],
        LobbyPhase.Race => [A("Select"), Second("All racers"), B("Leave")],
        LobbyPhase.Results => [A("Select"), Second("All results"), B("Leave")],
        _ => [A("Select"), B("Leave")],
    };

    /// <summary>The page's second action: Y on the keyboard, R1 (the GameCube's Z) on the pad.</summary>
    private Hint Second(string label) => Of("Z", "Y", label);

    /// <summary>The pad's look in the redesigned interface: the family of its button icons (the one chosen, else the
    /// pad's own) and whether its right face button confirms (Settings: "Button icons", "Confirm button").</summary>
    public (ButtonIcons Family, bool ConfirmRight) PadLook() => PadLooks.Of(_state);

    private Hint A(string label) => Of("A", "Enter", label);

    private Hint B(string label) => Of("B", "Esc", label);


    private Hint Y(string label) => Of("Y", "Y", label);

    private Hint Start(string label) => Of("Start", "S", label);

    private Hint Lr(string label) => Of("L  R", "Q  E", label);

    /// <summary>The page's sub-tabs: R1 on the pad (the GameCube's Z), Ctrl and the arrows on the keyboard.</summary>
    private Hint Tab(string label) => Of("Z", "Ctrl ←→", label);

    private Hint Dpad(string label) => Of("‹  ›", "←  →", label);

    /// <summary>A hint in the controller's button while the pad is in use (a face button round), else in its key.</summary>
    private Hint Of(string button, string key, string label)
    {
        if (!PadInUse)
        {
            return new Hint(key, label, Round: false);
        }
        var (family, confirmRight) = PadLook();
        // (a face button round; the GameCube's in its own colours and sizes)
        var cube = family == ButtonIcons.GameCube ? (button is "L  R" ? "LR" : button) : "";
        return new Hint(PadLooks.Glyph(button, family, confirmRight), label, button is "A" or "B" or "X" or "Y", cube);
    }
}
