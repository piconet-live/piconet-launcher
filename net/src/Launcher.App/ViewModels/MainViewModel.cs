using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>One of a tab's pages, as a heading above the page (the one showing in full, the others faint). The
/// Settings tab's pages name their <see cref="SettingsTab"/>: three of them are one view model's.</summary>
public sealed record SectionLink(string Label, ViewModelBase Page, bool Current, SettingsTab? Tab = null);

/// <summary>
/// The window (direction C): the left column that is on every page (the player's machine and name,
/// the play control, the connection), the four tabs, and the page showing. A tab can hold more than one page: Play is
/// Quick Play, the lobby while the player is in one, and the local network; Settings is Game, Online, Controller and
/// Launcher, and opens on the one last chosen (<see cref="LauncherSettings.SettingsTab"/>).
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly ServerStatusFeed _feed;
    private QuickPlaySession? _watched;
    private LobbyScreen _lobbyScreen;
    private ViewModelBase _play;
    private ViewModelBase _replays;

    /// <param name="statusHandler">How the server's status is asked (tests and pictures answer it themselves).</param>
    /// <param name="noticeTime">The clock a notice's banner is timed by (tests: their own).</param>
    /// <param name="updates">The launcher's own updates (the application's); null: none (tests).</param>
    public MainViewModel(AppState state, HttpMessageHandler? statusHandler = null, TimeProvider? noticeTime = null, Updates? updates = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        // nobody is asked while the first screen shows (ServerChoice.AskedUnprompted): the feed and the connection
        // dot run from the start, with nobody to ask until the disc is accepted
        _feed = new ServerStatusFeed(() => ServerChoice.AskedUnprompted(state.Settings, Navigation), statusHandler ?? state.ServiceAnswers());
        Garage = new GarageViewModel(state);
        Lobby = new LobbyViewModel(state, () => Current = Home!, Garage);
        Home = new HomeViewModel(state, _feed, Lobby, () => Current = Lobby, () => Current = Replays!, () => ShowSettings(SettingsTab.Launcher));
        Lan = new LanViewModel(state, () => Current = Lobby);
        Lobbies = CustomLobbies(state);
        Practice = PracticePage(state);
        Update = new UpdateViewModel(state, updates);
        Settings = new SettingsViewModel(state) { Update = Update };
        Controller = new ControllerViewModel(state);
        Replays = new ReplaysViewModel(state);
        _replays = Replays;
        Disc = new DiscViewModel(state, () => Current = Home);
        Account = new AccountViewModel(state, () => Current = Garage, () => Navigation);
        Control = new PlayControlViewModel(state, _feed, Lobby, () => Current = Lobby, ShowSettings);
        Control.ShowFirstScreen = () => Current = Disc;
        Notices = new NoticesViewModel(state, noticeTime);
        _play = Home;
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Tab))
            {
                RememberSettingsTab();
                RefreshSections();
            }
        };
        // the first screen until the player's disc is verified and the game's files are prepared from it (in dev mode
        // not at all: the stand-in's pages need neither)
        Current = Disc.Passed || state.StandIn ? Home : Disc;
        state.Changed += () => OnUi(Refresh);
        Garage.PropertyChanged += (_, _) => RefreshMachine();
        Practice.Courses.Changed += ShowVenue;
        Lobby.PropertyChanged += (_, _) => RefreshMachine();
        Refresh();
        _feed.Watching = true;
        _feed.Start();
    }

    /// <summary>Play's Practice page, on the Garage's machine.</summary>
    private PracticeViewModel PracticePage(AppState state) =>
        new(() => _state.Settings.Racer.Raced with { Machine = Garage.ChosenIndex }, () => Current = Garage,
            [.. state.PracticePackList.Select(p => p.Name)], state.StartPractice, state.Settings.Practice);

    /// <summary>The Custom lobbies page, and the lobby page's ways to it (leaving a custom lobby, the host's presets).</summary>
    private CustomLobbiesViewModel CustomLobbies(AppState state)
    {
        IReadOnlyList<string> Packs() => [.. state.PracticePackList.Select(p => p.Name)];
        var lobbies = new CustomLobbiesViewModel(state, () => Current = Lobby, Packs);
        Lobby.ToBrowser = () => Current = lobbies;
        Lobby.PackNames = Packs;
        return lobbies;
    }

    /// <summary>The launcher's update: the left column's line and button, and the Settings page's (<see cref="Updates"/>).</summary>
    public UpdateViewModel Update { get; }

    public string Title => string.Join(" · ", new[] { Branding.AppName, StackStamp.Current.Mark, _state.StandInName }.OfType<string>());

    /// <summary>The name inside the launcher, where the disc gives no lettering for it.</summary>
    public string Brand { get; } = Branding.DisplayName;

    public HomeViewModel Home { get; }

    public GarageViewModel Garage { get; }

    public LobbyViewModel Lobby { get; }

    public LanViewModel Lan { get; }

    /// <summary>Play's Custom lobbies page: the browser and Create.</summary>
    public CustomLobbiesViewModel Lobbies { get; }

    /// <summary>Play's Practice page: a solo run's set-up.</summary>
    public PracticeViewModel Practice { get; }

    public SettingsViewModel Settings { get; }

    public ControllerViewModel Controller { get; }

    public ReplaysViewModel Replays { get; }

    /// <summary>The Replays tab's second page: the player's figures from their replays (made when first asked for).</summary>
    public StatsViewModel Stats => _stats ??= new StatsViewModel(_state);

    private StatsViewModel? _stats;

    public DiscViewModel Disc { get; }

    /// <summary>The left column's name, machine and connection.</summary>
    public AccountViewModel Account { get; }

    /// <summary>The left column's play control: RACE or what the lobby is doing, and "Play F-Zero GX".</summary>
    public PlayControlViewModel Control { get; }

    /// <summary>The server's notices: the banner, the bell and its list.</summary>
    public NoticesViewModel Notices { get; }

    /// <summary>The interface scale the window is drawn at, 1 for 100% (<see cref="LauncherSettings.InterfaceScale"/>).</summary>
    [ObservableProperty]
    public partial double Scale { get; set; } = 1;

    /// <summary>Ctrl+plus (1) and Ctrl+minus (-1): the interface scale a step larger or smaller, at once and kept,
    /// never past its ends; Ctrl+0 (0): 100%. The way back from a scale the player cannot use.</summary>
    public void StepScale(int steps)
    {
        var to = steps == 0 ? LauncherSettings.DefaultInterfaceScale : _state.Settings.InterfaceScaleStepped(steps);
        if (to != _state.Settings.InterfaceScale)
        {
            _state.Update(s => s with { InterfaceScale = to });
        }
    }

    /// <summary>The settings as they are now, for the launcher's own sounds (their volume, muted or not).</summary>
    public LauncherSettings SoundSettings => _state.Settings;

    /// <summary>The left column and the tabs show once the disc is set (before the first page is chosen, while
    /// the window is being made, they do not).</summary>
    public bool Navigation => Current is not (null or DiscViewModel);

    /// <summary>The tab showing, by name, for the tabs to mark.</summary>
    public string Tab => Current switch
    {
        HomeViewModel or LobbyViewModel or LanViewModel or PracticeViewModel or CustomLobbiesViewModel => "play",
        GarageViewModel => "garage",
        ReplaysViewModel or StatsViewModel => "replays",
        SettingsViewModel or ControllerViewModel => "settings",
        _ => "",
    };

    /// <summary>The pages of the tab showing, as headings, when the tab's pages have none of their own (Play,
    /// Settings).</summary>
    /// <summary>The place's title over its page, in capitals (PLAY, GARAGE, REPLAYS, SETTINGS).</summary>
    public string TabTitle => Tab.ToUpperInvariant();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSections))]
    public partial IReadOnlyList<SectionLink> Sections { get; set; } = [];

    public bool HasSections => Sections.Count > 0;

    /// <summary>Beside the headings, on Quick Play: the server's rules.</summary>
    public bool OnQuickPlay => Current is HomeViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Navigation), nameof(Tab), nameof(TabTitle), nameof(OnQuickPlay))]
    public partial ViewModelBase Current { get; set; }

    /// <summary>The machine the left column shows, turning: the one selected in the Garage, which is the player's
    /// machine (in a lobby that does not allow it, the allowed one selected there); and once the server has spoken
    /// for a race (the machine stage closed, the player's race on), the machine and colour it races the player with.</summary>
    [ObservableProperty]
    public partial int ShownMachine { get; set; } = -1;

    [ObservableProperty]
    public partial int? ShownStockColor { get; set; }

    [ObservableProperty]
    public partial string ShownPaint { get; set; } = "#E8432E";

    /// <summary>The custom machine the left column shows in place of <see cref="ShownMachine"/>: the one the player
    /// races (or the server races them with); null: the stock machine.</summary>
    [ObservableProperty]
    public partial CustomBuild? ShownCustom { get; set; }

    /// <summary>That custom machine's colours by part (body, cockpit, booster), each null for the part's own.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Rgb?> ShownPartColours { get; set; } = [null, null, null];

    /// <summary>The 3D machine rests while the game runs: the graphics card is the game's then.</summary>
    [ObservableProperty]
    public partial bool MachineShown { get; set; } = true;

    /// <summary>A heading pressed: its page.</summary>
    [RelayCommand]
    private void Show(SectionLink link)
    {
        if (link.Tab is { } tab)
        {
            ShowSettings(tab);
        }
        else
        {
            Current = link.Page;
        }
    }

    /// <summary>The Settings tab on one of its pages: where a player sent to Settings finds what to set (the disc:
    /// Launcher; a server: Online).</summary>
    public void ShowSettings(SettingsTab tab)
    {
        if (tab == SettingsTab.Controller)
        {
            Current = Controller;
            return;
        }
        Settings.Tab = tab;
        Current = Settings;
    }

    /// <summary>The Settings page showing, as the settings file keeps it: null while Settings does not show.</summary>
    public SettingsTab? ShownSettingsTab => Current switch
    {
        ControllerViewModel => SettingsTab.Controller,
        SettingsViewModel settings => settings.Tab,
        _ => null,
    };

    /// <summary>The page showing is kept as the one the Settings tab opens on.</summary>
    private void RememberSettingsTab()
    {
        if (ShownSettingsTab is { } tab && tab != _state.Settings.SettingsTab)
        {
            _state.Update(s => s with { SettingsTab = tab });
        }
    }

    /// <summary>A tab pressed: its page, the one last shown there.</summary>
    [RelayCommand]
    private void ShowTab(string tab)
    {
        if (LoadTrace.On)
        {
            LoadTrace.Note($"tab {tab} asked");
        }
        Current = TabPage(tab);
    }

    private ViewModelBase TabPage(string tab) => tab switch
    {
        "play" => _play is LobbyViewModel && !_state.InQuickPlay ? Home : _play,
        "garage" => Garage,
        "replays" => _replays,
        "settings" => _state.Settings.SettingsTab == SettingsTab.Controller ? Controller : SettingsPage(),
        _ => Current,
    };

    /// <summary>The Settings view model on the page the settings keep (Game, Online or Launcher).</summary>
    private SettingsViewModel SettingsPage()
    {
        Settings.Tab = _state.Settings.SettingsTab;
        return Settings;
    }

    partial void OnCurrentChanged(ViewModelBase oldValue, ViewModelBase newValue)
    {
        // the browser asks for the custom lobbies' list while it shows (and ends a visit in no lobby when left)
        if (oldValue is CustomLobbiesViewModel && newValue is not CustomLobbiesViewModel)
        {
            Lobbies.Hidden();
        }
        else if (newValue is CustomLobbiesViewModel && oldValue is not CustomLobbiesViewModel)
        {
            Lobbies.Shown();
        }
        // the Controller page holds the game's input devices only while it shows
        if (oldValue is ControllerViewModel && newValue is not ControllerViewModel)
        {
            Controller.Hidden();
        }
        else if (newValue is ControllerViewModel && oldValue is not ControllerViewModel)
        {
            Controller.Shown();
        }
        if (oldValue is null or DiscViewModel && newValue is not DiscViewModel)
        {
            // the launcher's pages show (at the start with a verified disc, or the first screen just passed): the
            // server that is set is asked from now on, and at once
            Account.Shown();
            if (oldValue is DiscViewModel)
            {
                _ = _feed.AskAsync();
            }
        }
    }

    partial void OnCurrentChanged(ViewModelBase value)
    {
        if (value is HomeViewModel or LobbyViewModel or LanViewModel or PracticeViewModel or CustomLobbiesViewModel)
        {
            _play = value;
        }
        if (value is PracticeViewModel practice)
        {
            practice.Shown();   // the machine as the Garage has it now
        }
        RememberSettingsTab();
        if (value is ReplaysViewModel or StatsViewModel)
        {
            _replays = value;
        }
        if (value is ReplaysViewModel replays)
        {
            replays.Refresh();   // the folder as it is now: a race just recorded, a file copied in
        }
        if (value is StatsViewModel stats)
        {
            stats.Refresh();     // (the replay index reads only what is new)
        }
        RefreshSections();
        RefreshMachine();
    }

    // The Local network page (Lan) keeps its view model but has no tab: it stays hidden until there is a LAN server
    // people can download and run.
    private void RefreshSections()
    {
        SectionLink Link(string label, ViewModelBase page) => new(label, page, Current == page);
        SectionLink Page(string label, SettingsTab tab) =>
            new(label, tab == SettingsTab.Controller ? Controller : Settings, ShownSettingsTab == tab, tab);
        Sections = Tab switch
        {
            "play" when _state.InQuickPlay && _state.Session is not { Custom: true, Lobby: null } =>
                [Link("Quick Play", Home), Link("Custom lobbies", Lobbies), Link("Lobby", Lobby), Link("Practice", Practice)],
            "play" => [Link("Quick Play", Home), Link("Custom lobbies", Lobbies), Link("Practice", Practice)],
            // Stats beside the replays it is counted from, a page of the tab as Practice is Play's
            "replays" => [Link("Replays", Replays), Link("Stats", Stats)],
            "settings" =>
            [
                Page("Game", SettingsTab.Game), Page("Online", SettingsTab.Online),
                Page("Controller", SettingsTab.Controller), Page("Launcher", SettingsTab.Launcher),
            ],
            _ => [],
        };
    }

    private void RefreshMachine()
    {
        MachineShown = !_state.Racing;
        // the machine raced (RacedMachine): once the server has spoken, what it races the player with (the custom
        // machine it was told of); else the player's own, the custom machine when one is set
        var custom = _state.Settings.Racer.Raced.Custom;
        if (Lobby.Raced is { } raced && Machines.Has(raced.Machine))
        {
            custom = _state.Session is { } session ? LobbyViewModel.RacedOf(raced, session).Custom : custom;
            (ShownMachine, ShownStockColor, ShownPaint) = (raced.Machine, raced.StockColor, Rgb.Hex(raced.Color));
        }
        else if (Garage.ChosenIndex >= 0 && Garage.ChosenIndex != Garage.MachineIndex)
        {
            // (the Garage's PREVIEW shows another machine, not raced: the column keeps the one SELECT stands on, in
            // its look as kept)
            var look = _state.Settings.Racer.LookOf(Garage.ChosenIndex);
            (ShownMachine, ShownStockColor, ShownPaint) = (Garage.ChosenIndex, look.StockColor ?? (look.Color is null ? 0 : null),
                look.Color is { } c ? Rgb.Hex(c) : ShownPaint);
        }
        else if (Garage.MachineIndex >= 0)
        {
            (ShownMachine, ShownStockColor, ShownPaint) = (Garage.MachineIndex, Garage.StockColor, Garage.ColorHex);
        }
        // a custom machine raced: its parts in 3D, each in its colour as the builder shows it (MainViewModel.Custom.cs)
        ShownCustom = custom is null ? null : new CustomBuild(custom.Body, custom.Cockpit, custom.Booster, custom.Pilot);
        var colours = PartColoursOf(custom);
        if (!colours.SequenceEqual(ShownPartColours))
        {
            ShownPartColours = colours;
        }
        Account.ShowMachine(new RacedMachine(ShownMachine, custom));
    }

    /// <summary>The Garage's stage stands in the venue of the course drawn in the lobby, else of the course picked for
    /// Practice.</summary>
    private void ShowVenue() => Garage.VenueCourse = _state.Session?.CourseChosen?.Course ?? Practice.Courses.Picked;

    private void Refresh()
    {
        ShowVenue();
        if (_state.Session != _watched)
        {
            _watched = _state.Session;
            if (_watched is not null)
            {
                _watched.Changed += () => OnUi(Refresh);
            }
        }
        // the lobby page shows the course vote, the machine choice and the race: the vote and the choice bring the
        // player to it, the race from Quick Play's page
        var screen = LobbyScreens.Of(_state.InQuickPlay ? _state.Session?.Lobby : null);
        if (_state.InQuickPlay && LobbyScreens.BringsToLobby(_lobbyScreen, screen, Current is HomeViewModel))
        {
            if (screen is LobbyScreen.Starting or LobbyScreen.Choosing)
            {
                BringToDecision(screen);   // (MainViewModel.Decision.cs)
            }
            else
            {
                Current = Lobby;
            }
        }
        else if (Current is LobbyViewModel && _state.InQuickPlay && _state.Session is { Custom: true, Lobby: null })
        {
            Current = Lobbies;   // out of a custom lobby (removed by the host, or it closed): the browser says why
        }
        else if (Current is LobbyViewModel && !_state.InQuickPlay)
        {
            Current = Home;   // out of Quick Play: its lobby page has nothing to show
        }
        ForgetDecision(screen);
        _lobbyScreen = screen;
        // the server is not asked while the game runs
        _feed.Watching = !_state.Racing;
        Scale = _state.Settings.ShownInterfaceScale / 100.0;
        RefreshSections();
        RefreshMachine();
    }

    public void Dispose()
    {
        _feed.Dispose();
        Disc.Dispose();          // a preparation at work is stopped
        Controller.Hidden();
        ControllerHelper.StopAll();   // none outlives the launcher's window
        Account.Dispose();
        Lan.Dispose();
        Notices.Dispose();
    }
}
