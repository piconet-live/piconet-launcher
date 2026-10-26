using Launcher.Core;

namespace Launcher.App;

// Where the launcher keeps its data and finds the game; whether a start has the game's files, and their
// preparation from the player's disc.
public sealed partial class AppState
{
    /// <summary>What a preparation is told besides its paths. A test sets another disc's pins (a made-up image's)
    /// and the free space; the launcher's own stay as they are.</summary>
    public DiscPrepOptions Preparation { get; set; } = new();

    /// <summary>A test's eye on a preparation: every report, on the preparation's own thread, after the screen
    /// got it (a test holds a preparation still in the middle of its copy with it). Null: nobody looks.</summary>
    public IProgress<DiscPrepProgress>? PreparationWatch { get; set; }

    /// <summary>Where the game is for these settings, as the launcher's places have it: in this launcher's folder
    /// (<see cref="Install"/>: a player copy's is the one above its libraries).</summary>
    private RecompPaths PathsOf(RecompSettings? recomp) => RecompLaunch.Resolve(recomp, Where, dataFolder, Preparation.Pins);

    /// <summary>Where this launcher is (a test's: a made-up player copy).</summary>
    public Install Where { get; private init; } = Install.This;

    /// <summary>The launcher's data folder: the one given (<c>--data</c>), else a player copy's own
    /// (<see cref="Install.DataFolder"/>: everything a copy writes stays in the copy), else the player's.</summary>
    public static string DataFolderOf(string? given) => DataFolderOf(given, Install.This);

    /// <param name="install">Where the launcher is (a test's: another than this one's).</param>
    public static string DataFolderOf(string? given, Install install)
    {
        ArgumentNullException.ThrowIfNull(install);
        return given ?? install.DataFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Branding.DataFolder);
    }

    // What a game start has is looked at on another thread: a look reads the state of the prepared game, which is
    // thousands of files, and brings a stale one up to date. The window's thread only ever waits for the first
    // answer, which the launcher's start asks for before it loads its look from the disc (App.Pages), so that the
    // two run side by side.
    private readonly Lock _lookGate = new();
    private Task<GameLook>? _looking;      // the newest look
    private long _lookNumber;              // its number: an older look's answer is not kept over a newer one's
    private long _lookStarted;             // when it started (Environment.TickCount64)
    private volatile GameLook? _look;      // the newest answer

    /// <summary>What the newest look found (<see cref="RecompLaunch.Look"/>): what a start lacks, and what the
    /// first screen has to prepare. The first read waits for the look at work, or starts one.</summary>
    public GameLook Game
    {
        get
        {
            if (_look is { } look)
            {
                return look;
            }
            Task<GameLook> first;
            lock (_lookGate)
            {
                first = _looking ?? StartLook();
            }
            return first.GetAwaiter().GetResult();   // (the look's thread needs nothing of this one)
        }
    }

    /// <summary>What is missing to start the game, in the player's words; null: nothing. The newest look's answer:
    /// the buttons that start a game show it, and a start itself looks again.</summary>
    public string? GameProblem => Game.Problem;

    /// <summary>The same for a race, which needs the game's online part too.</summary>
    public string? RaceProblem => Game.RaceProblem;

    /// <summary>Looks again, on another thread, at what a game start has: at the launcher's start, at CONTINUE on
    /// the first screen, after a preparation, after a start that found something missing. When the answer is in
    /// and what a start lacks has changed, the pages hear of it (<see cref="Changed"/>).</summary>
    public Task<GameLook> LookForTheGame()
    {
        lock (_lookGate)
        {
            return StartLook();
        }
    }

    /// <summary>The same while the buttons that start a game are off, asked whenever they are shown again: at most
    /// one look every two seconds, and none while one is at work. A game that became startable meanwhile (another
    /// window of the launcher prepared it) turns them on.</summary>
    public void LookAgainSoon()
    {
        lock (_lookGate)
        {
            if (_looking is { IsCompleted: true } && Environment.TickCount64 - _lookStarted >= 2000)
            {
                StartLook();
            }
        }
    }

    /// <summary>Under <see cref="_lookGate"/>.</summary>
    private Task<GameLook> StartLook()
    {
        var (recomp, pins, number) = (Settings.Recomp, Preparation.Pins, ++_lookNumber);
        _lookStarted = Environment.TickCount64;
        return _looking = Task.Run(() =>
        {
            var look = RecompLaunch.Look(recomp, Where.Folder, dataFolder, pins, Where.PlayerCopy, Where.Installed);
            bool changed;
            lock (_lookGate)
            {
                if (number != _lookNumber)
                {
                    return look;   // a newer look is at work: its answer is the one the pages get
                }
                changed = _look is { } before && (before.Problem != look.Problem || before.RaceProblem != look.RaceProblem);
                _look = look;
            }
            if (changed)
            {
                Changed?.Invoke();
            }
            return look;
        });
    }

    /// <summary>The game's files prepared from a disc image into the data folder, for the mod bundle a game start
    /// would use (<see cref="DiscPrep.Prepare"/>, on another thread: every ending is a result). When it returns,
    /// what a game start has was looked at again, so the buttons that start a game are right.</summary>
    public async Task<DiscPrepResult> PrepareDiscAsync(string image, IProgress<DiscPrepProgress>? progress, CancellationToken cancel)
    {
        var (recomp, options, watch) = (Settings.Recomp, Preparation, PreparationWatch);
        var reports = watch is null ? progress : new Both(progress, watch);
        var result = await Task.Run(() => DiscPrep.Prepare(image, dataFolder, PathsOf(recomp).Bundle, reports, options, cancel),
            CancellationToken.None).ConfigureAwait(false);
        await LookForTheGame().ConfigureAwait(false);
        return result;
    }

    /// <summary>Takes a notice back that is over (a failed preparation's, once the game has its files), if it is
    /// still the one that shows.</summary>
    public void Unsay(string notice)
    {
        if (Problem == notice)
        {
            Problem = null;
            Changed?.Invoke();
        }
    }

    private sealed class Both(IProgress<DiscPrepProgress>? first, IProgress<DiscPrepProgress> second) : IProgress<DiscPrepProgress>
    {
        public void Report(DiscPrepProgress value)
        {
            first?.Report(value);
            second.Report(value);
        }
    }
}
