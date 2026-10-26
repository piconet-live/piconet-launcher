using System.Globalization;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// The launcher's state shared by every screen: the settings (saved on every change), the Quick Play session, LAN
/// browsing and hosting, and starting the recompiled game on a race. Plain C#, no UI types: the view models read and
/// drive it.
/// </summary>
public sealed partial class AppState(SettingsStore store, string dataFolder)
{
    public LauncherSettings Settings { get; private set; } = store.Load();

    /// <summary>This start's test settings (a launch file's, <see cref="CommandLine"/>); a player's start has none.</summary>
    public DebugSettings Debug { get; private init; } = DebugSettings.Off;

    public QuickPlaySession? Session { get; private set; }

    /// <summary>Where the launcher keeps the player's data (settings, replays, caches).</summary>
    public string DataFolder => dataFolder;

    /// <summary>The server notices this PC received, kept in the data folder (<c>notices.json</c>).</summary>
    public NoticeLog Notices { get; } = NoticeLog.Load(Path.Combine(dataFolder, "notices.json"));

    /// <summary>In Quick Play now: queueing or in a lobby, on a connection that is open. A session the player left, the
    /// server ended or the network dropped is not.</summary>
    public bool InQuickPlay => Session is { ConnectedTo: not null };

    /// <summary>The game while it runs (a race, offline play or a replay; null before the first start). The name is
    /// from the Dolphin fork's time: the views read it.</summary>
    public System.Diagnostics.Process? Fork { get; private set; }

    public bool Racing => Fork is { HasExited: false };

    /// <summary>A new live snapshot from the player's game in a Quick Play race, from its status board (on the board's
    /// thread; <see cref="Board"/>).</summary>
    public event Action<Online.Protocol.RaceSnapshot>? LiveReceived;

    private DateTimeOffset _lastReport;

    /// <summary>The game started for the last Quick Play race (not a replay's).</summary>
    private System.Diagnostics.Process? _raceGame;

    /// <summary>The last problem starting a race (the game is not installed, the servers at capacity) or with the last
    /// race (it desynced, it never started), for the screens to show; cleared when the player joins again or the next
    /// race starts.</summary>
    public string? Problem
    {
        get;
        private set
        {
            field = value;
            ProblemFolder = null;
        }
    }

    /// <summary>A folder to offer to open with <see cref="Problem"/> (the log's, for the handler of last resort's
    /// notice); null: none.</summary>
    public string? ProblemFolder { get; private set; }

    public event Action? Changed;

    /// <summary>The server's answer may mean this launcher is too old for it (<see cref="QuickPlaySession.NewerBuildHinted"/>,
    /// on the network's thread): the update service looks for a newer launcher and offers it.</summary>
    public event Action? NewerBuildHinted;

    /// <param name="dataFolder">Another data folder than the player's (tests and development: `--data`).</param>
    /// <param name="debug">The start's test settings; null: none.</param>
    /// <param name="install">Where the launcher is; null: where this one is (a test: a made-up player copy).</param>
    /// <param name="standIn">Dev mode (<c>--dev-service</c>): on the stand-in service (<see cref="StandIn"/>).</param>
    public static AppState Load(string? dataFolder = null, DebugSettings? debug = null, Install? install = null, bool standIn = false)
    {
        install ??= Install.This;
        var data = DataFolderOf(dataFolder, install);
        var store = new SettingsStore(Path.Combine(data, "settings.json"));
        var state = new AppState(store, data)
        {
            // a settings file that was not read as it stood, else an older copy's files in this one: said once,
            // where the launcher's notices are
            Problem = store.Notice ?? install.OlderCopy(),
            Debug = debug ?? DebugSettings.Off,
            Where = install,
            StandIn = standIn,
        };
        if (store.CopiesFull)
        {
            // the notice asks the player to delete old copies in the data folder: offered beside it ("Open its folder")
            state.ProblemFolder = store.Folder;
        }
        // the two folders that are the data folder's own unless the player chose another: here, wherever the file
        // was written (a data folder moved in from another copy), and never written as full paths (DataFolders)
        // and the launcher's secret, the first time (LauncherSettings.PlayerKey: the player's tag comes from it)
        // (before the first save: the file keeps the player's own server in dev mode, FileSettings)
        state._fileServer = (state.Settings.Server, state.Settings.NoServer);
        state.Update(s => DataFolders.AtHome(s, data).WithPlayerKey());
        if (standIn)
        {
            // the stand-in's address in place of the player's server while the launcher runs
            state.Update(s => s with { Server = StandInService.Address, NoServer = false });
        }
        return state;
    }

    /// <summary>Dev mode (<c>--dev-service</c>): Quick Play goes to the stand-in service in the launcher's own
    /// process (<see cref="StandInService"/>), the status page and the connection dot ask it, and a lobby can be
    /// joined without a disc or a game (races never start there). Never on in a player's start.</summary>
    public bool StandIn { get; private init; }

    /// <summary>The server's name for the window's title while the stand-in runs; null otherwise.</summary>
    public string? StandInName => StandIn ? StandInService.Name : null;

    /// <summary>How the status page and the connection dot ask the server: the stand-in's answers in dev mode;
    /// null, the network, otherwise.</summary>
    public HttpMessageHandler? ServiceAnswers() => StandIn ? StandInService.Answers() : null;

    /// <summary>The player's own server as their settings file has it, while the stand-in stands in for it.</summary>
    private (string? Server, bool NoServer) _fileServer;

    /// <summary>The settings as the file keeps them: in dev mode with the player's own server, never the stand-in's.</summary>
    private LauncherSettings FileSettings =>
        StandIn ? Settings with { Server = _fileServer.Server, NoServer = _fileServer.NoServer } : Settings;

    /// <summary>Something to tell the player that no page asked for (the handler of last resort's notice): shown
    /// where the last problem is (the left column's play control, once), until the player joins again or the next
    /// race starts.</summary>
    /// <param name="folder">A folder to offer to open with it (<see cref="ProblemFolder"/>).</param>
    public void Report(string problem, string? folder = null)
    {
        Problem = problem;
        ProblemFolder = folder;
        Changed?.Invoke();
    }

    /// <summary>An error that ended the Quick Play connection while a message was taken in
    /// (<see cref="QuickPlaySession.Fault"/>), once, with where it happened: for the launcher's log.</summary>
    public event Action<string, Exception>? Fault;

    /// <summary>Something for the launcher's log that is no error (a server's match start refused).</summary>
    public event Action<string>? Noted;

    private Exception? _fault;

    private bool _notSaved;

    private volatile bool _savesHeld;

    /// <summary>From now on no change of the settings is written to the file (the launcher is about to start again on
    /// a settings file put there by <see cref="DataMove"/>); changes last until it closes.</summary>
    public void HoldSaves() => _savesHeld = true;

    /// <summary>One change and its save at a time: the window's thread changes settings, and the network's saves the
    /// machine chosen in a lobby (<see cref="QuickPlaySession.MachineChosen"/>).</summary>
    private readonly Lock _settingsGate = new();

    public void Update(Func<LauncherSettings, LauncherSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_settingsGate)
        {
            var noServer = Settings.NoServer;
            Settings = change(Settings);
            if (noServer && !Settings.NoServer && Problem == store.Notice && !store.LeftAlone)
            {
                // the notice of settings that could not be read asked for a server, and the player has chosen one
                Problem = null;
            }
            // (another launcher's settings copied over the file, DataMove: this run's are not written over them,
            // and the launcher is started again on them)
            if (!_savesHeld)
            {
                Save();
            }
        }
        Changed?.Invoke();
    }

    /// <summary>The settings written to the file (under <see cref="_settingsGate"/>).</summary>
    private void Save()
    {
        // a file that cannot be written (the disk full, the folder gone) is said once; the launcher goes on
        var saved = store.TrySave(DataFolders.ForTheFile(FileSettings, dataFolder));
        if (!saved && !_notSaved && !store.LeftAlone)
        {
            // (a file the launcher leaves alone has its own notice, which says the same and why: SettingsStore.Notice)
            Problem = SettingsStore.NotSavedNotice;
        }
        _notSaved = !saved;
    }

    /// <summary>The join itself (<see cref="JoinAsync"/>): the session made and connected, any earlier one left.</summary>
    private async Task<QuickPlaySession> ConnectAsync(Uri server, bool custom, CancellationToken stop)
    {
        if (Session is not null)
        {
            await Session.DisposeAsync().ConfigureAwait(false);
        }
        Problem = null;
        // the racer as it is now (the garage can change while in a lobby); a machine chosen in the lobby is kept as the
        // racer's machine, where the next choice starts
        // what the game is made of (the image's and the module's hashes: half a second for the module the first time),
        // so that the coordinator matches this player only with players whose game is the same
        var recomp = Settings.Recomp;
        var build = await Task.Run(() => RecompLaunch.Build(PathsOf(recomp))).ConfigureAwait(false);
        Session = new QuickPlaySession(Settings, Path.Combine(dataFolder, "race"), StartRace, () => Settings.Racer, build,
            Debug.ReachesARace, () => Settings.InputDelay, StandIn ? StandInService.Connect : CoordinatorClient.ConnectionAsync)
        {
            Custom = custom,
        };
        Session.MachineChosen += machine => Update(s => s with { Racer = s.Racer.WithMachine(machine) });
        Session.SentHome += text =>
        {
            // out of Quick Play by the server's word: the connection has nothing more to say
            _ = LeaveAsync(text);
        };
        Session.Refused += text => Noted?.Invoke(text);
        Session.NewerBuildHinted += () => NewerBuildHinted?.Invoke();
        Session.Desynced += text =>
        {
            Problem = text;
            StopRaceGame();
            Changed?.Invoke();
        };
        var session = HeardWhenDropped(Session);
        session.Changed += () =>
        {
            if (session.Fault is { } fault && Interlocked.Exchange(ref _fault, fault) != fault)
            {
                Fault?.Invoke("while a message from the server was taken in (the connection ended, the launcher goes on)", fault);
            }
            // the server takes no launcher with a test setting on, and this one's launch file has one: nothing
            // follows such a hello, so the visit is over and the home screen says why (once: the session is left)
            if (session.LastError is { Code: Online.Protocol.Hello.DebugNotAllowed } && Session == session)
            {
                _ = LeaveAsync(QuickPlaySession.DebugRefusedText);
            }
        };
        Changed?.Invoke();
        await session.JoinAsync(server, stop).ConfigureAwait(false);   // (Session may be none already: a
        return session;                                                          // refusal that came at once has left it)
    }

    /// <summary>Leaves Quick Play: out of the lobby or the queue, and the connection closed.</summary>
    /// <param name="problem">What to tell the player, when it was not their choice.</param>
    public async Task LeaveAsync(string? problem = null)
    {
        if (Session is not { } session)
        {
            return;
        }
        Session = null;
        Problem = problem;
        Changed?.Invoke();
        try
        {
            await session.LeaveAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or IOException or InvalidOperationException
            or OperationCanceledException)
        {
            // the connection was gone already
        }
        await session.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Starts the recompiled game for offline play: a normal start to the title screen, in its own user
    /// folder (the fork's controller profile is copied there the first time); a problem string otherwise.</summary>
    /// <param name="controllerSetup">On to the game's own controller options, closed when the player leaves them.</param>
    public string? PlayOffline(bool controllerSetup = false)
    {
        if (Racing || InQuickPlay || Joining)
        {
            return Racing ? GameOpenText : InQuickPlayText;
        }
        var paths = GamePaths;
        if (RecompLaunch.Problem(paths) is { } problem)
        {
            return problem;
        }
        var debug = GameDebug.From(Debug);
        Fork = RecompLaunch.StartOffline(paths, WindowWithBoard(), Settings.UserFolder, Debug.RunnerArguments, debug, controllerSetup, Volume);
        Running(GameRun.Offline);
        Watch(Fork);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Starts the recompiled game on a race from a match file, as a match start does on the fork (tests and
    /// development until the online pages start the recompiled game); a problem string otherwise.</summary>
    /// <param name="replayFile">Where the race is recorded; null: in the replay folder, named by the time and the
    /// match (<see cref="RecompLaunch.ReplayFileFor"/>).</param>
    /// <param name="run">What the game is for Discord's status (a practice's, with its course).</param>
    /// <param name="record">False: the race is not recorded, whatever <paramref name="replayFile"/> says (the
    /// launch file names no replay, and the game writes none).</param>
    public string? PlayMatch(string matchFile, string? replayFile = null, GameRun run = GameRun.Match, int? course = null,
        bool record = true)
    {
        if (Racing)
        {
            return GameOpenText;
        }
        var paths = GamePaths;
        if (RecompLaunch.RaceProblem(paths) is { } problem)
        {
            return problem;
        }
        if (RecompLaunch.MatchFileProblem(matchFile) is { } missing)
        {
            return missing;
        }
        if (!record)
        {
            replayFile = null;
        }
        else if (replayFile is null)
        {
            // recorded by default, where the Replays page lists them, as the fork's races were
            replayFile = RecompLaunch.ReplayFileFor(Settings.ReplayFolder, matchFile, DateTimeOffset.Now);
        }
        if (replayFile is not null && Path.GetDirectoryName(Path.GetFullPath(replayFile)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }
        try
        {
            Fork = RecompLaunch.StartRace(paths, WindowWithBoard(), matchFile, replayFile, Settings.Widescreen,
                Settings.UserFolder, Debug.RunnerArguments, GameDebug.From(Debug), Volume);
        }
        catch (FormatException e)
        {
            return e.Message;
        }
        Running(run, course);
        Watch(Fork, sayError: run == GameRun.Practice);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Plays a replay on the recompiled game, as the race (the game quits at the replay's end); a problem
    /// string otherwise: the game or its online part is
    /// missing, the file is not a whole replay of this game, or it was recorded with a mod image that is not known
    /// to be the same race as this game's (<see cref="RecompLaunch.PlaybackProblem"/>).</summary>
    public string? Play(string replayFile)
    {
        var s = Settings;
        if (Racing || InQuickPlay || Joining)
        {
            return Racing ? "In a race" : InQuickPlayText;   // (a replay is a game start like any other)
        }
        var paths = PathsOf(s.Recomp);
        if (RecompLaunch.PlaybackProblem(paths, replayFile) is { } problem)
        {
            return problem;
        }
        Fork = RecompLaunch.StartPlayback(paths, WindowWithBoard(), Path.GetFullPath(replayFile), s.OtherSounds, s.Widescreen,
            s.UserFolder, Debug.RunnerArguments, GameDebug.From(Debug), Volume);
        Running(GameRun.Replay);
        Watch(Fork, sayError: true);
        Changed?.Invoke();
        return null;
    }

    /// <summary>The last removal of an empty replay (<see cref="RemoveEmptyReplay"/>), which a launcher that closes
    /// right after it waits for.</summary>
    private Task _cleanup = Task.CompletedTask;

    public void Shutdown()
    {
        PadsMade?.Dispose();
        EndControllerCheck();
        _cleanup.Wait(TimeSpan.FromSeconds(3));
        Session?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        Presence?.Dispose();
    }

    /// <summary>The race's game after the server disconnected it for a desync: the game stops on its own when the relay
    /// tells it (and closes its replay), and is asked to through its status board too, so it gets a few seconds; one
    /// still running then is ended, by its own handle (that process alone).</summary>
    private void StopRaceGame()
    {
        if (_raceGame is { } game)
        {
            _ = Task.Run(() => StopGameAsync(game, TimeSpan.FromSeconds(10)));
        }
    }

    /// <summary>A game started: the screens hear of its end, and its board goes with it.</summary>
    /// <param name="sayError">A replay's or a practice's game: an end with an exit code other than 0, which the
    /// launcher did not cause, is said (<see cref="StoppedText"/>).</param>
    private void Watch(System.Diagnostics.Process game, bool sayError = false)
    {
        var board = Board;
        game.EnableRaisingEvents = true;
        game.Exited += (_, _) =>
        {
            GameEnded(board);
            if (sayError && !EndedByLauncher(game) && ExitCodeOf(game) is int code && code != 0)
            {
                Problem = StoppedText(code);
            }
            Changed?.Invoke();
        };
    }

    /// <summary>What the player is told when a replay's or a practice's game stopped on its own with an error: the
    /// exit code as the game gave it (a Windows status such as an abort's 0xC0000409 in hexadecimal).</summary>
    public static string StoppedText(int code) =>
        "The game stopped because of an error (code " + (code is >= 0 and < 0x10000
            ? code.ToString(CultureInfo.InvariantCulture)
            : "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture)) + ").";

    /// <summary>Starts the recompiled game on a Quick Play race (the session's match start), as
    /// <see cref="PlayMatch"/> does from a match file: the race is recorded where the session said, the game shows
    /// the live race on its status board, and when it closes the player is back in the lobby. A game that is not racing
    /// <see cref="StartLimit"/> after its start is closed with a message (<see cref="WatchStartAsync"/>).</summary>
    private void StartRace(ForkRace race)
    {
        var s = Settings;
        var paths = PathsOf(s.Recomp);
        var session = Session;
        if (Racing)
        {
            // a game of this launcher is open (the player's own, a replay, an earlier race's): no second one is
            // started on a server's word. The player is back in the lobby (the relay's AI takes the seat).
            Problem = AlreadyOpenText;
            Noted?.Invoke($"the race of match {race.Start.MatchId} was not started: a game of this launcher was already running");
            _ = session?.RaceOverAsync();
        }
        else if (RecompLaunch.RaceProblem(paths) is { } problem)
        {
            Problem = problem;
            // no game will run this race: the player is back in the lobby (the relay's AI takes the seat)
            _ = session?.RaceOverAsync();
        }
        else
        {
            var window = WindowWithBoard();
            var racing = new RaceStart();
            var board = Board;
            if (board is not null)
            {
                board.Received += snapshot =>
                {
                    racing.Started = true;
                    // passed on for the lobby's waiting players when the coordinator asked, 10 times a second (the
                    // game writes every frame; watchers' maps glide between)
                    var now = DateTimeOffset.UtcNow;
                    if (now - _lastReport >= TimeSpan.FromMilliseconds(100))
                    {
                        _lastReport = now;
                        _ = session?.ReportAsync(snapshot);
                    }
                    LiveReceived?.Invoke(snapshot);
                };
            }
            var game = RecompLaunch.StartRace(paths, window, race.MatchFile, race.ReplayFile, s.Widescreen,
                s.UserFolder, Debug.RunnerArguments, GameDebug.From(Debug), Volume, race.Rejoin);
            Fork = game;
            Running(race.Spectate ? GameRun.Watch : GameRun.Race);
            _raceGame = game;
            Problem = null;
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) =>
            {
                var code = ExitCodeOf(game);
                var over = FoundTheRaceOver(code, race.Rejoin);   // (started again into a race that had ended)
                if (!racing.Started)
                {
                    // the race never ran in this game: say so unless something already did (the time limit, a
                    // desync), or the game found its race over (no error: the lobby is where the player goes), and
                    // leave no replay of a race with no step. Not for a game started again: the file is the
                    // crashed game's replay, which the game leaves as it was until it is given the race
                    Problem ??= over ? null : NotStartedText;
                    if (!race.Rejoin)
                    {
                        RemoveEmptyReplay(race.ReplayFile);
                    }
                }
                // the game is done with the race: back in the lobby for the next one; or, when it ended while the
                // race ran (closed while its seat raced, a crash, the process ended), the race is kept for Rejoin
                _ = session?.RaceOverAsync(rejoinable: KeepsTheRace(code, racing.Started, race.Rejoin));
                GameEnded(board);
                Changed?.Invoke();
            };
            _ = WatchStartAsync(game, racing, StartLimit);
        }
        Changed?.Invoke();
    }

    /// <summary>Whether a race's game has shown the race running (its first live snapshot came).</summary>
    private sealed class RaceStart
    {
        public volatile bool Started;
    }

    /// <summary>How long a race's game may take from its start to the race running. The race server gives the start
    /// signal at the latest 120 s after the match (it waits that long for a player whose game is still loading:
    /// the server's ready time-out) and a game is at the start gate after about 30 s, so a game not racing by now
    /// never will: it joined after the start and its seat went to the AI, or it cannot reach the race server.
    /// A launch file can set another (<see cref="DebugSettings.StartLimitSeconds"/>).</summary>
    public TimeSpan StartLimit => TimeSpan.FromSeconds(Debug.StartLimitSeconds > 0 ? Debug.StartLimitSeconds : 150);

    /// <summary>What the player is told when their game was closed because the race never started in it.</summary>
    public static string NoStartText(TimeSpan limit) =>
        "Race did not start · no start signal in " + ((int)limit.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";

    /// <summary>What the player is told when their game closed before the race ran in it.</summary>
    public const string AlreadyOpenText = "Race started without you: game open";

    public const string NotStartedText = "Game closed before the race";

    /// <summary>A race's game that has not shown the race running when the limit is over stands at the start gate on
    /// a dark picture for ever (nothing in the game times out there): it is asked to stop, then closed by its own
    /// handle (that process alone), and the player is told why. The game's own end (<see cref="StartRace"/>'s handler) brings the player
    /// back to the lobby.</summary>
    private async Task WatchStartAsync(System.Diagnostics.Process game, RaceStart racing, TimeSpan limit)
    {
        using var wait = new CancellationTokenSource(limit);
        try
        {
            await game.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
        }
        if (racing.Started)
        {
            return;
        }
        Problem = NoStartText(limit);
        Changed?.Invoke();
        // asked through its status board first (its log then says the launcher asked); ended by its process when it
        // does not go
        await StopGameAsync(game, StopGrace).ConfigureAwait(false);
    }

    /// <summary>The replay of a race that never ran holds no step: it is removed, so the Replays page does not list
    /// a file it can only call incomplete. The game's handle on it can outlive the game's end for a moment (a game
    /// the launcher closed): tried for two seconds, then it stays and the Replays page says what is wrong with it.</summary>
    private void RemoveEmptyReplay(string? replayFile)
    {
        if (replayFile is null)
        {
            return;
        }
        _cleanup = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (ReplayFile.Delete(replayFile))   // true when it is not there, too
                {
                    return;
                }
                await Task.Delay(200).ConfigureAwait(false);
            }
        });
    }
}
