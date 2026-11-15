using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>One of the hub's entries: what it is called, the line that explains it while the focus is on it, and
/// whether it is the big one (the page's main action).</summary>
public sealed partial class HubEntry : ObservableObject
{
    public HubEntry(string id, string label, string about, bool big = false)
    {
        Id = id;
        Label = label;
        About = about;
        Big = big;
    }

    public string Id { get; }

    /// <summary>What it is called (Start F-Zero GX: "Prepare the game" while the game's files are not ready).</summary>
    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>What the entry is, in a line.</summary>
    public string About { get; }

    public bool Big { get; }

    /// <summary>The entry can be pressed (off: the hub's line says why).</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;
}

/// <summary>
/// The redesigned interface's Play page out of a lobby, the hub: four entries (Quick Play, the big one; Custom lobby;
/// Practice; Start F-Zero GX), one line for the entry the focus is on, and the player's machine. Everything it does is
/// the classic launcher's: Quick Play and its lobbies (<see cref="HomeViewModel"/>, <see cref="LobbyViewModel"/>), the
/// custom lobbies, Practice, and the game on its own with its reasons (<see cref="PlayControlViewModel"/>). While the
/// game's files are not ready (missing, damaged, another version's), the last entry is "Prepare the game", and it,
/// Quick Play and Practice open the panel that prepares them (<see cref="PrepareSheetViewModel"/>): none of them
/// could start a race before.
/// </summary>
public sealed partial class HubViewModel : ViewModelBase
{
    public const string Quick = "quick", Custom = "custom", Practice = "practice", Game = "game";

    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly AppState _state;
    private readonly HubEntry _game;
    private HubEntry _on;

    public HubViewModel(MainViewModel main, ShellViewModel shell, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        _main = main;
        _shell = shell;
        _state = state;
        _game = new HubEntry(Game, "Start F-Zero GX", "The game on its own: Grand Prix, Story, Time Attack.");
        Entries =
        [
            new HubEntry(Quick, "Quick Play", "Race people online. Vote for the course, then drive.", big: true),
            new HubEntry(Custom, "Custom lobby", "Your rules. Invite friends with a password."),
            new HubEntry(Practice, "Practice", "Drive alone on any course. No pressure."),
            _game,
        ];
        _on = Entries[0];
        main.Control.PropertyChanged += (_, _) => Refresh();
        main.Feed.Changed += () => OnUi(Refresh);
        state.Changed += () => OnUi(Refresh);
        Refresh();
    }

    /// <summary>The shell: whether a game runs ("End game").</summary>
    public ShellViewModel Shell => _shell;

    /// <summary>The top bar's PICONET, from any page: the hub. What is open over the page closes, a page that keeps
    /// nothing unsaved is left as it is (the builder keeps its draft, the lobby its seat: the lobby button brings the
    /// player back). Asked first from every page where something would be lost or missed (<see cref="HomeQuestion"/>):
    /// a new lobby's rules, the host's unsaved rules, a server's address being typed, a button being set, any lobby or
    /// the queue (its vote, its machine choice, its race), and while a game of the launcher's runs. Nothing while a
    /// question is open, or before the first start is through.</summary>
    [RelayCommand]
    private void GoHome()
    {
        if (!_main.Navigation || _shell.AskingConfirm)
        {
            return;
        }
        if (HomeQuestion() is { } question)
        {
            _shell.Ask(question.Title, question.Line, "Main menu", Home, keep: "Stay");
            return;
        }
        Home();
    }

    /// <summary>What going to the hub would lose or miss, in a question; null: nothing.</summary>
    public (string Title, string Line)? HomeQuestion() => _main.Current switch
    {
        CustomLobbiesViewModel { Creating: true } => ("Leave the new lobby?", "Its rules are not kept."),
        LobbyViewModel { HostEditing: true } => ("Leave the rules unsaved?", "The lobby keeps the rules it had."),
        LobbyViewModel lobby => ("Go to the main menu?", LobbyLine(lobby.Phase, _shell.GameRunning)),
        ServersViewModel { Adding: true } => ("Leave the new server?", "The address you typed is not kept."),
        ControllerViewModel { Capturing: true } => ("Stop setting the button?", "The button keeps what it had."),
        _ when _shell.GameRunning => ("Go to the main menu?", "F-Zero GX keeps running. End game is on the main menu."),
        _ => null,
    };

    /// <summary>What going to the hub means from a lobby in this phase (the player keeps their seat there).</summary>
    public static string LobbyLine(LobbyPhase phase, bool gameRunning) => phase switch
    {
        LobbyPhase.Finding => "You stay in the queue. The lobby button at the top brings you back.",
        LobbyPhase.Vote => "You stay in the lobby, but the vote goes on without you. The lobby button at the top brings you back.",
        LobbyPhase.Machine => "You stay in the lobby, but the machine choice goes on without you. The lobby button at the top brings you back.",
        LobbyPhase.Starting or LobbyPhase.Race when gameRunning => "Your race goes on in the game. The lobby button at the top brings you back.",
        LobbyPhase.Starting => "You stay in the lobby while its race starts. The lobby button at the top brings you back.",
        LobbyPhase.Race => "You stay in the lobby while its race runs. The lobby button at the top brings you back.",
        _ => "You stay in the lobby. The lobby button at the top brings you back.",
    };

    /// <summary>To the hub: what is over the page closed, a new lobby's set-up and the host's rules dropped, the
    /// builder and a lobby's whole Garage closed (the builder keeps its draft).</summary>
    private void Home()
    {
        for (var i = 0; i < 8 && _shell.CloseOverPage(); i++)
        {
            // (the keyboard, a panel, the bell's list, the chat: the top one each time)
        }
        switch (_main.Current)
        {
            case CustomLobbiesViewModel { Creating: true } lobbies:
                lobbies.BackCommand.Execute(null);
                break;
            case LobbyViewModel { HostEditing: true } lobby:
                lobby.CancelRulesCommand.Execute(null);
                break;
            case LobbyViewModel { FullGarage: true } lobby:
                lobby.FullGarage = false;
                break;
            case ServersViewModel { Adding: true } servers:
                servers.CancelAddCommand.Execute(null);
                break;
            case ControllerViewModel { Capturing: true } pad:
                pad.CancelCaptureCommand.Execute(null);
                break;
            default:
                break;
        }
        if (_shell.GaragePage.Building)
        {
            _shell.GaragePage.CloseBuilder();
        }
        _main.Current = this;
    }

    /// <summary>The top bar's count beside the profile: how many players are online on the server set (its status's
    /// figure); -1 before it has answered, or while it does not (the count shows a dash).</summary>
    [ObservableProperty]
    public partial int PlayersOnline { get; set; } = -1;

    /// <summary>What the count is, on the pointer's rest: "128 players online on Quick · 12 racing now".</summary>
    [ObservableProperty]
    public partial string OnlineTip { get; set; } = OnlineUnknown;

    private const string OnlineUnknown = "Players online: not known yet.";

    /// <summary>The server's message of the day shows (it has one, and the player did not hide it for today).</summary>
    [ObservableProperty]
    public partial bool MotdShown { get; set; }

    [ObservableProperty]
    public partial string MotdText { get; set; } = "";

    /// <summary>When the message was set: "Today 18:00", "12 Nov".</summary>
    [ObservableProperty]
    public partial string MotdWhen { get; set; } = "";

    private string? _motdId;

    /// <summary>The message pressed: a panel with the whole of it and "Hide for today".</summary>
    [RelayCommand]
    private void OpenMotd() => _shell.Sheet = new MotdSheetViewModel(this);

    /// <summary>"Hide for today": the message stays away until tomorrow, or until the server has another.</summary>
    [RelayCommand]
    private void HideMotd()
    {
        if (_motdId is { } id)
        {
            _state.Update(s => s with { MotdHidden = LauncherSettings.MotdHiddenFor(id, DateOnly.FromDateTime(DateTime.Now)) });
        }
        _shell.Sheet = null;
        Refresh();
    }

    public IReadOnlyList<HubEntry> Entries { get; }

    /// <summary>The window's shared parts the page shows: the player's machine in 3D, the connection.</summary>
    public MainViewModel Main => _main;

    /// <summary>The line under the entries: what the entry the focus is on is, or why it cannot be pressed now.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "";

    /// <summary>The focus is on this entry (the pointer, the keyboard or the pad): its line shows.</summary>
    public void On(HubEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _on = entry;
        Refresh();
    }

    /// <summary>An entry pressed. Quick Play: its page (the lobby while the player is in one; without a server, to
    /// where one is chosen). Custom lobby and Practice: their pages. Start F-Zero GX: the game on its own (in a
    /// lobby it asks first).</summary>
    [RelayCommand]
    private void Open(HubEntry entry)
    {
        if (entry.Id is Quick or Practice or Game && PrepareSheetViewModel.Wanted(_state))
        {
            _shell.OpenPrepare();
            return;
        }
        switch (entry.Id)
        {
            case Quick:
                _shell.ShowQuickPlay();
                break;
            case Custom:
                _main.Current = _main.Lobbies;
                break;
            case Practice:
                _main.Current = _main.Practice;
                break;
            case Game when entry.Enabled:
                _main.Control.PlayGameCommand.Execute(null);
                break;
            default:
                break;
        }
    }

    /// <summary>The player's machine pressed: the Garage.</summary>
    [RelayCommand]
    private void OpenGarage() => _main.Current = _main.Garage;

    private void Refresh()
    {
        var control = _main.Control;
        var prepare = PrepareSheetViewModel.Wanted(_state);
        _game.Label = prepare ? "Prepare the game" : "Start F-Zero GX";
        _game.Enabled = prepare || control.GameEnabled;
        var feed = _main.Feed;
        var status = feed.Failed ? null : feed.Latest;
        var racing = status?.Races.Sum(r => r.Players.Count) ?? 0;
        PlayersOnline = status?.PlayersOnline ?? -1;
        OnlineTip = status is null ? OnlineUnknown : OnlineText(status.PlayersOnline, racing, status.Name);
        var motd = status?.Motd;
        _motdId = motd?.Id;
        MotdShown = motd is not null && !_state.Settings.MotdHiddenOn(motd.Id, DateOnly.FromDateTime(DateTime.Now));
        MotdText = motd?.Text ?? "";
        MotdWhen = motd is null ? "" : When(motd.Time, DateTimeOffset.Now);
        Line = _on.Id switch
        {
            Game or Quick or Practice when prepare => "The game's files are not ready yet. Press to prepare them.",
            Quick when control.NoServer => "No server set yet. Choose one on the next page.",
            Game when !control.GameEnabled && control.GameNote.Length > 0 => control.GameNote,
            _ => _on.About,
        };
    }

    /// <summary>The count's tip: "128 players online on Quick · 12 racing now."</summary>
    public static string OnlineText(int online, int racing, string server)
    {
        var players = online == 1 ? "1 player online" : $"{online} players online";
        var on = string.IsNullOrWhiteSpace(server) ? "" : " on " + server;
        return players + on + (racing > 0 ? $" · {racing} racing now" : "") + ".";
    }

    /// <summary>A moment as the message's panel says it: "Today 18:00", "Yesterday 09:30", "12 Nov".</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        var day = DateOnly.FromDateTime(local.DateTime);
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var clock = local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return day == today ? "Today " + clock
            : day == today.AddDays(-1) ? "Yesterday " + clock
            : local.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
    }
}
