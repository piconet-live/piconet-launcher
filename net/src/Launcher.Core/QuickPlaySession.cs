using Online.Protocol;

namespace Launcher.Core;

/// <summary>A race the session hands to the game: the match file it wrote, where the replay goes (null: not
/// recording), and the match; <paramref name="Rejoin"/>: the game is started again into the race it left.</summary>
public sealed record ForkRace(string MatchFile, string? ReplayFile, MatchStart Start, bool Rejoin = false, bool Spectate = false);

/// <summary>The forming match's course vote as the player sees it: the options, when it ends, their vote, and the
/// vote as it stands when the server tells (<see cref="CourseVotes"/>: the votes per option and who has voted; null
/// from a server that tells only the result).</summary>
public sealed record CourseVoteState(CourseVote Vote, DateTimeOffset Ends, int? Mine, CourseVotes? Standing = null);

/// <summary>The match's machine choice as the player sees it: the options and the course, when it ends, the machine
/// (in its garage look) the choice starts on, and the one the player confirmed (null until they do).</summary>
public sealed record MachineChoiceState(ChooseMachine Choose, DateTimeOffset Ends, MachineLook Default, MachineLook? Mine);

/// <summary>
/// One Quick Play visit to a coordinator, for the views to show (the launcher's logic, no UI): joins
/// with the player's racer (a machine the server allows; their own setting where players choose), keeps the lobby and
/// the chat, votes (start, CPUs, the course), chooses the machine for each race, and when the match starts writes the
/// match file, picks the replay's path (when recording) and hands the race to <c>startFork</c>. <see cref="Changed"/>
/// fires on the receiving thread whenever anything shown changes.
/// </summary>
/// <param name="racer">The player's racer as it is now (the garage can change during a session); without it, the
/// settings' racer with this session's machine choices.</param>
/// <param name="build">The player's game build, told to the coordinator, which matches players of one build only
/// (<see cref="RecompLaunch.Build"/>; null: none named).</param>
/// <param name="debug">A test setting that reaches a race is on in this launcher, told to the coordinator
/// (<see cref="DebugSettings.ReachesARace"/>): one that takes no test settings refuses the visit
/// (<see cref="DebugRefusedText"/>).</param>
/// <param name="inputDelay">The player's input delay as it is when a race starts (the Settings page can change it
/// while the session is in a lobby; <see cref="LauncherSettings.RaceInputDelay"/>). Null: the delay of
/// <paramref name="settings"/>. Held to 0 to 2 whatever it gives.</param>
/// <param name="connect">How the session reaches its coordinator (<see cref="ICoordinatorConnection"/>). Null: the real
/// one, a WebSocket (<see cref="CoordinatorClient.ConnectionAsync"/>).</param>
public sealed partial class QuickPlaySession(LauncherSettings settings, string workFolder, Action<ForkRace> startFork,
    Func<RacerProfile>? racer = null, string? build = null, bool debug = false, Func<int>? inputDelay = null,
    CoordinatorConnect? connect = null) : IAsyncDisposable
{
    private readonly List<ChatMessage> _chat = [];
    private readonly HashSet<string> _noticeIds = [];   // (under _chat's lock)
    private readonly CoordinatorConnect _connect = connect ?? CoordinatorClient.ConnectionAsync;
    private ICoordinatorConnection? _client;
    private ErrorMessage? _lastWord;   // the server's last message, when it was an error
    private bool _leaving;
    private RacerProfile _racer = settings.Racer;

    public event Action? Changed;

    /// <summary>A notice from the server's operator came (on the receiving thread), once per notice.</summary>
    public event Action<ServerNotice>? NoticeReceived;

    /// <summary>The server's notices of the last hour came again (after the welcome, on joining a lobby), oldest
    /// first, replayed (on the receiving thread). They are no chat lines.</summary>
    public event Action<IReadOnlyList<ServerNotice>>? NoticesReplayed;

    /// <summary>The server took the player out of Quick Play (e.g. every server full), with what to tell them: the
    /// launcher goes back to its home screen.</summary>
    public event Action<string>? SentHome;

    /// <summary>The connection ended without the player's doing (the server closed it, or it was lost), with what to
    /// tell them (<see cref="DroppedText"/>): the server's last word when it gave one before it closed.</summary>
    public event Action<string>? Dropped;

    /// <summary>The player's game went out of sync in this race and the server disconnected it (<see
    /// cref="Online.Protocol.RaceDesynced"/>), with what to tell them: the launcher stops the game. The player stays in
    /// the lobby for the next race.</summary>
    public event Action<string>? Desynced;

    /// <summary>The player confirmed a machine in the lobby's machine choice: the launcher keeps it as the racer's machine,
    /// so the next choice starts on it.</summary>
    public event Action<int>? MachineChosen;

    /// <summary>A match start was refused (<see cref="StartsRefused"/>): why, once for this connection, for the
    /// launcher's log.</summary>
    public event Action<string>? Refused;

    /// <summary>The server's answer may mean this launcher is too old for it (<see cref="NewerBuildHint"/>): the
    /// launcher looks for an update, and offers it in place of the server's words when there is one.</summary>
    public event Action? NewerBuildHinted;

    /// <summary>The server answers that may mean a newer launcher is out: a hello refused for its protocol version
    /// (bad_version), or Quick Play ended by a coordinator that takes no players any more (retired: "a new build is
    /// out", its operator's words; the protocol names no reason for it, so any ending with words of the server's but
    /// a full service's and too many joins counts). Only a hint: the update itself says whether there is one.</summary>
    public static bool NewerBuildHint(ServerMessage message) => message switch
    {
        ErrorMessage { Code: "bad_version" } => true,
        QuickPlayEnded { Message.Length: > 0 } r => r.Reason is not ("at_capacity" or "too many joins"),
        _ => false,
    };

    /// <summary>The match starts this session did not act on: the player was in no lobby, or a race of theirs was
    /// still on. A coordinator's word starts a program on the player's computer, and a LAN server is anyone's
    /// computer: one race at a time, and only for a player who is in a lobby.</summary>
    public int StartsRefused { get; private set; }

    private string _status = "Not connected";
    private bool _statusIsNews;   // the status is a server's own word (a refusal, a requeue): nothing stands over it

    /// <summary>What is happening, in a few words for the player. While a refused stat pack stands
    /// (<see cref="StatPackProblem"/>) the lobby's everyday words (its count, a vote, the machine stage) do not word
    /// that sentence away: the next race of this lobby would be refused for the same reason. A server's own word
    /// does: the text of an error it sent, of a requeue, of a race taken away.</summary>
    public string Status => Lobby is not null && !_statusIsNews && StatPackProblem is { } why ? why : _status;

    /// <summary>How <see cref="Status"/> begins when a match start's stat pack was refused; the reason follows.</summary>
    public const string StatPackNotTaken = "Stat pack not taken · ";

    /// <summary>Why the last match start's stat pack was refused, the whole sentence; null: none was, a race has
    /// started since, the player is out of that lobby (it was left, or the connection is gone), or the lobby's
    /// state names another pack or none (the cause is gone: <c>OnLobbyState</c>). One word is written when it is
    /// set, and <see cref="Status"/> reads it: nobody sees the one without the other.</summary>
    public string? StatPackProblem { get; private set; }

    private StatPack? _served;   // the server's stat pack as it sent it after the welcome, checked; null: none, or no pack

    /// <summary>The stat pack this lobby's races run on, with its values, for what is shown before a race (the
    /// Garage): the pack the server sent after the welcome (<see cref="StatPackValues"/>), while the lobby's state
    /// names that very pack by its fingerprint. Null: no lobby, a lobby on the original stats, or a pack this
    /// launcher was not sent or does not take.</summary>
    public StatPack? LobbyStatPack =>
        Lobby?.StatPack is { } named && _served is { } pack && pack.Fingerprint == named.Fingerprint ? pack : null;

    public string? ServerName { get; private set; }

    /// <summary>The game build this session names to the coordinator (null: none).</summary>
    public string? Build { get; } = build;

    /// <summary>The coordinator this session's connection is open to (null before it connects and after it ends).</summary>
    public Uri? ConnectedTo { get; private set; }

    /// <summary>The error that ended the connection while a message was taken in (<see cref="ICoordinatorConnection.Fault"/>),
    /// for the launcher's log; null otherwise.</summary>
    public Exception? Fault => _client?.Fault;

    public string? PlayerId { get; private set; }

    /// <summary>The player's tag on this connection (<see cref="PlayerTags"/>): the welcome's, then a <c>tag</c>
    /// message's (after a rename). The server works it out from <see cref="LauncherSettings.PlayerKey"/>; nothing of
    /// it is saved. Null before the welcome, and from a server without tags.</summary>
    public string? Tag { get; private set; }

    /// <summary>The secret this session's hello sends: the saved one when it is one (null: none, and the server
    /// draws a tag for this visit).</summary>
    private string? SentKey => PlayerTags.IsKey(settings.PlayerKey) ? settings.PlayerKey : null;

    public LobbyState? Lobby { get; private set; }

    /// <summary>The lobby's race as a racer's game reports it, while this player waits and watches (null otherwise).</summary>
    public LiveRace? Live { get; private set; }

    /// <summary>The ping of each player of the lobby, by player id, in milliseconds, as the server last sent them
    /// (<see cref="LobbyPings"/>); a player it has no number for is not there, and neither is anybody on a server that
    /// sends none.</summary>
    public IReadOnlyDictionary<string, int> Pings { get; private set; } = NoPings;

    private static readonly IReadOnlyDictionary<string, int> NoPings = new Dictionary<string, int>();

    /// <summary>In the lobby's race now (between the match start and the player's game ending).</summary>
    public bool Racing => Lobby?.Players.FirstOrDefault(p => p.Id == PlayerId)?.Racing ?? Race is not null;

    public ForkRace? Race { get; private set; }

    /// <summary>The server's rules (from its welcome; null from a server that sends none).</summary>
    public ServerRules? Rules { get; private set; }

    /// <summary>The machine-setting range this lobby races under: a custom lobby's own (its state names both ends),
    /// else the server's (null from a server that sends none).</summary>
    public SettingRange? MachineSetting =>
        Lobby is { Custom: true, Rules: { } r } && r.Value(HostRules.MachineSetting) is { } low
            && r.Value(LobbyRules.SettingHighKey) is { } high && new SettingRange(low, high) is { IsSound: true } range
            ? range : Rules?.MachineSetting;

    /// <summary>The machine this session races: the one it queued with (the player's, or the server's first allowed one),
    /// then the one last chosen.</summary>
    public int? Machine { get; private set; }

    /// <summary>The machine choice before the race (null when there is none).</summary>
    public MachineChoiceState? Choice { get; private set; }

    /// <summary>When the lobby's start countdown runs out, by this computer's clock (<see cref="LobbyState.StartsInMs"/>
    /// counted from the state's arrival); null: none runs.</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>The course vote while the match forms (null when there is none).</summary>
    public CourseVoteState? CourseVote { get; private set; }

    /// <summary>The vote's result, for the draw's animation (null until it comes).</summary>
    public CourseChosen? CourseChosen { get; private set; }

    /// <summary>The last refusal from the coordinator (e.g. machine_not_allowed), for the views to explain.</summary>
    public ErrorMessage? LastError { get; private set; }

    public IReadOnlyList<ChatMessage> Chat
    {
        get
        {
            lock (_chat)
            {
                return [.. _chat];
            }
        }
    }

    public async Task JoinAsync(Uri server, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        _served = null;
        Set("Connecting");
        _client = await _connect(server, settings.Racer.Name, Build, debug, SentKey, cancel).ConfigureAwait(false);
        ConnectedTo = server;
        _client.Closed += reason =>
        {
            ConnectedTo = null;
            StatPackProblem = null;
            _served = null;
            Set(Race is null ? $"Disconnected ({reason})" : _status);
            if (reason != ICoordinatorConnection.Stopped && !_leaving)
            {
                Dropped?.Invoke(DroppedText(_lastWord));
            }
        };
        _client.Received += OnReceived;   // last: the client reads from here on (the welcome, or the hello's refusal)
        Changed?.Invoke();
    }

    public Task VoteStartAsync(bool yes) => Client.VoteStartAsync(yes);

    public Task VoteCpuFillAsync(bool yes) => Client.VoteCpuFillAsync(yes);

    /// <summary>Votes for one of the course vote's options (it can change until the vote ends).</summary>
    public Task VoteCourseAsync(int option)
    {
        if (CourseVote is not { } v || option < 0 || option >= v.Vote.Options.Count)
        {
            return Task.CompletedTask;
        }
        CourseVote = v with { Mine = option };
        Changed?.Invoke();
        return Client.VoteCourseAsync(v.Vote.MatchId, option);
    }

    /// <summary>The machine choice is open at the server: one is on, and the lobby does not wait for its race
    /// server. Once every racer is ready the server has closed the stage (<see cref="LobbyState.Waiting"/> is what
    /// it says of it until the race starts), and a choice sent after that is dropped there without a word: so none
    /// is sent, and nothing here says the player races what they picked after it.</summary>
    public bool ChoiceOpen => Choice is not null && Lobby?.Waiting is null;

    /// <summary>This player as the server has them in the lobby: the machine and colour it races them with.</summary>
    public PlayerInfo? Me => Lobby?.Players.FirstOrDefault(p => p.Id == PlayerId);

    /// <summary>Races this machine, in its garage look (it can change until the choice ends), and keeps it as the racer's
    /// machine. Nothing once the choice is closed (<see cref="ChoiceOpen"/>).</summary>
    public Task ChooseMachineAsync(int machine)
    {
        if (Choice is not { } c || !ChoiceOpen || !c.Choose.Machines.Contains(machine))
        {
            return Task.CompletedTask;
        }
        var profile = Racer;
        var look = profile.LookOf(machine);
        Choice = c with { Mine = look };
        Machine = machine;
        Sent = new RacedMachine(machine, CustomMachine.Checked(profile.Custom));
        _racer = profile.WithMachine(machine);
        MachineChosen?.Invoke(machine);
        Changed?.Invoke();
        return Client.ChooseMachineAsync(c.Choose.MatchId, machine, look.Color ?? profile.Color,
            MachineSetting?.Clamp(Math.Clamp(look.Setting, 0, 100)), look.StockColor, profile.Custom,
            Lobby?.SeatPacks is not null ? SeatPack : null);
    }


    /// <summary>Where a machine choice starts: the racer's machine when the server allows it, else the one this session
    /// races (what the server keeps when the player does not choose), else the first allowed.</summary>
    public static int DefaultChoice(int racerMachine, int? sessionMachine, IReadOnlyList<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return allowed.Contains(racerMachine) ? racerMachine
            : sessionMachine is { } m && allowed.Contains(m) ? m
            : allowed.Count > 0 ? allowed[0] : racerMachine;
    }

    public Task ChatAsync(string text, bool lobby = true) =>
        Client.ChatAsync(lobby ? ControlProtocol.LobbyChannel : ControlProtocol.GlobalChannel, text);

    public async Task LeaveAsync()
    {
        if (_client is not null)
        {
            await (Custom ? _client.LeaveLobbyAsync() : _client.LeaveQuickPlayAsync()).ConfigureAwait(false);
        }
        Lobby = null;
        StartsAt = null;
        StatPackProblem = null;
        Set("Left the queue");
    }

    public async ValueTask DisposeAsync()
    {
        _leaving = true;
        ConnectedTo = null;
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ICoordinatorConnection Client => _client ?? throw new InvalidOperationException("not connected");

    private RacerProfile Racer => racer?.Invoke() ?? _racer;

    /// <summary>The machine the server was last told this player races (the queue's, then each machine choice's);
    /// null before the welcome.</summary>
    public RacedMachine? Sent { get; private set; }

    /// <param name="news">The status is a server's own word, which a standing stat pack problem does not hide.</param>
    private void Set(string status, bool news = false)
    {
        (_status, _statusIsNews) = (status, news);
        Changed?.Invoke();
    }

    /// <summary>The player's game is done with the race: back in the lobby for the next one. <paramref name="rejoinable"/>:
    /// it ended while its race ran (a crash): the race is kept for <see cref="Rejoin"/>, the server is not told.</summary>
    public async Task RaceOverAsync(bool rejoinable = false)
    {
        if (Race is { Spectate: true })
        {
            // a spectator's game closed: nothing to tell the server (it was never a racer of that race)
            Race = null;
            Set(Watching ? "Watching" : "In lobby");
            Changed?.Invoke();
            return;
        }
        if (Race is { } race && _client is not null)
        {
            Race = null;
            if (rejoinable && race.Start.NoRejoin)
            {
                // a mode without CPUs: the seat does not come back, and the server is told, so the player may watch
                LeftForGood = race;
            }
            else if (rejoinable)
            {
                RejoinableRace = race with { Rejoin = true };
                Set("Race left");
                return;
            }
            await _client.RaceOverAsync(race.Start.MatchId, LastSeenOf(race.Start.MatchId)).ConfigureAwait(false);
            Changed?.Invoke();
        }
    }

    /// <summary>What players read while their race waits for its race server to start.</summary>
    public const string StartingServerText = "Starting race server…";

    /// <summary>The lobby in a few words: its players, a race starting, the machine choice, the wait for the race
    /// server, or its race (this player in it or watching).</summary>
    public static string LobbyText(LobbyState l, string? playerId, string? build = null) => l.Stage switch
    {
        _ when l.Waiting == LobbyState.WaitingForRelay => StartingServerText,
        LobbyState.Racing when l.Players.FirstOrDefault(p => p.Id == playerId)?.Racing == true => "Racing",
        LobbyState.Racing => "Watching",
        LobbyState.Starting => "Starting",
        LobbyState.Choosing => "Machines",
        _ => $"Lobby · {l.Players.Count} of {l.Capacity}" + (l.OtherBuilds is { Count: > 0 } others ? ". " + OtherBuildsText(build, others) : ""),
    };

    /// <summary>The parts of a game build's name (<see cref="RecompLaunch.Build"/>), in its order, as a player is told
    /// of them.</summary>
    private static readonly string[] BuildParts = ["the game's online part", "the game's native build", "the game program"];

    /// <summary>Two hashes of a part that differs, as they are shown: the first eight characters of each, or as many
    /// more as it takes for them to differ (the server tells each build whole: two can share their beginning).</summary>
    private static (string Mine, string Theirs) Shown(string mine, string theirs)
    {
        var length = 8;
        while (length < Math.Max(mine.Length, theirs.Length)
               && string.Equals(mine[..Math.Min(length, mine.Length)], theirs[..Math.Min(length, theirs.Length)], StringComparison.Ordinal))
        {
            length++;
        }
        return (mine[..Math.Min(length, mine.Length)], theirs[..Math.Min(length, theirs.Length)]);
    }

    /// <summary>The parts of two builds that differ, compared whole, each with its two hashes as shown.</summary>
    private static IEnumerable<(int Part, string Mine, string Theirs)> Differing(string[] ours, string[] theirs)
    {
        for (var i = 0; i < ours.Length; i++)
        {
            if (!string.Equals(ours[i], theirs[i], StringComparison.Ordinal))
            {
                var (shownMine, shownTheirs) = Shown(ours[i], theirs[i]);
                yield return (i, shownMine, shownTheirs);
            }
        }
    }

    /// <summary>What a player waiting in a lobby is told while players with another build of the game are in Quick
    /// Play (<see cref="LobbyState.OtherBuilds"/>): that those players are not matched with them, and which part of
    /// the game differs (each side's hash by its first eight characters, more where those are the same).</summary>
    /// <param name="mine">This player's game build.</param>
    public static string OtherBuildsText(string? mine, IReadOnlyList<OtherBuild> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var differences = Differences(mine, others).SelectMany(d => d.Gap switch
        {
            BuildGap.OlderLauncher => ["an older launcher that does not name its game"],
            BuildGap.NewerLauncher => ["a newer launcher than yours"],
            BuildGap.NamedDifferently => ["a game named differently by its launcher"],
            BuildGap.Line => [$"version {d.Line}"],
            _ => d.Parts.Select(p => $"{BuildParts[p.Part]} differs (yours {p.Mine}, theirs {p.Theirs})"),
        });
        return OnAnotherVersion(others) + ", not matched with you: " + string.Join("; ", differences.Distinct());
    }

    /// <summary>The short names of a build's parts, as a value's key ("native build · yours"), in
    /// <see cref="BuildParts"/>' order.</summary>
    private static readonly string[] BuildPartKeys = ["online part", "native build", "game program"];

    /// <summary><see cref="OtherBuildsText"/> as a label and its values, for a page that shows no sentence:
    /// "2 players online on another version", then for each part that differs its key and both sides'
    /// hashes ("native build · yours" 8a3a451e, "theirs" 672749d2), or what the other side is ("theirs" "older
    /// launcher").</summary>
    public static (string Label, IReadOnlyList<(string Key, string Value)> Values) OtherBuildsValues(string? mine,
        IReadOnlyList<OtherBuild> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var values = Differences(mine, others).SelectMany(d => d.Gap switch
        {
            BuildGap.OlderLauncher => [("theirs", "older launcher")],
            BuildGap.NewerLauncher => [("theirs", "newer launcher")],
            BuildGap.NamedDifferently => [("theirs", "named differently")],
            BuildGap.Line => [("theirs", $"version {d.Line}")],
            _ => d.Parts.SelectMany<(int Part, string Mine, string Theirs), (string, string)>(p =>
                [($"{BuildPartKeys[p.Part]} · yours", p.Mine), ("theirs", p.Theirs)]),
        });
        return (OnAnotherVersion(others), [.. values.Distinct()]);
    }

    /// <summary>How another build is not this player's.</summary>
    private enum BuildGap
    {
        /// <summary>A launcher from before builds were named: it names none.</summary>
        OlderLauncher,

        /// <summary>This player's launcher names no build; theirs does.</summary>
        NewerLauncher,

        /// <summary>Its name has other parts than this player's.</summary>
        NamedDifferently,

        /// <summary>The same parts, some of them other hashes (<see cref="Differing"/>).</summary>
        Parts,

        /// <summary>Another release line (<see cref="OtherBuild.Line"/>): the server keys their lobbies to it, so their
        /// version is what tells them apart, not a build's parts.</summary>
        Line,
    }

    /// <summary>The one walk over the other builds that both the sentence and the values say: for each, how it is
    /// not this player's (<paramref name="mine"/>), and for <see cref="BuildGap.Parts"/> the parts that differ.</summary>
    private static IEnumerable<(BuildGap Gap, IReadOnlyList<(int Part, string Mine, string Theirs)> Parts, string? Line)> Differences(string? mine,
        IReadOnlyList<OtherBuild> others)
    {
        var ours = mine?.Split('/') ?? [];
        foreach (var other in others)
        {
            var theirs = other.Build?.Split('/') ?? [];
            yield return other.Line is { } line ? (BuildGap.Line, [], line)
                : other.Build is null ? (BuildGap.OlderLauncher, [], null)
                : mine is null ? (BuildGap.NewerLauncher, [], null)
                : theirs.Length != ours.Length || ours.Length > BuildParts.Length ? (BuildGap.NamedDifferently, [], null)
                : (BuildGap.Parts, [.. Differing(ours, theirs)], null);
        }
    }

    /// <summary>"2 players online on another version" (as a long: Sum over int throws when the counts overflow, checked
    /// where they are read, ServerMessages).</summary>
    private static string OnAnotherVersion(IReadOnlyList<OtherBuild> others) => others.Sum(o => (long)o.Players) is var players && players == 1
        ? "1 player online on another version"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{players} players online on another version");

    /// <summary>What the player is told when left out of a forming match (<see cref="Requeued"/>); they stay in the
    /// lobby.</summary>
    public static string RequeuedText(string reason) => reason switch
    {
        "latency" => "Connection too slow for this race · watching",
        "no_relay" => "No race server · the lobby tries again",
        "relay_failed" => "Race server not started · back in the lobby",
        _ => $"Not in this race ({reason})",
    };

    /// <summary>What the player is told when taken out of Quick Play (<see cref="QuickPlayEnded"/>): the server's
    /// own words when it sent some (a server that takes no players any more says where the new build is).</summary>
    public static string RemovedText(string reason, string? message = null) => message is { Length: > 0 } ? message : reason switch
    {
        "at_capacity" => "Servers at capacity",
        _ => $"Taken out of Quick Play ({reason})",
    };

    /// <summary>What the player is told when the connection ended without their doing (<see cref="Dropped"/>).</summary>
    /// <param name="lastWord">The error the server sent last before it closed the connection (null: none).</param>
    public static string DroppedText(ErrorMessage? lastWord) => lastWord switch
    {
        null => "Lost the connection to the server",
        { Code: "rate_limited" } => "Disconnected by the server · too many messages",
        { Message.Length: > 0 } => $"Disconnected by the server · {lastWord.Message}",
        _ => "Disconnected by the server",
    };

    /// <summary>What the player is told when the server refused the visit because this launcher has a test setting
    /// on (the error <see cref="Hello.DebugNotAllowed"/>); the launcher goes back to its home screen.</summary>
    public const string DebugRefusedText = "Test settings refused";

    /// <summary>What the player is told when their game desynced and the server disconnected it from the race
    /// (<see cref="Online.Protocol.RaceDesynced"/>).</summary>
    public const string DesyncedText = "Desync detected";

    /// <summary>The player's machine when the server allows it; otherwise the allowed machine nearest to it by
    /// number (the lower of two as near), which the Garage then shows selected.</summary>
    public static int QueueMachine(int machine, ServerRules? rules) =>
        rules?.AllowedMachines is { Count: > 0 } allowed && !allowed.Contains(machine)
            ? allowed.OrderBy(m => Math.Abs((long)m - machine)).ThenBy(m => m).First() : machine;
}
