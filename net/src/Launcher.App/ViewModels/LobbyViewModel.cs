using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>The lobby (direction C: 30 racers, the vote and chat in one window): who is in, the server's rules, the
/// votes (start, CPUs, the course with each option's chance as the votes come, and its draw), the machine stage,
/// chat, leaving. One page in four states from the lobby's stage (<see cref="LobbyScreen"/>): gathering, the course
/// vote while a match forms, the machine stage once the course is decided, and the race as it runs
/// (<see cref="Race"/>). A line across the top says where the lobby is (<see cref="Steps"/>), with the one clock
/// (<see cref="ClockText"/>). The machine stage is the Garage itself (<see cref="Garage"/>),
/// under that line; it can be opened all through the lobby (<see cref="LobbyScreens.GarageOpen"/>), and the machine
/// selected there is the one the player races (LobbyViewModel.Machine.cs).</summary>
public sealed partial class LobbyViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly Action _left;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private QuickPlaySession? _session;
    private string? _voteMatch;
    private string? _drawnMatch;
    private string? _choiceMatch;

    /// <param name="garage">The launcher's Garage (the lobby's machine stage is that page); without one, a Garage of
    /// the lobby's own (a test's page).</param>
    /// <param name="stats">Where the machines' stats come from for a Garage of the lobby's own.</param>
    public LobbyViewModel(AppState state, Action left, GarageViewModel? garage = null, Func<Task<MachineStatTable?>>? stats = null)
    {
        _state = state;
        _left = left;
        Garage = garage ?? new GarageViewModel(state, stats);
        Garage.Picked += OnPicked;
        Garage.LookChanged += OnLookChanged;
        Garage.LookSaved += SendLook;
        Garage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GarageViewModel.MachineIndex) or nameof(GarageViewModel.Phase) or nameof(GarageViewModel.Shelf))
            {
                OnPropertyChanged(nameof(CanReady));
                OnPropertyChanged(nameof(IsReady));
            }
        };
        Race = new RaceViewModel(state);
        Race.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RaceViewModel.Waiting) && _session is { } session)
            {
                ShowSteps(session, session.Lobby);
            }
        };
        state.Changed += () => OnUi(Attach);
        _clock.Tick += (_, _) => Tick();
        _noteClock.Tick += (_, _) => ShowChatNote(_session);
    }

    /// <summary>The lobby's race, shown while it runs.</summary>
    public RaceViewModel Race { get; }

    /// <summary>The Garage: the lobby's machine stage, and open all through the lobby.</summary>
    public GarageViewModel Garage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gathering), nameof(Starting), nameof(Choosing), nameof(Racing), nameof(RosterShown),
        nameof(CustomBarShown), nameof(HostCanEdit))]
    public partial LobbyScreen Screen { get; set; }

    /// <summary>The line across the top: the steps of this server's lobbies, the one the lobby is at marked.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StepMark> Steps { get; set; } = [];

    /// <summary>Where the lobby is on the line.</summary>
    [ObservableProperty]
    public partial LobbyStep Step { get; set; }

    /// <summary>The one clock, at the line's end: what is left of the course vote or of the machine stage (0'32",
    /// the series' way);
    /// "" while neither runs.</summary>
    [ObservableProperty]
    public partial string ClockText { get; set; } = "";

    /// <summary>On the line: the stat pack this lobby's races run on, by name and author ("GX Unleashed · AKC12");
    /// "" for a lobby on the original stats.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackShown), nameof(BandPackText), nameof(BandPackTip))]
    public partial string PackText { get; set; } = "";

    public bool PackShown => PackText.Length > 0;

    /// <summary>The pack on the machine stage's course band: its label, "Stock" for the game's own stats.</summary>
    public string BandPackText => PackShown ? PackText : "Stock";

    /// <summary>The band's pack whole, when pointed at ("Stock": none).</summary>
    public string? BandPackTip => PackShown ? PackTip : null;

    /// <summary>The pack's label when pointed at: the label, and the game's code constants the pack changes, a line
    /// each (<see cref="LobbyScreens.PackTip"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BandPackTip))]
    public partial string PackTip { get; set; } = "";

    /// <summary>A moment the launcher makes itself heard at (a sound, the taskbar's button).</summary>
    public event Action<LobbyMoment>? Moment;

    /// <summary>A sound of the lobby's that is no moment (no flash): the draw's tick on every hop of its frame, and
    /// its bleep on the course drawn.</summary>
    public event Action<LauncherSound>? Sounded;

    /// <summary>A label, with its values beside it (<see cref="NoticeValues"/>): the server refused a machine
    /// ("Machine not allowed", the machines it allows); the last match start's stat pack was not taken and no game
    /// was started ("Stat pack not taken", the pack; <see cref="QuickPlaySession.StatPackProblem"/>, for as long as
    /// it stands); or, while the lobby gathers, players with another build of the game are in Quick Play and are not
    /// matched with this lobby ("2 players online on another version", the part that differs on each side:
    /// <see cref="QuickPlaySession.OtherBuildsValues"/>). The last problem of
    /// a race or of the launcher itself (<see cref="AppState.Problem"/>: the game could not start, the race never
    /// started, the handler of last resort's notice) is said once, in the left column, which is on every page.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The notice's values, each a key and a value ("allowed" "Blue Falcon only"), beside its label.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NoticeValue> NoticeValues { get; set; } = [];

    /// <summary>The notice's whole sentence, where there is one (a refused stat pack's reason), for its tooltip.</summary>
    [ObservableProperty]
    public partial string NoticeWhole { get; set; } = "";

    /// <summary>The racers' list stands under the vote and is the gathering's page; the race has its standings
    /// instead, and the Garage the whole page.</summary>
    public bool RosterShown => !Racing && !GarageShown && !RulesOpen;

    /// <summary>"Racers 22 of 30", with " · Watching 2" where the server takes spectators, and what a tick in the
    /// list means in this state.</summary>
    [ObservableProperty]
    public partial string RosterTitle { get; set; } = "";

    [ObservableProperty]
    public partial string RosterKey { get; set; } = "";

    /// <summary>The seats in columns: two of fifteen with the machines while gathering, three of ten otherwise (two
    /// on a narrow page).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SeatColumn> SeatColumns { get; set; } = [];

    /// <summary>The lobby's watchers (<see cref="PlayerInfo.Watching"/>), beside the racers' seats: a name and its
    /// paint each, the player's own among them while they watch; no number, no tick (a watcher has no vote). Empty
    /// where the server takes no spectators.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchingShown))]
    public partial IReadOnlyList<SeatRow> WatchingRows { get; set; } = [];

    /// <summary>The watchers' list stands beside the seats: someone watches.</summary>
    public bool WatchingShown => WatchingRows.Count > 0;

    /// <summary>The player watches the lobby's races (the session's word): no vote of theirs, so the course cards
    /// take no press and the start's vote is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVoteStart))]
    public partial bool Watching { get; set; }

    /// <summary>"Vote to start" can be pressed: the vote is open and the player races.</summary>
    public bool CanVoteStart => VotingOpen && !Watching;

    /// <summary>How tall a course's picture is in the vote and a seat's row while gathering: what the window's
    /// height leaves them (the view says), so that the page needs no scrolling.</summary>
    [ObservableProperty]
    public partial double PictureHeight { get; set; } = 112;

    [ObservableProperty]
    public partial double SeatHeight { get; set; } = 20;

    /// <summary>The page is narrow and tall enough (the view says): the racers' list keeps two columns in every
    /// state, so that a name, its tag and the ping have room beside the chat.</summary>
    [ObservableProperty]
    public partial bool Narrow { get; set; }

    partial void OnNarrowChanged(bool value)
    {
        if (_session is { } session)
        {
            ShowSeats(session, session.Lobby);
        }
    }

    public bool Gathering => Screen == LobbyScreen.Gathering;

    public bool Starting => Screen == LobbyScreen.Starting;

    public bool Choosing => Screen == LobbyScreen.Choosing;

    public bool Racing => Screen == LobbyScreen.Race;

    /// <summary>Leaving the queue before a lobby is found, the lobby once in one.</summary>
    [ObservableProperty]
    public partial string LeaveText { get; set; } = "Leave the queue";

    public ObservableCollection<CourseOption> CourseOptions { get; } = [];

    [ObservableProperty]
    public partial bool VoteShown { get; set; }

    /// <summary>The draw has landed on the course (the vote can close).</summary>
    [ObservableProperty]
    public partial bool DrawFinished { get; set; }

    [ObservableProperty]
    public partial string RulesText { get; set; } = "";

    public ObservableCollection<ChatLine> Chat { get; } = [];

    [ObservableProperty]
    public partial string StartVotes { get; set; } = "";

    [ObservableProperty]
    public partial string CpuVotes { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVoteStart))]
    public partial bool VotingOpen { get; set; }

    /// <summary>The players vote on CPUs in the empty seats (the server's rules leave it to them).</summary>
    [ObservableProperty]
    public partial bool CpuVoteShown { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    /// <summary>The player's game left its race while it ran, and the lobby still races it.</summary>
    [ObservableProperty]
    public partial bool CanRejoin { get; set; }

    [RelayCommand]
    private void Rejoin() => _state.Rejoin();

    /// <summary>The server takes spectators: the player may watch the lobby's races instead of racing.</summary>
    [ObservableProperty]
    public partial bool CanWatch { get; set; }

    /// <summary>"Watch" while the player races, "Race" while they watch.</summary>
    [ObservableProperty]
    public partial string WatchText { get; set; } = "Watch";

    [RelayCommand]
    private Task WatchAsync() => _session?.WatchAsync(!_session.Watching) ?? Task.CompletedTask;

    [RelayCommand]
    private Task VoteStartAsync() => _session?.VoteStartAsync(true) ?? Task.CompletedTask;

    [RelayCommand]
    private Task VoteCpuAsync(string yes) => _session?.VoteCpuFillAsync(yes == "yes") ?? Task.CompletedTask;

    [RelayCommand]
    private Task VoteCourseAsync(CourseOption option) =>
        _session is { CourseChosen: null, Watching: false } s ? s.VoteCourseAsync(option.Index) : Task.CompletedTask;

    [RelayCommand]
    private async Task SendAsync()
    {
        if (_session is not null && Message.Trim().Length > 0)
        {
            await _session.ChatAsync(Message.Trim()).ConfigureAwait(true);
            Message = "";
        }
    }

    /// <summary>Out of the lobby: a custom lobby back to the browser (still connected), Quick Play's home.</summary>
    [RelayCommand]
    private async Task LeaveAsync()
    {
        if (_session is { InCustomLobby: true } session && ToBrowser is { } browser)
        {
            (AllRulesShown, HostEditing) = (false, false);
            await session.LeaveLobbyAsync().ConfigureAwait(true);
            browser();
            return;
        }
        await _state.LeaveAsync().ConfigureAwait(true);
        _left();
    }

    private void Attach()
    {
        if (_state.Session is { } s && s != _session)
        {
            Forget();   // (another session: its matches' names are its own)
            _session = s;
            s.Changed += () => OnUi(Refresh);
            s.SentHome += _ => OnUi(_left);              // e.g. the servers at capacity: home shows why
        }
        else if (_state.Session is null && _session is not null)
        {
            _session = null;   // left: nothing of the old lobby is shown or voted in
            Forget();
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null)
        {
            return;
        }
        RulesText = _session.Lobby is { Custom: true } ? "" : _session.Rules is { } rules ? Describe(rules) : "";
        ShowVote(_session);
        var lobby = _session.Lobby;
        Screen = LobbyScreens.Of(lobby);
        ShowGarage(_session);
        ShowCustom(_session, lobby);
        ShowChoice(_session);
        ShowRaceStart(_session);
        Clock(_session);
        LeaveText = lobby is null ? "Leave the queue" : "Leave the lobby";
        CanRejoin = _session.CanRejoin && !_state.Racing;
        CanWatch = _session.CanWatch && _session.Race is null;
        WatchText = _session.Watching ? "Race" : "Watch";
        Watching = _session.Watching;
        ShowNotice(_session, lobby);
        if (lobby is not null)
        {
            VotingOpen = lobby.VotingOpen;
            // the count: the votes there are, of the votes a start takes (more than half of the members present)
            var needed = LobbyScreens.StartVotesNeeded(lobby);
            StartVotes = lobby.VotingOpen ? $"{lobby.StartVotes} of {needed}" : $"{LobbyScreens.Racers(lobby)} of {lobby.VotingMinimum} racers";
            CpuVoteShown = CpuFill(_session, lobby) == CpuFillMode.Vote;
            CpuVotes = $"{lobby.CpuFillVotes} of {needed}";
        }
        ShowSeats(_session, lobby);
        ShowSteps(_session, lobby);
        // rebuilt only when a line came (the race's live reports refresh the lobby many times a second), so the chat
        // keeps its place while read
        var paints = new Dictionary<string, string>();
        foreach (var p in lobby?.Players ?? [])
        {
            paints[p.Id] = Rgb.Hex(p.Color);
        }
        // a server's notice (known by its id, never its name) is the server's own line, its text without the tier's word
        var lines = _session.Chat.TakeLast(50).Select(c => ServerNotice.Of(c) is { } notice
            ? new ChatLine(c.FromId, Notices.SenderName, "#000000", notice.Text, you: false, NoticeItem.Of(notice))
            : new ChatLine(c.FromId, c.FromName, paints.TryGetValue(c.FromId, out var paint) ? paint : "#8792A2", Shown(c),
                c.FromId == _session.PlayerId, tag: c.FromTag)).ToList();
        ShowChat(lines);
        ShowChatNote(_session);
    }

    /// <summary>The chat brought to the lines that are to show, line by line: the lines that stay are not taken off
    /// the page (a line being read or selected stays as it is while others arrive, and takes a changed paint in
    /// place); the oldest go from the top, the new ones come at the foot. Only when nothing of what shows is among
    /// them (another lobby) is the list made anew.</summary>
    private void ShowChat(List<ChatLine> lines)
    {
        bool Stays(int gone) => Chat.Count - gone <= lines.Count && Chat.Skip(gone).Zip(lines).All(pair => pair.First.Same(pair.Second));
        var gone = 0;
        while (gone < Chat.Count && !Stays(gone))
        {
            gone++;
        }
        for (var i = 0; i < gone; i++)
        {
            Chat.RemoveAt(0);
        }
        for (var i = 0; i < Chat.Count; i++)
        {
            Chat[i].ColorHex = lines[i].ColorHex;
        }
        foreach (var line in lines.Skip(Chat.Count))
        {
            Chat.Add(line);
        }
    }

    /// <summary>The seats: every racer in the order they joined, then the empty seats (a CPU's, when the rules fill
    /// them), in columns. Ticked: has voted for a course, or is ready with a machine. A watcher has no seat: where
    /// the server takes spectators, the watchers stand in a list of their own beside the seats, and the title counts
    /// them ("Racers 5 of 30 · Watching 1"); the player's own row moves between the two as they watch or race.</summary>
    private void ShowSeats(QuickPlaySession session, LobbyState? lobby)
    {
        if (lobby is null)
        {
            (SeatColumns, WatchingRows, RosterTitle, RosterKey) = ([], [], "", "");
            return;
        }
        var voted = session is { CourseVote: { Standing: { } standing }, CourseChosen: null } ? standing.Voted : null;
        var spectators = session.CanWatch;
        var rows = new List<SeatRow>();
        var watching = new List<SeatRow>();
        foreach (var p in lobby.Players)
        {
            if (spectators && p.Watching)
            {
                watching.Add(new SeatRow("", p.Name, Rgb.Hex(p.Color), "", p.Id == session.PlayerId, false, false, Tag: p.Tag)
                {
                    Id = p.Id, Host = lobby.Custom && p.Id == lobby.HostId, Kickable = session.IsHost && p.Id != session.PlayerId,
                });
                continue;
            }
            var ticked = lobby.Stage == LobbyState.Starting ? voted?.Contains(p.Id) == true : lobby.Stage == LobbyState.Choosing && p.Chosen;
            rows.Add(new SeatRow((rows.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), p.Name,
                Rgb.Hex(p.Color), SeatMachine(p, session, lobby),
                p.Id == session.PlayerId, false, ticked,
                session.Pings.TryGetValue(p.Id, out var ms) ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ms} ms") : "",
                p.Tag)
            {
                Id = p.Id, Host = lobby.Custom && p.Id == lobby.HostId, Kickable = session.IsHost && p.Id != session.PlayerId,
            }.OnTeam(p, TeamsOf(lobby), TeamChanges(session, lobby, p)));
        }
        var racers = rows.Count;
        var empty = CpuFill(session, lobby) == CpuFillMode.Never ? "" : "CPU";
        while (rows.Count < lobby.Capacity)
        {
            rows.Add(new SeatRow((rows.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), empty, "#00000000", "", false, true, false));
        }
        var columns = Gathering || Narrow ? 2 : 3;
        var each = (rows.Count + columns - 1) / columns;
        var seats = Enumerable.Range(0, columns).Select(c => new SeatColumn([.. rows.Skip(c * each).Take(each)])).ToList();
        if (SeatColumns.Count != seats.Count || !SeatColumns.Zip(seats).All(pair => pair.First.Seats.SequenceEqual(pair.Second.Seats)))
        {
            SeatColumns = seats;
        }
        if (!WatchingRows.SequenceEqual(watching))
        {
            WatchingRows = watching;
        }
        RosterTitle = spectators ? $"Racers {racers} of {lobby.Capacity} · Watching {watching.Count}" : $"Racers {racers} of {lobby.Capacity}";
        RosterKey = Starting && voted is not null ? "✓ voted" : Choosing ? "✓ ready" : "";
    }

    private void ShowNotice(QuickPlaySession session, LobbyState? lobby)
    {
        var (label, values, whole) = CustomNotice(session) is { } custom ? custom
            : session.LastError is { Code: "machine_not_allowed" } error
            ? ("Machine not allowed", session.Rules is { } rules ? [new NoticeValue("allowed", Allowed(rules) ?? "any")] : [], error.Message)
            // (until a race starts, the lobby is left or its pack changes)
            : lobby is not null && session.StatPackProblem is { } refused
                ? ("Stat pack not taken", LobbyScreens.PackLabel(lobby.StatPack) is { Length: > 0 } pack ? [new NoticeValue("pack", pack)] : [], refused)
            : lobby?.CannotRace?.Contains(session.PlayerId ?? "") == true
                ? ("Can't race this mode", ModeValues(lobby), "This lobby races a game mode your launcher can't run. Update the launcher to race it.")
            : lobby is { Stage: LobbyState.Forming, OtherBuilds: { Count: > 0 } others } ? Others(session.Build, others)
            : ("", (IReadOnlyList<NoticeValue>)[], "");
        (Notice, NoticeWhole) = (label, whole);
        if (!NoticeValues.SequenceEqual(values))
        {
            NoticeValues = values;
        }

        static (string, IReadOnlyList<NoticeValue>, string) Others(string? build, IReadOnlyList<OtherBuild> others)
        {
            var (label, values) = QuickPlaySession.OtherBuildsValues(build, others);
            return (label, [.. values.Select(v => new NoticeValue(v.Key, v.Value))], QuickPlaySession.OtherBuildsText(build, others));
        }
    }


    /// <summary>A member's seat whose launcher cannot run the lobby's mode (<see cref="LobbyState.CannotRace"/>): they
    /// watch its races.</summary>
    public const string CannotRaceText = "Can't race this mode";

    /// <summary>A seat's machine as its row says it, or that its player cannot race the lobby's mode.</summary>
    private string SeatMachine(PlayerInfo p, QuickPlaySession session, LobbyState lobby) =>
        lobby.CannotRace?.Contains(p.Id) == true ? CannotRaceText : MachineWithPack(p, session);

    /// <summary>The lobby's mode, as a notice's value; none when it names none.</summary>
    private static IReadOnlyList<NoticeValue> ModeValues(LobbyState lobby) =>
        lobby.Rules?.Mode is { } mode ? [new NoticeValue("mode", mode.Name)] : [];

    /// <summary>How the empty seats are filled: never when the lobby's modes allow no CPUs (no vote then), else a
    /// custom lobby's rule, else the server's (a vote where it names none).</summary>
    /// <summary>The teams of a lobby's next race; 0: none.</summary>
    private static bool TeamChanges(QuickPlaySession session, LobbyState lobby, PlayerInfo p) =>
        lobby.Stage == LobbyState.Forming && (session.IsHost || p.Id == session.PlayerId);

    private static int TeamsOf(LobbyState lobby) => lobby.Rules is { Mode: not null } rules ? rules.Value(LobbyRules.TeamsKey) ?? 0 : 0;

    private static CpuFillMode CpuFill(QuickPlaySession session, LobbyState? lobby) => lobby is { NoCpuVote: true } ? CpuFillMode.Never
        : lobby is { Custom: true, Rules: { } rules }
        ? (CpuFillMode)CustomRules.Value(rules, HostRules.CpuFill.Name, session.QuickPlayRules)
        : session.Rules?.CpuFill ?? CpuFillMode.Vote;

    /// <summary>The machines a server allows, when not all: "Blue Falcon only", "12 machines"; null: all.</summary>
    private static string? Allowed(ServerRules r) =>
        r.AllowedMachines.Count >= Machines.Names.Count ? null
        : r.AllowedMachines.Count == 1 && r.AllowedMachines[0] is var only && Machines.Has(only) ? Machines.Names[only] + " only"
        : $"{r.AllowedMachines.Count} machines";

    /// <summary>Quick Play's mines' rule (the server's default, the rule's own: <see cref="HostRules.MinesReturn"/>):
    /// the rules line says nothing of it.</summary>
    public static int QuickPlayMinesMs => HostRules.MinesReturn.Default;

    /// <summary>The mines' rule as the rules line says it, only when it is not Quick Play's:
    /// "mines stay gone" (0: the game's own way), "mines back after 0.5 s"; null at Quick Play's 0.2 s.</summary>
    public static string? MinesRule(int ms) => ms == QuickPlayMinesMs ? null
        : ms <= 0 ? "mines stay gone"
        : "mines back after " + (ms / 1000.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s";

    /// <summary>The machine setting's rule as the rules line says it, when it is not Quick Play's (100 for everyone):
    /// "setting 80-100", "setting 50", "your own setting" (0-100); null: Quick Play's.</summary>
    public static string? SettingRule(SettingRange range) => range == SettingRange.QuickPlay ? null
        : range.IsFree ? "your own setting" : $"setting {range.Text}";

    /// <summary>The lobby's latency limit as the rules line says it, when it is not Quick Play's 250 ms: "ping limit
    /// 150 ms"; null: Quick Play's.</summary>
    public static string? PingRule(int ms) => ms == ServerRules.QuickPlayLatencyLimitMs ? null : $"ping limit {ms} ms";

    private static string Describe(ServerRules r)
    {
        // the course vote and CPUs on a vote are Quick Play's own way, which the page shows itself (its steps, its
        // CPU vote): only another way is said
        var parts = new List<string> { $"{r.Laps} {(r.Laps == 1 ? "lap" : "laps")}" };
        if (r.CourseSelect != CourseSelectMode.Vote)
        {
            parts.Add(r.CourseSelect == CourseSelectMode.Random ? "random course"
                : r.FixedCourse is { } c ? Courses.Name(c) : "the host's course");
        }
        if (r.CpuFill != CpuFillMode.Vote)
        {
            parts.Add(r.CpuFill == CpuFillMode.Never ? "no CPUs"
                : $"CPUs fill empty seats ({LanViewModel.CpuLevels[Math.Clamp(r.Race.CpuLevel, 0, 3)]})");
        }
        if (r.Race.BoostFromStart)
        {
            parts.Add("boost from the start");
        }
        // the mines' rule only when it is not Quick Play's 0.2 s
        if (MinesRule(r.Race.MineRegenMs) is { } mines)
        {
            parts.Add(mines);
        }
        // what is as in every Quick Play race (any machine, at top speed) is not said: the line names what differs,
        // and so stays a line (it was cut off at the window's edge with everything in it)
        if (Allowed(r) is { } allowed)
        {
            parts.Add(allowed);
        }
        if (SettingRule(r.MachineSetting) is { } setting)
        {
            parts.Add(setting);
        }
        // the lobby's latency limit when it is not Quick Play's 250 ms (as the line's other
        // rules: a player above it watches the race)
        if (PingRule(r.LatencyLimitMs) is { } ping)
        {
            parts.Add(ping);
        }
        return string.Join(" · ", parts);
    }
}
