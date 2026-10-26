using Velopack;
using Velopack.Sources;

namespace Launcher.App;

/// <summary>Where an update stands (<see cref="Updates"/>).</summary>
public enum UpdateStage
{
    /// <summary>This launcher was not installed by Setup (a player copy, a development copy): it is not updated.</summary>
    NotInstalled,

    /// <summary>Not looked for yet in this run ("Update automatically" off, and nobody asked).</summary>
    NotChecked,

    Checking,

    /// <summary>This is the newest version.</summary>
    UpToDate,

    /// <summary>A newer version is out and not downloaded ("Update automatically" off): the player downloads it.</summary>
    Available,

    Downloading,

    /// <summary>Downloaded: it is put in at the next start, or at once with "Restart to update".</summary>
    Ready,

    /// <summary>The last look or download failed (no network, GitHub not answering): said, tried again later.</summary>
    Failed,
}

/// <summary>When the open launcher looks for an update again (<see cref="Updates"/>).</summary>
public static class UpdateTimes
{
    /// <summary>The time between looks while the launcher stays open.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(2);

    /// <summary>The most random time added to <see cref="Every"/>, so that launchers left open do not all ask at once.</summary>
    public static readonly TimeSpan Spread = TimeSpan.FromMinutes(5);

    /// <summary>A game's end or leaving Quick Play looks again only when the last look is older than this.</summary>
    public static readonly TimeSpan AfterPlay = TimeSpan.FromMinutes(30);

    /// <summary>The next periodic look after one at <paramref name="last"/> (<paramref name="spread"/>: 0 to 1, the
    /// part of <see cref="Spread"/> added).</summary>
    public static DateTimeOffset Next(DateTimeOffset last, double spread) => last + Every + (Spread * Math.Clamp(spread, 0, 1));

    /// <summary>A game ended or the player left Quick Play: look again now (no look yet in this run, or the last one
    /// over <see cref="AfterPlay"/> ago).</summary>
    public static bool AfterPlayDue(DateTimeOffset now, DateTimeOffset? last) => last is not { } at || now - at > AfterPlay;
}

/// <summary>
/// The launcher's own updates (Velopack, from the GitHub releases of the launcher's public repository). An installed
/// launcher looks for a newer version when it starts, if the player's "Update automatically" is on, and then while it
/// stays open (players leave it open), with the setting on and never while a game runs: every 2 hours with a few
/// minutes' random spread, and when a game ends or the player leaves Quick Play if the last look was over 30 minutes
/// ago (<see cref="UpdateTimes"/>, <see cref="ViewModels.UpdateViewModel"/>); and when a server's answer hints that
/// this one is too old for it
/// (<see cref="AppState.NewerBuildHinted"/>). It downloads it in the background (with the setting on, or when the
/// player asks), and puts it in at the next start (<see cref="PutInAtStart"/>, before the window opens) or when the
/// player clicks "Restart to update". It **never restarts while a game runs**: the restart is refused then
/// (<see cref="CanRestart"/>), and nothing restarts on its own.
/// <para>The feed is <see cref="Branding.UpdateFeed"/>, read as a plain web folder (Velopack's
/// <see cref="SimpleWebSource"/>): GitHub's "latest release" download link answers from its file servers, with no API
/// call, so no rate limit and no token; Velopack's GitHub source asks the GitHub API, which allows 60 requests an
/// hour from one address without a token (a LAN party behind one address would run out), and a token cannot ship in
/// a public launcher. The channel is the one the release was packed for (the release script's, "beta" for
/// 0.5.0).</para>
/// <para>Everything here happens on other threads than the window's; <see cref="Changed"/> is raised there, and the
/// pages post their refresh to the window's thread as for <see cref="AppState.Changed"/>.</para>
/// </summary>
public sealed class Updates : IDisposable
{
    private readonly UpdateManager? _manager;
    private readonly Func<bool> _gameRunning;
    private readonly Action<string>? _note;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _working;
    private UpdateInfo? _found;
    private VelopackAsset? _ready;

    /// <param name="gameRunning">Whether a game runs now (<see cref="AppState.Racing"/>): no restart then.</param>
    /// <param name="note">A line for the launcher's log (what failed, and why).</param>
    /// <param name="feed">Where the releases are; null: <see cref="Branding.UpdateFeed"/>.</param>
    public Updates(Func<bool> gameRunning, Action<string>? note = null, string? feed = null)
    {
        ArgumentNullException.ThrowIfNull(gameRunning);
        (_gameRunning, _note) = (gameRunning, note);
        try
        {
            // the locator is the one VelopackApp.Run() set at the top of Main; without it (a test, a mode that did not
            // run it) there is nothing to update
            if (Velopack.Locators.VelopackLocator.IsCurrentSet)
            {
                var manager = new UpdateManager(new SimpleWebSource(feed ?? Branding.UpdateFeed));
                if (manager.IsInstalled)
                {
                    _manager = manager;
                    _ready = manager.UpdatePendingRestart;
                    Stage = _ready is null ? UpdateStage.NotChecked : UpdateStage.Ready;
                    Version = _ready?.Version.ToString();
                    return;
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _note?.Invoke($"updates: none in this launcher ({e.GetType().Name}: {e.Message})");
        }
        Stage = UpdateStage.NotInstalled;
    }

    /// <summary>At the start of the window's launcher, before anything of it is open: an update downloaded earlier is
    /// put in, and the launcher started again on it, unless a game runs (another launcher window's, from this
    /// folder or any: the update replaces the folder the game runs from), when it waits for a later start or for
    /// "Restart to update". Velopack's own apply at the start is off (Program.Main) for that reason. Returns only
    /// when nothing was put in.</summary>
    public static void PutInAtStart(string[] args, Action<string>? note = null)
    {
        try
        {
            if (!Velopack.Locators.VelopackLocator.IsCurrentSet)
            {
                return;
            }
            var manager = new UpdateManager(new SimpleWebSource(Branding.UpdateFeed));
            if (!manager.IsInstalled || manager.UpdatePendingRestart is not { } pending || AGameRuns())
            {
                return;
            }
            manager.ApplyUpdatesAndRestart(pending, args);   // (ends this process)
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            note?.Invoke($"updates: the update was not put in at the start ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>A game's runner runs on this PC (by its program's name only: another user's process cannot be read).</summary>
    private static bool AGameRuns()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Launcher.Core.RecompLaunch.RunnerFileName));
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }

    /// <summary>Raised whenever <see cref="Stage"/>, <see cref="Percent"/> or <see cref="Version"/> change (on any thread).</summary>
    public event Action? Changed;

    public UpdateStage Stage { get; private set; }

    /// <summary>When the last look (or download) began; null: none yet in this run.</summary>
    public DateTimeOffset? LastLook { get; private set; }

    /// <summary>How much of the download is done, 0 to 100.</summary>
    public int Percent { get; private set; }

    /// <summary>The newer version's number (found, downloading or ready); null: none.</summary>
    public string? Version { get; private set; }

    /// <summary>This launcher is updated (it was installed by Setup).</summary>
    public bool Installed => _manager is not null;

    /// <summary>"Restart to update" may be done now: an update is ready and no game runs.</summary>
    public bool CanRestart => Stage == UpdateStage.Ready && !_gameRunning();

    /// <summary>Looks for a newer version, on another thread, unless a look or a download is at work; downloads it
    /// when <paramref name="download"/> says so. A launcher that is not installed does nothing.</summary>
    public Task CheckAsync(bool download)
    {
        if (_manager is not { } manager)
        {
            return Task.CompletedTask;
        }
        lock (_gate)
        {
            if (_working is { IsCompleted: false } working)
            {
                return working;
            }
            if (Stage == UpdateStage.Ready)
            {
                return Task.CompletedTask;
            }
            return _working = Task.Run(() => LookAsync(manager, download, _stop.Token));
        }
    }

    /// <summary>Downloads the version found (<see cref="UpdateStage.Available"/>), on another thread.</summary>
    public Task DownloadAsync() => CheckAsync(download: true);

    private async Task LookAsync(UpdateManager manager, bool download, CancellationToken stop)
    {
        // (a download of the version found counts too: one that failed is tried again at the next look, not at once)
        LastLook = DateTimeOffset.UtcNow;
        try
        {
            if (_found is null)
            {
                Set(UpdateStage.Checking);
                _found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                if (_found is null)
                {
                    Set(UpdateStage.UpToDate);
                    return;
                }
                Version = _found.TargetFullRelease.Version.ToString();
            }
            if (!download)
            {
                Set(UpdateStage.Available);
                return;
            }
            Percent = 0;
            Set(UpdateStage.Downloading);
            await manager.DownloadUpdatesAsync(_found, percent =>
            {
                Percent = Math.Clamp(percent, 0, 100);
                Changed?.Invoke();
            }, stop).ConfigureAwait(false);
            _ready = _found.TargetFullRelease;
            Set(UpdateStage.Ready);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // the launcher is closing
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // the network, GitHub, a full disk, a feed that cannot be read: said, and looked for again next time
            _note?.Invoke($"updates: {(_found is null ? "the look" : "the download")} failed ({e.GetType().Name}: {e.Message})");
            Set(UpdateStage.Failed);
        }
    }

    /// <summary>Hands the ready update to Velopack's updater, which waits for this launcher to close, puts the new
    /// version in and starts it. The caller then closes the launcher as the player's own close does. False, and
    /// nothing done: no update is ready, or a game runs.</summary>
    public bool RestartToUpdate()
    {
        if (_manager is not { } manager || _ready is not { } ready || !CanRestart)
        {
            return false;
        }
        try
        {
            manager.WaitExitThenApplyUpdates(ready, silent: false, restart: true);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _note?.Invoke($"updates: the updater did not start ({e.GetType().Name}: {e.Message})");
            Set(UpdateStage.Failed);
            return false;
        }
    }

    private void Set(UpdateStage stage)
    {
        Stage = stage;
        Changed?.Invoke();
    }

    /// <summary>A look or download at work is stopped (the launcher is closing).</summary>
    public void Dispose() => _stop.Cancel();
}
