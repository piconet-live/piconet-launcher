using System.Diagnostics;

namespace Launcher.Core;

/// <summary>Where the recompiled game is: the runner (ModernGekko), the native module built for the player's disc, the
/// game's files extracted from that disc, the game's own user folder (settings, memory card, controller), and the
/// mod bundle a race needs (the image and, for now, its codes); null: races cannot be started.</summary>
/// <param name="PlayerCopy">The launcher is a player copy (<see cref="Install"/>): what is missing is said as it is
/// true for one (nothing is installed into a copy).</param>
/// <param name="NoGame">No game start may take <paramref name="GameFolder"/>, whatever it holds: it is a player
/// copy's prepared folder that is not ready (damaged, or with another online part's words in it).</param>
/// <param name="Installed">The launcher is an installed one (<see cref="Install.Installed"/>; a kind of
/// <paramref name="PlayerCopy"/>): what is missing is said as it is true for one (Setup puts it back).</param>
public sealed record RecompPaths(string Runner, string Module, string GameFolder, string UserFolder, string? Bundle = null,
    bool PlayerCopy = false, bool NoGame = false, bool Installed = false);

/// <summary>Where the recompiled game is when the settings do not say (development builds set these): null
/// leaves the launcher's own place, a <c>recomp</c> folder
/// beside the launcher (the runner, the module, <c>game</c>, and the mod bundle in <c>fzgx</c>) and a user folder in
/// the launcher's data.</summary>
public sealed record RecompSettings(string? Runner = null, string? Module = null, string? GameFolder = null, string? UserFolder = null,
    string? Bundle = null);

/// <summary>The volume a start gives the game, 0 to 100, and what is done when the game's settings could not be
/// written with it (the game starts all the same, at the volume they hold).</summary>
public sealed record StartVolume(int Level, Action? NotSet = null);

/// <summary>What one look found of the game (<see cref="RecompLaunch.Look"/>).</summary>
/// <param name="Problem">What is missing to start the game, in the player's words; null: nothing.</param>
/// <param name="RaceProblem">The same for a race, which needs the game's online part too.</param>
/// <param name="Wanted">What the first screen has to prepare from the disc: the prepared folder's state, with its
/// sentence; null: nothing.</param>
public sealed record GameLook(RecompPaths Paths, string? Problem, string? RaceProblem, PreparedStatus? Wanted);

/// <summary>
/// Starts the recompiled F-Zero GX: the game's code compiled to native code, run by
/// ModernGekko. Offline play is a normal start to the title screen with the player's controller and sound; a race is
/// booted from its match file, and the game quits when the player leaves
/// the results. Every start's settings are one launch file (<see cref="GameLaunchFile"/>), the runner's <c>--config</c>. The game runs in its own user folder, never the player's Dolphin profile or the fork's folder.
/// </summary>
public static partial class RecompLaunch
{
    /// <summary>The disc the module is built for.</summary>
    public const string ModuleFileName = "gGFZE01_recomp.dll";

    public const string RunnerFileName = "moderngekko-run.exe";

    /// <summary>The game's exit code when it was started again into a race that is over (the race server said so):
    /// nothing to rejoin, and no error; the player is back in the lobby (fzgx::EXIT_RACE_OVER).</summary>
    public const int RaceOverExitCode = 3;

    /// <summary>The game's exit code when it stopped by no stop of its own (its window closed, Escape) while the
    /// player's seat still raced: the race is kept and Rejoin offered, as after a crash (fzgx::EXIT_LEFT_RACING).
    /// Closed after finishing, a knock-out, in the results or as a spectator, the game ends with 0.</summary>
    public const int LeftRacingExitCode = 4;

    /// <summary>The paths in use: the settings' own, else the launcher's places.</summary>
    /// <param name="launcherFolder">Where the launcher is installed.</param>
    /// <param name="dataFolder">The launcher's data folder.</param>
    /// <param name="pins">The disc a prepared folder is of; null: the game's (tests prepare a made-up one).</param>
    /// <param name="playerCopy">The launcher is a player copy. Its game's files are the prepared folder's and no
    /// other: a <c>recomp\game</c> in a copy was not made by this copy (an older copy's, unzipped over or carried
    /// across), carries no record, and holds another online part's words, which the new module would run on. With
    /// no prepared folder that is ready, a copy has no game (<see cref="RecompPaths.NoGame"/>). In any other
    /// launcher (a development copy, the checks') <c>recomp\game</c> is the game when nothing is prepared.</param>
    public static RecompPaths Resolve(RecompSettings? settings, string launcherFolder, string dataFolder, DiscPins? pins = null,
        bool playerCopy = false)
    {
        var beside = Path.Combine(launcherFolder, "recomp");
        var bundle = settings?.Bundle ?? Path.Combine(beside, "fzgx");
        var game = settings?.GameFolder ?? DiscPrep.ReadyFolder(dataFolder, bundle, pins);
        return new RecompPaths(
            settings?.Runner ?? Path.Combine(beside, RunnerFileName),
            settings?.Module ?? Path.Combine(beside, ModuleFileName),
            game ?? (playerCopy ? DiscPrep.GameFolder(dataFolder, pins) : Path.Combine(beside, CarriedGame)),
            settings?.UserFolder ?? Path.Combine(dataFolder, "recomp-user"),
            bundle, playerCopy, NoGame: playerCopy && game is null);
    }

    /// <summary>The game's files beside the game's program, in a launcher that is no player copy.</summary>
    public const string CarriedGame = "game";

    /// <summary>The paths of a launcher that is where <paramref name="install"/> says.</summary>
    public static RecompPaths Resolve(RecompSettings? settings, Install install, string dataFolder, DiscPins? pins = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        return Resolve(settings, install.Folder, dataFolder, pins, install.PlayerCopy) with { Installed = install.Installed };
    }

    /// <summary>The runner's arguments, for every kind of start: where the game is, the window's title, the runner's
    /// own log, and <c>--config</c> with the game's launch file (<see cref="GameLaunchFile"/>), which holds every
    /// setting of the start: the kind (offline play, a race, a replay), the bundle, and the test settings, if any. No
    /// mods folder is searched: the module's fastest code is used only without one.</summary>
    /// <param name="title">The window's title.</param>
    /// <param name="launchFile">The game's launch file.</param>
    /// <param name="extra">More arguments of the runner's own (tests and diagnosis: <c>--mute</c>,
    /// <c>--automation-dir</c>); no setting of the game is one.</param>
    public static IReadOnlyList<string> Arguments(RecompPaths paths, string title, string launchFile, IEnumerable<string>? extra = null) =>
        [.. Common(paths, title), "--config", launchFile, .. extra ?? []];

    /// <summary>The launch file of offline play. A mod bundle of the native form (it has a patch list,
    /// <c>patches.json</c>) goes along: there the bundle's image and the patched game code are compiled into the
    /// module and run in every start, so a plain start needs the bundle too, and runs with the stock game's
    /// behaviour. The fork's kind of bundle (Gecko codes) has no part in a plain start and is not named.</summary>
    /// <param name="controllerSetup">The start opens the game's own controller options and the game closes when
    /// the player leaves them (<see cref="GameLaunchFile.Offline"/>).</param>
    public static string OfflineLaunch(RecompPaths paths, GameDebug? debug = null, GameWindow? window = null, bool controllerSetup = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var native = NativeBundle(paths);
        return GameLaunchFile.Offline(native ? paths.Bundle : null, debug, window, controllerSetup);
    }

    /// <summary>The launch file of a race from a match file: the
    /// game boots the race, records the replay, shows the race on its status board (the window's
    /// <see cref="GameWindow.Board"/>), and quits when the player leaves the results.</summary>
    /// <param name="matchFile">The match file (<see cref="MatchFile"/>), read here.</param>
    /// <param name="widescreen">The picture: the game's Normal, its own 16:9 mode or true widescreen, the last two shown at 16:9.</param>
    /// <exception cref="FormatException">The match file is not a JSON object.</exception>
    public static string RaceLaunch(RecompPaths paths, string matchFile, string? replayFile, WidescreenMode widescreen = WidescreenMode.Game,
        GameDebug? debug = null, GameWindow? window = null, bool rejoin = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return GameLaunchFile.Race(paths.Bundle ?? throw new ArgumentException("no mod bundle", nameof(paths)),
            GameLaunchFile.MatchFileText(matchFile), widescreen, replayFile, debug, window, rejoin);
    }

    /// <summary>The launch file of a replay's playback: the
    /// game boots the recorded race from the file alone and plays it as the race itself, watched through the game
    /// camera of any racer, plays the recorded inputs, compares every step with the recording, and quits at the
    /// replay's end.</summary>
    /// <param name="sounds">How loud the other racers' player-only sounds are.</param>
    public static string PlaybackLaunch(RecompPaths paths, string replayFile, OtherSounds sounds = OtherSounds.Quieter,
        WidescreenMode widescreen = WidescreenMode.Game, GameDebug? debug = null, GameWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return GameLaunchFile.Playback(paths.Bundle ?? throw new ArgumentException("no mod bundle", nameof(paths)), replayFile,
            sounds, widescreen, debug, window);
    }

    /// <summary>Where the runner writes its standard output and error (and its line for every slow second) when the
    /// launcher starts it.</summary>
    public static string LogFile(RecompPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.UserFolder, "runner.log");
    }

    private static string[] Common(RecompPaths paths, string title)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return
        [
            "--game", paths.GameFolder,
            "--module", paths.Module,
            "--user-dir", paths.UserFolder,
            "--no-mods",
            "--title", title,
            // the game is started with no console (StartInfo): the runner writes what it has to say into its own
            // folder, and keeps the 19 runs before beside it (runner.1.log the latest, to runner.19.log)
            "--fzgx-log", LogFile(paths),
        ];
    }

    /// <summary>Starts the game for offline play: a normal start to the title screen.</summary>
    /// <param name="controllerFrom">A user folder whose controller profile and port devices the game takes when it has none (the
    /// fork's).</param>
    /// <param name="window">The window's title, and what its strip shows (the launch file's window members).</param>
    /// <param name="debug">The test settings handed on to the game (a launch file's); null: none.</param>
    /// <param name="controllerSetup">Straight to the game's own controller options, and closed when the player
    /// leaves them (<see cref="GameLaunchFile.Offline"/>).</param>
    /// <param name="volume">How loud the game is (<see cref="PrepareUserFolder"/>); null: as its settings say.</param>
    public static Process StartOffline(RecompPaths paths, GameWindow window, string? controllerFrom = null, IEnumerable<string>? extra = null,
        GameDebug? debug = null, bool controllerSetup = false, StartVolume? volume = null) =>
        Start(paths, window, OfflineLaunch(paths, debug, window, controllerSetup), extra, controllerFrom, volume);

    /// <summary>Starts the game on a race from a match file (<see cref="RaceLaunch"/>).</summary>
    public static Process StartRace(RecompPaths paths, GameWindow window, string matchFile, string? replayFile, WidescreenMode widescreen = WidescreenMode.Game,
        string? controllerFrom = null, IEnumerable<string>? extra = null, GameDebug? debug = null,
        StartVolume? volume = null, bool rejoin = false) =>
        Start(paths, window, RaceLaunch(paths, matchFile, replayFile, widescreen, debug, window, rejoin), extra, controllerFrom, volume);

    /// <summary>Starts the game on a replay's playback (<see cref="PlaybackLaunch"/>).</summary>
    public static Process StartPlayback(RecompPaths paths, GameWindow window, string replayFile, OtherSounds sounds = OtherSounds.Quieter,
        WidescreenMode widescreen = WidescreenMode.Game, string? controllerFrom = null, IEnumerable<string>? extra = null,
        GameDebug? debug = null, StartVolume? volume = null) =>
        Start(paths, window, PlaybackLaunch(paths, replayFile, sounds, widescreen, debug, window), extra, controllerFrom, volume);

    /// <summary>How the runner is started, for offline play and for a race alike: directly (no shell) and with no
    /// console made for it. The runner is a console program, and started from the launcher, which has no console,
    /// Windows would open a command prompt window beside the game's. Nothing is read from the game through a pipe:
    /// its output goes to <see cref="LogFile"/>, written by the runner itself, so the game does not depend on the
    /// launcher staying alive and cannot block on a pipe nobody reads.</summary>
    public static ProcessStartInfo StartInfo(RecompPaths paths, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(paths.Runner)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(paths.Runner)) ?? ".",
        };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }
        return info;
    }

    /// <summary>The game's launch file is written into its user folder, and the runner is given its name.</summary>
    private static Process Start(RecompPaths paths, GameWindow window, string launch, IEnumerable<string>? extra, string? controllerFrom,
        StartVolume? volume)
    {
        ArgumentNullException.ThrowIfNull(window);
        // The game must find every controller free: every controller helper of this launcher is ended here, one
        // whose start was in flight too, and none is started until the game has gone.
        var hold = ControllerHelper.HoldForGame();
        try
        {
            PrepareUserFolder(paths.UserFolder, controllerFrom, volume?.Level);
            if (volume is { NotSet: { } said } && !HasVolume(paths.UserFolder, volume.Level))
            {
                said();   // the game's settings could not be written: it starts at the volume they hold
            }
            var arguments = Arguments(paths, window.Title, GameLaunchFile.Write(paths, launch), extra);
            var game = Process.Start(StartInfo(paths, arguments)) ?? throw new InvalidOperationException("the game did not start");
            hold.Until(game);
            return game;
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }
}
