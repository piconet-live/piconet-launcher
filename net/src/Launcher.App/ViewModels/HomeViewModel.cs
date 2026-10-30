using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>
/// The Play page out of a lobby's own screens (direction C's Home): at the top the race of the player's
/// lobby as a live 3D map beside its standings, and in the same place, at the same size, what there is to say when
/// there is no such race (the lobby gathering or voting, nobody online, no disc for the map, no news of the race);
/// under it the server's lobbies as a table, and the player's last races from their replays. Out of a lobby everything
/// comes from the server's public status (<see cref="ServerStatusFeed"/>); in one, from the lobby itself.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    /// <summary>How many of the standings fit beside the map.</summary>
    private const int Shown = 6;

    /// <summary>How many racers' names fit beside the map's place, two to a line.</summary>
    private const int PeopleShown = 14;

    /// <summary>No report of a running race for this long: the map says so.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(4);

    private readonly AppState _state;
    private readonly ServerStatusFeed _feed;
    private readonly Action _showLobby;
    private readonly Action _showReplays;
    private readonly Action _showSettings;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private QuickPlaySession? _session;
    private long _news = Environment.TickCount64;
    private string _replaysOf = "";
    private string _askedFor = "";
    private string? _motdId;

    public HomeViewModel(AppState state, ServerStatusFeed feed, LobbyViewModel lobby, Action showLobby, Action showReplays,
        Action showSettings)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        _state = state;
        _feed = feed;
        Lobby = lobby;
        _showLobby = showLobby;
        _showReplays = showReplays;
        _showSettings = showSettings;
        state.Changed += () => OnUi(Attach);
        feed.Changed += () => OnUi(Refresh);
        lobby.Race.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RaceViewModel.MapRacers))
            {
                _news = Environment.TickCount64;   // a report of the race came
            }
            else if (e.PropertyName is nameof(RaceViewModel.TimeText) or nameof(RaceViewModel.Waiting))
            {
                Refresh();
            }
        };
        lobby.Race.Standings.CollectionChanged += (_, _) => RefreshStandings();
        _clock.Tick += (_, _) => Refresh();
        Attach();
    }

    /// <summary>The lobby's own page model: its race gives the map and the standings.</summary>
    public LobbyViewModel Lobby { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWords), nameof(IsVote), nameof(IsRace))]
    public partial HomeBlock Block { get; set; }

    public bool IsWords => Block == HomeBlock.Words;

    public bool IsVote => Block == HomeBlock.Vote;

    public bool IsRace => Block == HomeBlock.Race;

    /// <summary>Beside the page's headings: the server's rules, once it has told them (it does when the player joins).</summary>
    [ObservableProperty]
    public partial string RulesLine { get; set; } = "";

    /// <summary>The words in the map's place: a line (the lobby), a large word (its phase) and a value under it.</summary>
    [ObservableProperty]
    public partial string Over { get; set; } = "";

    [ObservableProperty]
    public partial string Big { get; set; } = "";

    [ObservableProperty]
    public partial string Under { get; set; } = "";

    /// <summary>The course vote: how long is left, and its options.</summary>
    [ObservableProperty]
    public partial string VoteClock { get; set; } = "";

    public ObservableCollection<VoteRow> VoteRows { get; } = [];

    /// <summary>Beside the map's place: a line, a heading, and under it the standings, the racers, or a sentence.</summary>
    [ObservableProperty]
    public partial string SideOver { get; set; } = "";

    [ObservableProperty]
    public partial string SideTitle { get; set; } = "";

    [ObservableProperty]
    public partial string SideText { get; set; } = "";

    public ObservableCollection<PersonRow> People { get; } = [];

    [ObservableProperty]
    public partial bool HasPeople { get; set; }

    public ObservableCollection<HomeStanding> Standings { get; } = [];

    /// <summary>Under the standings: how many more race, how many are out.</summary>
    [ObservableProperty]
    public partial string StandingsFoot { get; set; } = "";

    /// <summary>No disc is chosen: the race shows as the list only.</summary>
    [ObservableProperty]
    public partial bool NoDisc { get; set; }

    /// <summary>The race's games have not reached the start yet.</summary>
    [ObservableProperty]
    public partial bool RaceWaiting { get; set; }

    /// <summary>No report of the race for a while: the map and the list dim, with a line that says so.</summary>
    [ObservableProperty]
    public partial bool Stale { get; set; }

    [ObservableProperty]
    public partial string StaleText { get; set; } = "";

    [ObservableProperty]
    public partial string StaleAsOf { get; set; } = "";

    public ObservableCollection<LobbyRow> Lobbies { get; } = [];

    /// <summary>Under the table: the server's count, or why the table is empty.</summary>
    [ObservableProperty]
    public partial string LobbiesFoot { get; set; } = "";

    /// <summary>The foot says the server has stopped answering: it is marked as a warning.</summary>
    [ObservableProperty]
    public partial bool LobbiesFootWarns { get; set; }

    public ObservableCollection<RecentRace> Recent { get; } = [];

    [ObservableProperty]
    public partial bool HasRecent { get; set; }

    [ObservableProperty]
    public partial string AllReplays { get; set; } = "";

    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The server's message of the day while it stands (its public status carries it), drawn as its notices
    /// are; null: none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMotd))]
    public partial NoticeItem? Motd { get; set; }

    public bool HasMotd => Motd is not null;

    /// <summary>The race's own page (the lobby's): the whole standings, the player's numbers, following a racer.</summary>
    [RelayCommand]
    private void OpenRace() => _showLobby();

    [RelayCommand]
    private void ChooseDisc() => _showSettings();

    [RelayCommand]
    private void ShowReplays() => _showReplays();

    /// <summary>Plays the race's replay in the game.</summary>
    [RelayCommand]
    private void Watch(RecentRace race) =>
        Notice = _state.Play(race.Path) is { Length: > 0 } why ? $"Replay not played: {char.ToLowerInvariant(why[0])}{why[1..].TrimEnd('.')}" : "";

    private void Attach()
    {
        if (_state.Session is { } s && s != _session)
        {
            _session = s;
            s.Changed += () => OnUi(Refresh);
        }
        RefreshRecent();
        Refresh();
    }

    private void Refresh()
    {
        // (a visit to the custom lobbies is no Quick Play: Home shows Quick Play from outside then)
        var session = _state.InQuickPlay && _state.Session is { Custom: false } ? _state.Session : null;
        var status = _feed.Latest;
        ShowMotd(status?.Motd);
        NoDisc = DiscCheck.Check(_state.Settings.DiscPath) != DiscStatus.Ok;
        RulesLine = session?.Rules is { } rules ? Rules(rules) : "";
        Stale = false;
        if (session is null)
        {
            ShowStatus(status);
        }
        else if (session.Lobby is not { } lobby)
        {
            Words("", "Joining Quick Play", session.Status.TrimEnd('.', '…'));
            Side("", "", "");
        }
        else
        {
            ShowLobby(session, lobby, status?.Lobbies.Any(l => l.Id == lobby.LobbyId) == true
                ? StatusText.LobbyName(status, lobby.LobbyId) : "Your lobby");
        }
        // the race's news is timed and the vote's clock runs
        var ticking = Block is HomeBlock.Race or HomeBlock.Vote;
        if (ticking != _clock.IsEnabled)
        {
            if (ticking)
            {
                _clock.Start();
            }
            else
            {
                _clock.Stop();
            }
        }
        ShowLobbies(session, status);
    }

    /// <summary>The message of the day, kept as it is while the status says the same (the card is not drawn again
    /// every few seconds); it goes when the status has none, or when no status is known.</summary>
    private void ShowMotd(RecentNotice? motd)
    {
        if (motd is null)
        {
            Motd = null;
        }
        else if (Motd is null || _motdId != motd.Id || Motd.Text != motd.Text)
        {
            _motdId = motd.Id;
            Motd = NoticeItem.Of(ServerNotice.Of(motd));
        }
    }

    /// <summary>Out of Quick Play: what the server says of itself. A lobby seen from outside: its phase and how full
    /// it is, with its racers beside.</summary>
    private void ShowStatus(ServiceStatus? status)
    {
        if (_state.Settings.QuickPlayServer is not { } server)
        {
            // nobody is asked (ServerStatusFeed)
            Words("", "No server", "");
            Side("", "", "");
            return;
        }
        if (status is null)
        {
            Words("", _feed.Failed ? "No answer" : "", _feed.Failed ? $"Retrying {ServerHealth.HostOf(server)}" : "");
            Side("", "", "");
            return;
        }
        var online = status.PlayersOnline == 1 ? "1 racer online" : $"{status.PlayersOnline} racers online";
        if (ShownLobby(status) is not { } lobby)
        {
            Words("", "No lobbies", online);
            Side("", "", "");
            return;
        }
        var name = StatusText.LobbyName(status, lobby.Id);
        Words(name, StatusText.Stage(lobby.Stage), StatusText.Count(lobby.Players.Count, lobby.Capacity));
        Side("", "Racers", "", lobby.Players.Select(p => new PersonRow(p.Name, p.Color ?? "#8C96AA", p.Tag)));
    }

    /// <summary>In a lobby: what it is doing (its phase and the values that go with it), and its race while it
    /// runs.</summary>
    private void ShowLobby(QuickPlaySession session, LobbyState lobby, string name)
    {
        var people = lobby.Players.Select(p => new PersonRow(p.Name, Rgb.Hex(p.Color), p.Tag));
        var count = StatusText.Count(lobby.Players.Count, lobby.Capacity);
        switch (lobby.Stage)
        {
            case LobbyState.Racing:
                ShowRace(lobby, name);
                return;
            case LobbyState.Starting when session.CourseVote is { } vote:
                ShowVote(session, vote, name);
                break;
            case LobbyState.Starting:
                Words(name, "Starting", lobby.Waiting == LobbyState.WaitingForRelay ? "Starting race server" : count);
                break;
            case LobbyState.Choosing:
                var ready = lobby.Players.Count(p => p.Chosen);
                Words(Lobby.ChoiceCourseName is { Length: > 0 } course ? $"{name} · {course}" : name, "Machines", lobby.Waiting == LobbyState.WaitingForRelay ? "Starting race server"
                    : $"Ready {ready} of {lobby.Players.Count}");
                break;
            default:
                Words(name, "Gathering", count);
                break;
        }
        Side("", "Racers", "", people);
    }

    private void ShowVote(QuickPlaySession session, CourseVoteState vote, string name)
    {
        Block = HomeBlock.Vote;
        // the votes as they stand while the vote runs (an older server tells them only with the result)
        var drawn = session.CourseChosen is { } chosen && chosen.MatchId == vote.Vote.MatchId ? chosen : null;
        var counts = drawn?.Votes ?? vote.Standing?.Votes;
        var chances = counts is null ? [] : CourseDraw.Chances(counts);
        var left = Math.Max(0, (int)Math.Ceiling((vote.Ends - DateTimeOffset.UtcNow).TotalSeconds));
        VoteClock = drawn is null ? ReplayWords.Seconds(left) : "";
        Over = $"{name} · Course vote";
        Under = "";
        var rows = vote.Vote.Options.Select((course, i) =>
        {
            var n = counts is not null && i < counts.Count ? counts[i] : 0;
            var mine = vote.Mine == i;
            var named = course == CourseVote.RandomCourse
                ? drawn?.Winner == i ? CourseNames.Name(drawn.Course) : "Random course"
                : CourseNames.Name(course);
            return new VoteRow(named, mine, counts is not null, i < chances.Count ? CourseDraw.Percent(chances[i]) : "",
                [.. Enumerable.Range(0, Math.Max(0, n)).Select(k => new VotePip(k == 0 && mine))], drawn?.Winner == i,
                drawn is not null && drawn.Winner != i);
        }).ToList();
        Replace(VoteRows, rows);
    }

    /// <summary>The lobby's race: the map and the standings come from the lobby's race model; here its heading, its foot,
    /// and whether its news has stopped.</summary>
    private void ShowRace(LobbyState lobby, string name)
    {
        Block = HomeBlock.Race;
        var race = Lobby.Race;
        RaceWaiting = race.Waiting;
        var course = lobby.Race is { } r ? CourseNames.Name(r.Course) : race.Course;
        var quiet = TimeSpan.FromMilliseconds(Environment.TickCount64 - _news);
        Stale = !race.Waiting && race.Stage != "Results" && quiet > Quiet;
        // the course over the standings; the leader's lap and the race's time as its title
        var clock = string.Join(" · ", new[] { race.LapText, race.TimeText }.Where(t => t.Length > 0));
        Side(course.Length > 0 ? $"{name} · {course}" : name, race.Waiting ? "Starting" : clock.Length > 0 ? clock : "Racing", "");
        StaleText = $"Last heard {(int)quiet.TotalSeconds} s ago";
        StaleAsOf = $"as of {race.TimeText}";
        RefreshStandings();
    }

    private void RefreshStandings()
    {
        var all = Lobby.Race.Standings;
        Replace(Standings, [.. all.Take(Shown).Select(s => new HomeStanding(s.Rank, s.Name, s.ColorHex, s.Detail, s.You, s.Out))]);
        var more = Math.Max(0, all.Count(s => !s.Out) - Standings.Count(s => s.Place != "-"));
        var gone = all.Count(s => s.Out);
        StandingsFoot = string.Join(" · ", new[] { more > 0 ? $"{more} more" : "", gone > 0 ? $"{gone} KO" : "" }.Where(t => t.Length > 0));
    }

    private void Words(string over, string big, string under)
    {
        Block = HomeBlock.Words;
        (Over, Big, Under) = (over, big, under);
    }

    private void Side(string over, string title, string text, IEnumerable<PersonRow>? people = null)
    {
        // as many as fit beside the map's place; the rest as a count
        List<PersonRow> all = [.. people ?? []];
        var fit = Math.Min(all.Count, PeopleShown);
        (SideOver, SideTitle, SideText) = (over, title, all.Count > fit ? $"and {all.Count - fit} more" : text);
        Replace(People, [.. all.Take(fit)]);
        HasPeople = People.Count > 0;
    }

    /// <summary>Changes a shown list only where it differs, so a table refreshed many times a second stays still.</summary>
    private static void Replace<T>(ObservableCollection<T> shown, IReadOnlyList<T> now)
    {
        for (var i = 0; i < now.Count; i++)
        {
            if (i == shown.Count)
            {
                shown.Add(now[i]);
            }
            else if (!EqualityComparer<T>.Default.Equals(shown[i], now[i]))
            {
                shown[i] = now[i];
            }
        }
        while (shown.Count > now.Count)
        {
            shown.RemoveAt(shown.Count - 1);
        }
    }

    /// <summary>The lobby shown at the top, out of a lobby: the one RACE would join; when none takes players now, the
    /// first.</summary>
    private static LobbyView? ShownLobby(ServiceStatus status) =>
        ServerStatusFeed.QuickPlayLobby(status) ?? (status.Lobbies.Count > 0 ? status.Lobbies[0] : null);

    /// <summary>The server's rules in a line: laps; how the course is picked and CPUs only when not by a vote, Quick
    /// Play's own way.</summary>
    private static string Rules(ServerRules r)
    {
        var parts = new List<string> { $"{r.Laps} {(r.Laps == 1 ? "lap" : "laps")}" };
        if (r.CourseSelect != CourseSelectMode.Vote)
        {
            parts.Add(r.CourseSelect == CourseSelectMode.Random ? "random course"
                : r.FixedCourse is { } c ? CourseNames.Name(c) : "the host's course");
        }
        if (r.CpuFill != CpuFillMode.Vote)
        {
            parts.Add(r.CpuFill == CpuFillMode.Never ? "no CPUs"
                : $"CPUs fill empty seats ({LanViewModel.CpuLevels[Math.Clamp(r.Race.CpuLevel, 0, 3)]})");
        }
        return string.Join(" · ", parts);
    }

}
