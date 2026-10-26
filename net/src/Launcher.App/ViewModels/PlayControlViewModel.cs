using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The left column's play control (direction C: "one button that always says what it will do or what is happening").
/// Out of Quick Play it is RACE, with what pressing it would do under it (from the server's public status). In Quick
/// Play it is a box that says what the lobby is doing (gathering, voting, choosing machines, racing) and leads to the
/// lobby page. With no server set (the settings could not be read) it leads to Settings, Online, where one is chosen, and
/// connects to nothing. Under it, "Play F-Zero GX" starts the whole game on this computer with no server; in a lobby it asks
/// before leaving, and while the game runs it is greyed out with the reason.
/// </summary>
public sealed partial class PlayControlViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly ServerStatusFeed _feed;
    private readonly LobbyViewModel _lobby;
    private readonly Action _showLobby;
    private readonly Action<SettingsTab> _showSettings;
    private QuickPlaySession? _session;
    private string _gameProblem = "";
    private bool _raceRefused;   // RACE was pressed with a game open, and it is still open
    private bool _starting;

    public PlayControlViewModel(AppState state, ServerStatusFeed feed, LobbyViewModel lobby, Action showLobby, Action<SettingsTab> showSettings)
    {
        _state = state;
        _feed = feed;
        _lobby = lobby;
        _showLobby = showLobby;
        _showSettings = showSettings;
        state.Changed += () => OnUi(Attach);
        feed.Changed += () => OnUi(Refresh);
        lobby.Race.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RaceViewModel.LapText))
            {
                Refresh();
            }
        };
        // the lobby's line is the one source of the step's word and of the clock: this box shows the same
        lobby.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LobbyViewModel.ClockText) or nameof(LobbyViewModel.Step) or nameof(LobbyViewModel.StepNote))
            {
                Refresh();
            }
        };
        Attach();
    }

    /// <summary>In Quick Play: the box that says what is happening stands where RACE was.</summary>
    [ObservableProperty]
    public partial bool InQuickPlay { get; set; }

    /// <summary>The button's word: RACE, or what has to happen first.</summary>
    [ObservableProperty]
    public partial string GoText { get; set; } = "RACE";

    /// <summary>The player cleared the server in Settings (or the settings could not be read): the page says so in
    /// a line with a way to Settings, never with the big button (the official server is the
    /// default, and Settings the one place to change it).</summary>
    [ObservableProperty]
    public partial bool NoServer { get; set; }

    /// <summary>The box's word, the step the lobby's line marks (GATHERING, COURSE VOTE, DRAW, MACHINES, STARTING,
    /// RACE), and its figure (14/30, the line's clock, lap 2).</summary>
    [ObservableProperty]
    public partial string Word { get; set; } = "";

    [ObservableProperty]
    public partial string Figure { get; set; } = "";

    /// <summary>Under the button or the box: what it would do or what is happening, and what comes next.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLines))]
    public partial string Line1 { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLines))]
    public partial string Line2 { get; set; } = "";

    public bool HasLines => Line1.Length > 0 || Line2.Length > 0;

    /// <summary>The first line is a problem (the game's files, the race's game): in the warning colour.</summary>
    [ObservableProperty]
    public partial bool Line1Warns { get; set; }

    [ObservableProperty]
    public partial string LeaveText { get; set; } = "Leave Lobby";

    /// <summary>A problem with the last race or the last try (no answer, the servers full, the race never started),
    /// or the handler of last resort's notice: the one place the launcher says it, on every page.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The notice comes with a folder to open (the log's, for the handler of last resort's notice).</summary>
    [ObservableProperty]
    public partial bool NoticeHasFolder { get; set; }

    /// <summary>The notice is that the game stands still (<see cref="AppState.Hung"/>): it offers to end it.</summary>
    [ObservableProperty]
    public partial bool NoticeEndsGame { get; set; }

    [ObservableProperty]
    public partial bool GameEnabled { get; set; } = true;

    /// <summary>The line under "Play F-Zero GX" while it cannot be pressed: why.</summary>
    [ObservableProperty]
    public partial string GameNote { get; set; } = "";

    /// <summary>The question before leaving a lobby for the game.</summary>
    [ObservableProperty]
    public partial bool Asking { get; set; }

    [ObservableProperty]
    public partial string AskTitle { get; set; } = "";

    [ObservableProperty]
    public partial string AskLine { get; set; } = "";

    [ObservableProperty]
    public partial string StayText { get; set; } = "Stay in the lobby";

    /// <summary>(In dev mode RACE needs no disc: the stand-in starts no race, <see cref="AppState.StandIn"/>.)</summary>
    private bool HasDisc => _state.StandIn || DiscCheck.Check(_state.Settings.DiscPath) == DiscStatus.Ok;

    /// <summary>RACE cannot be pressed: the game could not be started (<see cref="AppState.RaceProblem"/>, said
    /// under the button).</summary>
    private bool _noGame;

    private bool CanGo() => !_noGame;

    /// <summary>Back to the first screen, where the game is prepared from the disc (the window sets it). While the
    /// launcher is open and the game's files have to be prepared (a preparation failed for a reason that was not the
    /// image's, or a start found them damaged), the button reads PREPARE AGAIN and leads there.</summary>
    public Action? ShowFirstScreen { get; set; }

    private bool _prepare;

    /// <summary>RACE: Quick Play on the online server (or first, without a disc or without a server, to where it is
    /// chosen).</summary>
    [RelayCommand(CanExecute = nameof(CanGo))]
    private async Task GoAsync()
    {
        if (_prepare)
        {
            ShowFirstScreen?.Invoke();
            return;
        }
        if (!HasDisc || _state.Settings.QuickPlayServer is not { } server)
        {
            // the page that holds what is missing: the disc's (Launcher) first, as the button says
            _showSettings(HasDisc ? SettingsTab.Online : SettingsTab.Launcher);
            return;
        }
        if (_state.WhyNotJoin is { } why)
        {
            // a game is open (a match start would find it open and give the seat away): said until it is closed,
            // over whatever notice is up. Or a join is on its way: the first press is still at work. Or the game
            // for a race is not there (the button is off then, with the reason under it: said here all the same)
            _raceRefused = why == AppState.GameOpenText;
            _gameProblem = _raceRefused || why.Length == 0 ? _gameProblem : why;
            ShowNotice();
            return;
        }
        try
        {
            _gameProblem = "";
            var controller = _state.CheckControllerAsync();
            await _state.JoinAsync(new Uri(server)).ConfigureAwait(true);
            _showLobby();
            if (await controller.ConfigureAwait(true) is { } notice)
            {
                _state.Report(notice);   // after the join, which clears the notice before it
            }
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or UriFormatException or IOException)
        {
            await _state.LeaveAsync(_state.JoinFailed(server, e)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // the player left while it was connecting: nothing to say
        }
    }

    /// <summary>Under the notice: the folder it speaks of, in the system's file browser.</summary>
    [RelayCommand]
    private void OpenNoticeFolder() => DolphinTools.TryOpenFolder(_state.ProblemFolder);

    /// <summary>Under the notice that the game stands still: ends it (what it recorded stays).</summary>
    [RelayCommand]
    private void EndGame() => _state.EndHungGame();

    /// <summary>The box pressed: the lobby page.</summary>
    [RelayCommand]
    private void OpenLobby() => _showLobby();

    [RelayCommand]
    private Task LeaveAsync() => _state.LeaveAsync();

    /// <summary>Play F-Zero GX: the game on its own, at its title screen. In Quick Play it asks first.</summary>
    [RelayCommand]
    private async Task PlayGameAsync()
    {
        if (_state.InQuickPlay)
        {
            var lobby = _state.Session?.Lobby;
            AskTitle = lobby is null ? "Leave the queue and start F-Zero GX?" : "Leave the lobby and start F-Zero GX?";
            AskLine = lobby is null ? "Queueing" : StatusText.PhaseAndCount(lobby.Stage, lobby.Players.Count, lobby.Capacity);
            StayText = lobby is null ? "Stay in the queue" : "Stay in the lobby";
            Asking = true;
            return;
        }
        await StartGameAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LeaveAndPlayAsync()
    {
        Asking = false;
        await _state.LeaveAsync().ConfigureAwait(true);
        await StartGameAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Stay() => Asking = false;

    /// <summary>The controllers first (a first start gets a working one; a port 1 with nothing plugged in is said
    /// here, before the race), then the game.</summary>
    private async Task StartGameAsync()
    {
        if (_starting)
        {
            return;
        }
        _starting = true;
        try
        {
            var controller = await _state.CheckControllerAsync().ConfigureAwait(true);
            _gameProblem = _state.PlayOffline() ?? "";
            if (_gameProblem.Length > 0)
            {
                _ = _state.LookForTheGame();   // what the buttons last knew of the game's files may be over
            }
            if (controller is not null)
            {
                _state.Report(controller);
            }
        }
        finally
        {
            _starting = false;
        }
        ShowNotice();
    }

    private void ShowNotice()
    {
        // RACE pressed with a game open: its reason first, for as long as the game is open
        _raceRefused = _raceRefused && _state.Racing;
        var notice = _raceRefused ? AppState.GameOpenText : _state.Problem ?? _gameProblem;
        // (said once in the column: a notice that a line under a button already shows is not shown again)
        Notice = !InQuickPlay && notice == Line1 || notice == GameNote ? "" : notice;
        NoticeHasFolder = Notice.Length > 0 && !_raceRefused && _state is { Problem: not null, ProblemFolder: not null };
        NoticeEndsGame = Notice == AppState.HungText && _state.Hung;
    }

    private void Attach()
    {
        if (_state.Session is { } s && s != _session)
        {
            _session = s;
            s.Changed += () => OnUi(Refresh);
        }
        Refresh();
    }

    private void Refresh()
    {
        // (a visit to the custom lobbies' browser, in no lobby, is no Quick Play: RACE stands, and joins Quick Play)
        var session = _state.InQuickPlay && _state.Session is not { Custom: true, Lobby: null } ? _state.Session : null;
        // a join on its way shows as the queue does, with its way out ("Leave the queue" ends the connecting)
        var joining = session is null && _state.Joining;
        InQuickPlay = session is not null || joining;
        if (session is null)
        {
            Asking = false;
        }
        // off, with the reason, while a game is open, while a join is on its way, and while the game could not be
        // started (no game program, or its files not prepared from the disc); while it could not, the launcher
        // looks again from time to time
        if (_state.RaceProblem is not null)
        {
            _state.LookAgainSoon();
        }
        GameEnabled = !_state.Racing && !joining && _state.GameProblem is null;
        Line1Warns = false;   // only RACE's own lines warn (ShowRace)
        if (joining)
        {
            (Word, Figure, Line1, Line2, LeaveText) = ("JOINING", "", "Connecting", "", "Leave Queue");
        }
        else if (session is null)
        {
            ShowRace();
        }
        else
        {
            ShowLobby(session);
        }
        // the reason the game cannot start is said once, in the column: under RACE when RACE says it, else here
        var problem = _state.GameProblem ?? "";
        GameNote = _state.Racing ? session is { Racing: true } ? "In a race" : AppState.GameOpenText
            : !InQuickPlay && problem == Line1 ? "" : problem;
        ShowNotice();
    }

    /// <summary>Out of Quick Play: RACE, and what it would do, from the server's status.</summary>
    private void ShowRace()
    {
        _prepare = ShowFirstScreen is not null && !_state.StandIn && _state.Game.Wanted is not null && HasDisc;
        NoServer = HasDisc && !_prepare && _state.Settings.QuickPlayServer is null;
        var noGame = !_prepare && !_state.StandIn && _state.RaceProblem is not null && _state.Settings.QuickPlayServer is not null && HasDisc;
        if (noGame != _noGame)
        {
            _noGame = noGame;
            GoCommand.NotifyCanExecuteChanged();
        }
        if (!HasDisc)
        {
            (GoText, Line1, Line2) = ("CHOOSE YOUR DISC", "F-Zero GX (USA) disc image", "");
            return;
        }
        if (_prepare)
        {
            (GoText, Line1, Line2, Line1Warns) = ("PREPARE AGAIN", _state.GameProblem ?? "", "", true);
            return;
        }
        if (_state.Settings.QuickPlayServer is null)
        {
            // what to do is the button's own word; why is the notice's, once (SettingsStore.Notice)
            (GoText, Line1, Line2) = ("CHOOSE A SERVER", "No server", "");
            return;
        }
        GoText = "RACE";
        if (noGame)
        {
            (Line1, Line2, Line1Warns) = (_state.RaceProblem ?? "", "", true);
            return;
        }
        if (_feed.Latest is not { } status)
        {
            // no answer: the connection at the column's foot says so
            (Line1, Line2) = ("", "");
            return;
        }
        if (ServerStatusFeed.QuickPlayLobby(status) is { } lobby)
        {
            // the lobby RACE would join, as the lobby lists name it: "Lobby 2", "Gathering · 14/30"
            Line1 = StatusText.LobbyName(status, lobby.Id);
            Line2 = StatusText.PhaseAndCount(lobby.Stage, lobby.Players.Count, lobby.Capacity);
        }
        else
        {
            (Line1, Line2) = ("New lobby", "");
        }
    }

    /// <summary>In Quick Play: what the lobby is doing.</summary>
    private void ShowLobby(QuickPlaySession session)
    {
        if (session.Lobby is not { } lobby)
        {
            (Word, Figure, Line1, Line2, LeaveText) = ("JOINING", "", session.Status, "", "Leave Queue");
            return;
        }
        LeaveText = "Leave Lobby";
        var seats = $"{LobbyScreens.Racers(lobby)} of {lobby.Capacity} racers";
        var lap = _lobby.Race.LapText is { Length: > 0 } text ? text.Split('/')[0].ToLowerInvariant() : "";
        // the word is the lobby line's step, the figure the line's clock (or, where no clock runs, the racers or the lap)
        Word = LobbyScreens.Name(_lobby.Step).ToUpperInvariant();
        Figure = _lobby.Step switch
        {
            LobbyStep.Gathering => StatusText.Count(LobbyScreens.Racers(lobby), lobby.Capacity),
            LobbyStep.Race => lap,
            _ => _lobby.ClockText,
        };
        (Line1, Line2) = _lobby.Step switch
        {
            // the box above says the phase and the count; the column adds nothing while gathering
            LobbyStep.Gathering => ("", ""),
            LobbyStep.Starting or LobbyStep.Race when session.Racing && lobby.Race is { } race => (seats, CourseNames.Name(race.Course)),
            _ => (seats, _lobby.StepNote),
        };
    }
}

/// <summary>The words for lobbies and races that more than one place shows (the play control, the Play page).</summary>
public static class StatusText
{
    /// <summary>A lobby as players can name it: its place in the server's list ("Lobby 2"). The server has no names for
    /// lobbies today.</summary>
    public static string LobbyName(ServiceStatus? status, string lobbyId)
    {
        for (var i = 0; status is not null && i < status.Lobbies.Count; i++)
        {
            if (status.Lobbies[i].Id == lobbyId)
            {
                return string.Create(CultureInfo.InvariantCulture, $"Lobby {i + 1}");
            }
        }
        return "The lobby";
    }

    /// <summary>A lobby's racers as a value: "14/30".</summary>
    public static string Count(int racers, int capacity) => string.Create(CultureInfo.InvariantCulture, $"{racers}/{capacity}");

    /// <summary>What a lobby is doing and how full it is, as every lobby list shows it:
    /// "Gathering · 14/30".</summary>
    public static string PhaseAndCount(string stage, int racers, int capacity) => $"{Stage(stage)} · {Count(racers, capacity)}";

    public static string Stage(string stage) => stage switch
    {
        LobbyState.Racing => "Racing",
        LobbyState.Starting => "Starting",
        LobbyState.Choosing => "Machines",
        _ => "Gathering",
    };

    /// <summary>The votes to start, for a lobby seen from outside.</summary>
    public static string Votes(LobbyView lobby) => lobby.VotingOpen
        ? $"Gathering · start vote {lobby.StartVotes} of {(lobby.Players.Count / 2) + 1}"
        : "Gathering";

    /// <summary>The votes to start, for the player's own lobby.</summary>
    public static string Votes(LobbyState lobby) => lobby.VotingOpen
        ? $"Start vote {lobby.StartVotes} of {LobbyScreens.StartVotesNeeded(lobby)}"
        : $"Start vote at {lobby.VotingMinimum} racers";

    /// <summary>A lobby's race seen from outside: "Racing Dragon Slope, lap 2 of 3" (the lap when a racer's game has
    /// reported).</summary>
    public static string Racing(ServiceStatus status, LobbyView lobby)
    {
        if (status.Races.FirstOrDefault(r => r.Id == lobby.RaceId) is not { } race)
        {
            return "Racing";
        }
        var course = race.CourseName.Contains(": ", StringComparison.Ordinal)
            ? race.CourseName[(race.CourseName.IndexOf(": ", StringComparison.Ordinal) + 2)..] : race.CourseName;
        return Lap(race) is { } lap ? $"Racing {course}, lap {lap} of {race.Laps}" : $"Racing {course}";
    }

    /// <summary>The leader's lap (1 first), when a racer's game has reported the race.</summary>
    public static int? Lap(RaceView race) =>
        race.Live is { } live && RaceStandings.Order(live.Racers).FirstOrDefault(r => r.State != RacerLive.Out) is { } leader
            ? Math.Min(leader.Lap + 1, race.Laps) : null;
}
