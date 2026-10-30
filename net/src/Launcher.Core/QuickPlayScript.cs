using System.Globalization;
using System.Text.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>What a scripted Quick Play visit does (<see cref="QuickPlayScript"/>).</summary>
/// <param name="Races">How many races it stays for; a watcher: how many of the lobby's races it watches.</param>
/// <param name="StartWith">It votes to start (and for CPU racers on the empty seats) once the lobby has this many
/// players, as a player presses the two buttons.</param>
/// <param name="Course">The course it votes for when the vote offers it; otherwise, and when null, the vote's first
/// option.</param>
/// <param name="Machine">The machine it chooses; null: the one the choice starts on.</param>
/// <param name="Watch">It only waits in the lobby: no vote, no choice. It should join while the lobby's race is on
/// (everyone in a lobby when a match forms races in it).</param>
public sealed record QuickPlayScriptOptions(int Races = 1, int StartWith = 2, int? Course = null, int? Machine = null,
    bool Watch = false, QuickPlayCustom? Custom = null, string? AfterLeave = null, Action? Rejoin = null);

/// <summary>
/// Quick Play driven without a person, for tests (a launch file's <c>"quickPlay"</c>): it makes the calls the lobby
/// page's buttons make, on the same <see cref="QuickPlaySession"/> (vote to start, vote for CPU racers, vote for a
/// course, choose a machine) and nothing else. The session itself starts the game at the match start, as for a
/// player. Every step is written as one line of JSON (the time, the event, its values) for the test to read.
/// </summary>
public sealed class QuickPlayScript(QuickPlayScriptOptions options, Action<string> writeLine, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lock = new();
    private string? _status;
    private string? _lobbyLine;
    private string? _problem;
    private bool _voted;
    private string? _votedCourse;
    private string? _courseSaid;      // the match whose course drawn was logged
    private string? _machinePressed;  // the match whose machine choice was pressed (one field for both had each undo
                                      // the other: the course logged again and the machine pressed again at a telling)
    private string? _started;
    private string? _watching;
    private bool _gameRan;
    private int _races;
    private int _liveCount;
    private int _watchCount;
    private DateTimeOffset _lastLiveLine;
    private bool _green;
    private RaceSnapshot? _last;
    private bool _connected;
    private int _watchFrame = -1;
    private RaceSnapshot? _watchLast;
    private bool _browsing;
    private bool _lobbyAsked;
    private bool _teamChosen;
    private string? _hostStarted;
    private string? _offers;
    private bool _pressedAfterLeave;

    /// <summary>Why the visit ended before its races were over (the server refused the player, or the connection
    /// closed); null: it did not.</summary>
    public string? Failure { get; private set; }

    /// <summary>Completes when the visit's races are over (the player back in the lobby after the last one).</summary>
    public Task Done => _done.Task;

    /// <summary>The races this visit has finished (a watcher: watched to their end).</summary>
    public int RacesDone => _races;

    /// <summary>Call whenever the session or the game changed: logs what is new and presses what a player would.</summary>
    /// <param name="gameRunning">The race's game is running now.</param>
    /// <param name="problem">What the launcher would show as its problem (null: nothing).</param>
    /// <param name="gameProcess">The race game's process ID while it runs, for the log: a test ends, by this ID,
    /// a game its launcher left behind.</param>
    public void Step(QuickPlaySession session, bool gameRunning, string? problem, int? gameProcess = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_lock)
        {
            if (_status is null)
            {
                Log("build", new { build = session.Build });
            }
            if (session.Status != _status)
            {
                _status = session.Status;
                Log("status", new { text = _status });
            }
            if (problem != _problem)
            {
                _problem = problem;
                Log("problem", new { text = problem });
            }
            if (Failure is null && session.LastError is { Code: "bad_version" or "bad_name" or "bad_password" or "bad_build" or Hello.DebugNotAllowed or Hello.TagsTaken } error)
            {
                // a refusal the player is shown and nothing follows: the visit is over
                Fail($"{error.Code}: {error.Message}");
            }
            else if (Failure is null && problem == QuickPlaySession.DebugRefusedText)
            {
                // the same refusal, when the launcher had left the visit for it before this step looked (its home
                // screen says why; the session's last error may be a later one by then)
                Fail($"{Hello.DebugNotAllowed}: {problem}");
            }
            else if (Failure is null && _connected && session.ConnectedTo is null)
            {
                Fail(session.Status);
            }
            _connected |= session.ConnectedTo is not null;
            var lobby = session.Lobby;
            var line = lobby is null ? null : string.Create(CultureInfo.InvariantCulture,
                $"{lobby.LobbyId} {lobby.Stage} {lobby.Waiting} {string.Join(",", lobby.Players.Select(p => p.Name + (p.Racing ? "*" : "")))} votes {lobby.StartVotes}/{lobby.CpuFillVotes}");
            if (line != _lobbyLine)
            {
                _lobbyLine = line;
                Log("lobby", lobby is null ? null : new
                {
                    id = lobby.LobbyId, stage = lobby.Stage, waiting = lobby.Waiting,
                    players = lobby.Players.Select(p => new { p.Name, p.Machine, p.Racing }), startVotes = lobby.StartVotes,
                    cpuVotes = lobby.CpuFillVotes, race = lobby.Race?.MatchId,
                });
            }
            Watch(session, lobby);
            if (lobby?.Stage != LobbyState.Forming)
            {
                _voted = false;
            }
            if (!options.Watch)
            {
                Press(session, lobby, gameRunning);
            }
            Race(session, gameRunning, gameProcess);
        }
    }

    /// <summary>What the lobby page offers after a game was closed in the race, as its Rejoin and Watch show, and the
    /// press of the visit's "afterLeave".</summary>
    private void AfterLeave(QuickPlaySession session)
    {
        var offers = string.Create(CultureInfo.InvariantCulture,
            $"{session.CanRejoin} {session.CanWatchLeftRace} {session.Watching} {session.RejoinRefused}");
        if (offers != _offers)
        {
            _offers = offers;
            Log("offers", new { rejoin = session.CanRejoin, watchLeft = session.CanWatchLeftRace, watching = session.Watching, refused = session.RejoinRefused });
        }
        if (_pressedAfterLeave)
        {
            return;
        }
        if (options.AfterLeave == "rejoin" && session.CanRejoin)
        {
            _pressedAfterLeave = true;
            Log("press", new { button = "rejoin" });
            options.Rejoin?.Invoke();
        }
        else if (options.AfterLeave == "watch" && session.CanWatchLeftRace)
        {
            _pressedAfterLeave = true;
            Log("press", new { button = "watch the race left" });
            _ = session.WatchLeftRaceAsync();
        }
    }

    /// <summary>The custom lobby pages' buttons: make or join the lobby, take a team, the host's Start.</summary>
    private void PressCustom(QuickPlaySession session, LobbyState? lobby, bool gameRunning)
    {
        var plan = options.Custom!;
        if (session.ConnectedTo is null)
        {
            return;
        }
        if (!_browsing)
        {
            _browsing = true;
            _ = session.BrowseAsync(true);
        }
        if (lobby is null && !_lobbyAsked && plan.Create is { } name)
        {
            _lobbyAsked = true;
            var values = new Dictionary<string, int>(plan.Rules ?? new Dictionary<string, int>(), StringComparer.Ordinal);
            if (plan.Teams is { } teams)
            {
                values[LobbyRules.TeamsKey] = teams;
            }
            var text = plan.ModeFile is { } file ? File.ReadAllText(file) : null;
            Log("press", new { button = "create lobby", name, modeFile = plan.ModeFile, values = plan.Values, rules = values });
            _ = session.CreateLobbyAsync(name, new LobbyRules(values, null, null, null, text, plan.Values), null);
        }
        else if (lobby is null && !_lobbyAsked && plan.Join is { } wanted && session.Lobbies.FirstOrDefault(l => l.Name == wanted) is { } row)
        {
            _lobbyAsked = true;
            Log("press", new { button = "join lobby", id = row.Id, name = wanted });
            _ = session.JoinLobbyAsync(row.Id);
        }
        if (lobby is { Custom: true } && plan.Team is { } team && !_teamChosen)
        {
            _teamChosen = true;
            Log("press", new { button = "team", team });
            _ = session.ChooseTeamAsync(team);
        }
        if (lobby is { Custom: true, Stage: LobbyState.Forming } && session.IsHost && !gameRunning && session.Race is null &&
            _races < options.Races && lobby.Players.Count >= options.StartWith && _hostStarted != lobby.LobbyId + _races)
        {
            _hostStarted = lobby.LobbyId + _races;
            Log("press", new { button = "start race", players = lobby.Players.Count });
            _ = session.StartLobbyRaceAsync();
        }
    }

    /// <summary>What a player presses now, if anything.</summary>
    private void Press(QuickPlaySession session, LobbyState? lobby, bool gameRunning)
    {
        AfterLeave(session);
        if (options.Custom is not null)
        {
            PressCustom(session, lobby, gameRunning);
        }
        if (options.Custom is null && lobby is { Stage: LobbyState.Forming } && !_voted && !gameRunning && session.Race is null &&
            _races < options.Races && lobby.Players.Count >= options.StartWith && lobby.Players.Count >= lobby.VotingMinimum)
        {
            _voted = true;
            Log("press", new { button = "vote for CPU racers, vote to start", players = lobby.Players.Count });
            _ = session.VoteCpuFillAsync(true);
            _ = session.VoteStartAsync(true);
        }
        if (session.CourseVote is { Mine: null } vote && session.CourseChosen is null && _votedCourse != vote.Vote.MatchId)
        {
            _votedCourse = vote.Vote.MatchId;
            var wanted = options.Course is { } c ? vote.Vote.Options.ToList().IndexOf(c) : -1;
            var option = wanted >= 0 ? wanted : 0;
            Log("press", new { button = "course vote", match = vote.Vote.MatchId, options = vote.Vote.Options, option, course = vote.Vote.Options[option] });
            _ = session.VoteCourseAsync(option);
        }
        if (session.CourseChosen is { } chosen && _courseSaid != chosen.MatchId)
        {
            _courseSaid = chosen.MatchId;
            Log("course", new { match = chosen.MatchId, votes = chosen.Votes, winner = chosen.Winner, course = chosen.Course, name = CourseNames.Name(chosen.Course) });
        }
        if (session.Choice is { Mine: null } choice && _machinePressed != choice.Choose.MatchId)
        {
            _machinePressed = choice.Choose.MatchId;
            var machine = options.Machine is { } m && choice.Choose.Machines.Contains(m) ? m : choice.Default.Machine;
            Log("press", new { button = "machine choice", match = choice.Choose.MatchId, course = choice.Choose.Course, machine, name = Machines.Names.ElementAtOrDefault(machine) });
            _ = session.ChooseMachineAsync(machine);
        }
    }

    /// <summary>The player's own race: its start, its game, its end.</summary>
    private void Race(QuickPlaySession session, bool gameRunning, int? gameProcess)
    {
        if (session.Race is { } race && _started != race.Start.MatchId)
        {
            _started = race.Start.MatchId;
            _gameRan = false;
            _liveCount = 0;
            _green = false;
            _last = null;
            var setup = race.Start.Setup;
            Log("match", new
            {
                match = race.Start.MatchId, relay = race.Start.Relay.Id, host = race.Start.Relay.Host, port = race.Start.Relay.Port,
                seat = race.Start.Seat, course = setup.Course, name = CourseNames.Name(setup.Course), laps = setup.Laps,
                seats = setup.Seats.Count, cpu = setup.Seats.Count(s => s.Cpu), reportLive = race.Start.ReportLive,
                replay = race.ReplayFile, matchFile = race.MatchFile,
            });
        }
        if (_started is null || _started == "over")
        {
            return;
        }
        if (gameRunning && !_gameRan)
        {
            _gameRan = true;
            Log("game", new { state = "running", match = _started, pid = gameProcess });
        }
        // the race is over for this player when its game has gone (or never came) and the session has let go of it
        if (!gameRunning && session.Race is null)
        {
            Log("game", new
            {
                state = _gameRan ? "closed" : "never ran", match = _started, liveSnapshots = _liveCount, lastFrame = _last?.Frame,
                standings = _last is null ? null : Standings(_last),
            });
            _started = "over";
            if (++_races >= options.Races && !options.Watch)
            {
                _done.TrySetResult();
            }
        }
    }

    /// <summary>The lobby's race as a waiting player is sent it (the first three seats' launchers are asked to report
    /// it: a race whose first seats are simulated players sends nothing).</summary>
    private void Watch(QuickPlaySession session, LobbyState? lobby)
    {
        var racing = lobby is { Stage: LobbyState.Racing, Race: not null } && session.Race is null;
        if (racing && _watching != lobby!.Race!.MatchId)
        {
            _watching = lobby.Race.MatchId;
            _watchCount = 0;
            _watchFrame = -1;
            _watchLast = null;
            Log("watch", new { state = "waiting in the lobby while its race is on", match = _watching });
        }
        if (racing && session.Live is { } live && _watching == live.MatchId && live.Snapshot.Frame != _watchFrame)
        {
            if (_watchCount++ == 0)
            {
                Log("watch", new { state = "first live snapshot", match = _watching, frame = live.Snapshot.Frame });
            }
            _watchFrame = live.Snapshot.Frame;
            _watchLast = live.Snapshot;
        }
        if (_watching is not null && lobby?.Stage != LobbyState.Racing)
        {
            Log("watch", new
            {
                state = "the race is over", match = _watching, snapshots = _watchCount, lastFrame = _watchLast?.Frame,
                standings = _watchLast is null ? null : Standings(_watchLast),
            });
            _watching = null;
            if (options.Watch && ++_races >= options.Races)
            {
                _done.TrySetResult();
            }
        }
    }

    /// <summary>A live snapshot from the player's own game (60 a second): the first, the green light (the race
    /// timer runs), one line a second, and the last is kept for the standings.</summary>
    public void Live(RaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _last = snapshot;
            var now = _time.GetUtcNow();
            if (_liveCount++ == 0)
            {
                Log("live", new { state = "first snapshot (the race server said go)", frame = snapshot.Frame });
            }
            if (!_green && snapshot.Racers.Any(r => r.TotalMs > 0))
            {
                _green = true;
                Log("live", new { state = "green light (the race timer runs)", frame = snapshot.Frame });
            }
            if (now - _lastLiveLine >= TimeSpan.FromSeconds(5))
            {
                _lastLiveLine = now;
                var own = snapshot.Racers.FirstOrDefault(r => r.Seat == snapshot.LocalSeat);
                Log("live", new
                {
                    frame = snapshot.Frame, lap = own?.Lap, speed = own?.Speed, state = own?.State,
                    racing = snapshot.Racers.Count(r => r.State == RacerLive.Racing),
                    finished = snapshot.Racers.Count(r => r.State == RacerLive.Finished),
                    @out = snapshot.Racers.Count(r => r.State == RacerLive.Out),
                });
            }
        }
    }

    /// <summary>The standings a snapshot gives (<see cref="RaceStandings"/>), one entry a racer: seat, state, time.</summary>
    public static IReadOnlyList<string> Standings(RaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. RaceStandings.Order(snapshot.Racers).Select(r => string.Create(CultureInfo.InvariantCulture,
            $"{r.Seat}:{r.State}:{(r.State == RacerLive.Finished ? r.TotalMs : 0)}"))];
    }

    private void Fail(string why)
    {
        if (_done.Task.IsCompleted)
        {
            // the visit is over (its races done, or a failure said first): what follows is the launcher leaving
            // Quick Play at its end (Program: the session left, the connection closed), whose step can come after
            // Done on the connection's thread, and is no failure of the visit
            return;
        }
        Failure = why;
        Log("failed", new { text = why });
        _done.TrySetResult();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private void Log(string name, object? values) =>
        writeLine(JsonSerializer.Serialize(new { at = _time.GetLocalNow().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), @event = name, values }, Json));
}
