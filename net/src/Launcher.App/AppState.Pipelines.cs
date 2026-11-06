using Launcher.Core;

namespace Launcher.App;

/// <summary>Where the graphics cache of the look in use stands (<see cref="AppState.Pipelines"/>).</summary>
public enum PipelinePhase
{
    /// <summary>Not looked at yet, or nothing to build (the game is not set up, or Aurora does not draw).</summary>
    None,

    /// <summary>The look's cache is ready: games start.</summary>
    Ready,

    /// <summary>The runner builds it: no game starts until it is done.</summary>
    Building,

    /// <summary>The build ended without being done: games start all the same (their first races compile as before).</summary>
    Failed,
}

/// <summary>The graphics cache's state: its phase, the look, the share built, why it is built, and a failed build's why.</summary>
public sealed record PipelineStatus(PipelinePhase Phase, string? Look = null, double Fraction = 0, PipelineWhy? Why = null, string? Failure = null)
{
    public static PipelineStatus Nothing { get; } = new(PipelinePhase.None);
}

// The graphics cache of the look in use (Launcher.Core PipelineCache): looked at once the game is set up, whenever the
// look, the mods or a game's end may have changed it; built in the background (the runner, no window) while every start
// of a game waits, with its progress on the pages; the confirmation before ray tracing is first turned on.
public sealed partial class AppState
{
    /// <summary>The reason under every button that starts a game while the cache is built.</summary>
    public const string PreparingText = "Preparing graphics";

    private readonly Lock _pipelinesGate = new();
    private CancellationTokenSource? _building;
    private bool _checkAfterGame;
    private int _checks;
    private long _lastShown;

    /// <summary>The graphics cache of the look in use.</summary>
    public PipelineStatus Pipelines { get; private set; } = PipelineStatus.Nothing;

    /// <summary>Said when <see cref="Pipelines"/> changed (its progress about twice a second).</summary>
    public event Action? PipelinesChanged;

    /// <summary>Why no game starts now because of the cache (<see cref="PreparingText"/>); null: it does not stand in the way.</summary>
    public string? PipelinesBlock => Pipelines.Phase == PipelinePhase.Building ? PreparingText : null;

    /// <summary>Runs a build (the runner's, <see cref="PipelineCache.RunAsync"/>; a test's own).</summary>
    public Func<RecompPaths, string, IProgress<double>, CancellationToken, Task<PipelineBuildEnd>> PipelineRunner { get; set; } =
        PipelineCache.RunAsync;

    /// <summary>The graphics drivers' words (<see cref="PipelineCache.Drivers"/>; a test's own).</summary>
    public Func<string> PipelineDrivers { get; set; } = PipelineCache.Drivers;

    /// <summary>Whether the graphics card can ray trace (or the check has not answered yet): what a launch file's ray
    /// tracing key follows (<see cref="LauncherSettings.AuroraLaunchKeys"/>), the game's and the build's alike. A test's own.</summary>
    public Func<bool> CardRayTraces { get; set; } = () => RayTracingCard.Known?.Can != false;

    /// <summary>The look the next game starts in, as its launch file will say it: plain or ray traced (Aurora draws every
    /// start since 0.6.0, <see cref="GameWindow.Renderer"/>; null would be a start that needs no cache).</summary>
    public string? PipelineLook => PipelineLookOf(Settings);

    private string? PipelineLookOf(LauncherSettings settings) =>
        settings.AuroraLaunchKeys(CardRayTraces()).RayTracing ? PipelineCache.Rays : PipelineCache.Plain;

    /// <summary>Whether turning ray tracing on would start the ray-traced look (Aurora draws, ray tracing is offered): its
    /// cache must be ready first, else the question (<see cref="RaysReadyAsync"/>).</summary>
    public bool RaysBuildFirst => PipelineLookOf(Settings with { RayTracing = true }) == PipelineCache.Rays;

    /// <summary>Whether the ray-traced look's cache is ready, so that ray tracing can be turned on without a build (the
    /// question before it otherwise, <see cref="RaysQuestion"/>). Off the window's thread.</summary>
    public Task<bool> RaysReadyAsync()
    {
        var paths = GamePaths;
        var drivers = PipelineDrivers;
        return Task.Run(() => PipelineCache.Why(paths, PipelineCache.Want(paths, PipelineCache.Rays, drivers())) is null);
    }

    /// <summary>The question before ray tracing is turned on with its cache not built yet (Settings): Build or Cancel.</summary>
    public bool RaysQuestion { get; private set; }

    /// <summary>Asks the question (<see cref="RaysQuestion"/>).</summary>
    public void AskForRays()
    {
        RaysQuestion = true;
        PipelinesChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>The question answered: Build turns ray tracing on (and its build starts), Cancel leaves it off.</summary>
    public void AnswerRays(bool build)
    {
        RaysQuestion = false;
        if (build)
        {
            Update(s => s with { RayTracing = true });   // (the look changed: CheckPipelines)
        }
        PipelinesChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Looks at the cache of the look in use and builds it when it is not ready. Nothing while the game is not set
    /// up or Aurora does not draw; once a game of this launcher has ended when one is running, and once the lobby is left
    /// when the player is in one (a build there would keep its next race from starting). A build of another look is
    /// ended first. Returns when the look was looked at (a build goes on in the background).</summary>
    public async Task CheckPipelinesAsync()
    {
        var number = Interlocked.Increment(ref _checks);
        await ModsRead.ConfigureAwait(false);   // (the overlay the stamp is of: the mods' list read once)
        if (number != Volatile.Read(ref _checks))
        {
            return;   // a newer check is on its way
        }
        if (Racing || Session is not null)
        {
            _checkAfterGame = true;
            return;
        }
        if (PipelineLook is not { } look)
        {
            StopBuild();   // Aurora does not draw: no cache, and the game's files are not looked at for it
            Show(PipelineStatus.Nothing);
            return;
        }
        var game = await LookForTheGameOnce().ConfigureAwait(false);
        if (game.Problem is not null)
        {
            StopBuild();
            Show(PipelineStatus.Nothing);
            return;
        }
        if (Pipelines is { Phase: PipelinePhase.Building } now && now.Look == look)
        {
            return;   // (the same look is being built)
        }
        var paths = GamePaths;
        var drivers = PipelineDrivers;
        var (want, why, stale) = await Task.Run(() =>
        {
            var want = PipelineCache.Want(paths, look, drivers());
            return (want, PipelineCache.Why(paths, want), PipelineCache.Stale(paths, want));
        }).ConfigureAwait(false);
        if (number != Volatile.Read(ref _checks))
        {
            return;
        }
        if (why is null)
        {
            StopBuild();
            Show(new PipelineStatus(PipelinePhase.Ready, look));
            return;
        }
        Build(paths, want, why.Value, stale);
    }

    /// <summary>The newest look at the game's files (the first one waited for, off the window's thread).</summary>
    private Task<GameLook> LookForTheGameOnce()
    {
        lock (_lookGate)
        {
            return _look is { } look ? Task.FromResult(look) : _looking ?? StartLook();
        }
    }

    private void Build(RecompPaths paths, PipelineStamp want, PipelineWhy why, bool stale)
    {
        CancellationTokenSource cancel;
        lock (_pipelinesGate)
        {
            _building?.Cancel();
            _building = cancel = new CancellationTokenSource();
        }
        Noted?.Invoke($"the graphics cache of the {want.Look} look is built ({why})");
        Show(new PipelineStatus(PipelinePhase.Building, want.Look, 0, why));
        // the build's launch file: the offline game's, with this look's window (its Aurora settings) and the mods
        var launch = RecompLaunch.OfflineLaunch(paths, GameDebug.From(Debug), GameWindow);
        var progress = new Progress(this, want.Look, why);
        _ = Task.Run(async () =>
        {
            PipelineBuildEnd end;
            try
            {
                if (stale)
                {
                    PipelineCache.Clear(paths);   // Aurora or the drivers changed: nothing of Dawn's cache is of use
                }
                end = await PipelineRunner(paths, launch, progress, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;   // another look's build, or none, took its place
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                end = new PipelineBuildEnd(false, e.Message);
            }
            lock (_pipelinesGate)
            {
                if (_building != cancel)
                {
                    return;
                }
                _building = null;
            }
            if (end.Done)
            {
                try
                {
                    PipelineCache.Mark(paths, want);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    end = new PipelineBuildEnd(false, e.Message);
                }
            }
            Noted?.Invoke(end.Done ? $"the graphics cache of the {want.Look} look is built" : $"the graphics cache was not built: {end.Why}");
            Show(end.Done ? new PipelineStatus(PipelinePhase.Ready, want.Look)
                : new PipelineStatus(PipelinePhase.Failed, want.Look, 0, why, end.Why));
        }, CancellationToken.None);
    }

    private void StopBuild()
    {
        lock (_pipelinesGate)
        {
            _building?.Cancel();
            _building = null;
        }
    }

    private void Show(PipelineStatus status)
    {
        var changed = Pipelines != status;
        var phase = Pipelines.Phase != status.Phase;
        Pipelines = status;
        if (changed)
        {
            PipelinesChanged?.Invoke();
        }
        if (phase)
        {
            Changed?.Invoke();   // (the buttons that start a game, and their reason)
        }
    }

    /// <summary>A game of this launcher ended: a check that waited for it, and a driver updated meanwhile, are looked at.</summary>
    private void PipelinesAfterGame()
    {
        if (_checkAfterGame || PipelineLook is not null)
        {
            _checkAfterGame = false;
            _ = CheckPipelinesAsync();
        }
    }

    /// <summary>The lobby left: a check that waited for it.</summary>
    private void PipelinesAfterLobby()
    {
        if (_checkAfterGame)
        {
            _checkAfterGame = false;
            _ = CheckPipelinesAsync();
        }
    }

    /// <summary>The build's progress, kept and said about twice a second (and at its end).</summary>
    private sealed class Progress(AppState state, string look, PipelineWhy why) : IProgress<double>
    {
        public void Report(double value)
        {
            var now = Environment.TickCount64;
            if (value < 1 && now - Volatile.Read(ref state._lastShown) < 400)
            {
                return;
            }
            Volatile.Write(ref state._lastShown, now);
            lock (state._pipelinesGate)
            {
                if (state._building is null)
                {
                    return;
                }
            }
            state.Show(new PipelineStatus(PipelinePhase.Building, look, value, why));
        }
    }
}
