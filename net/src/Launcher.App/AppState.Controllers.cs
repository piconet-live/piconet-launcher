using Launcher.Core;

namespace Launcher.App;

// The controllers before a game start.
public sealed partial class AppState
{
    private Task<string?>? _controllerCheck;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);   // set by Shutdown

    /// <summary>Where the game is, as the settings and the launcher's places have it.</summary>
    public RecompPaths GamePaths => PathsOf(Settings.Recomp);

    /// <summary>What the player is told when a start could not write the game's volume into the game's settings.</summary>
    public const string VolumeNotSetText = "Volume not set";

    /// <summary>The volume every start gives the game (the Settings page's slider), and the notice when the game's
    /// settings could not be written with it. The notice comes a moment after the start: a start clears the notice
    /// of the start before it as its last step.</summary>
    private StartVolume Volume => new(Settings.GameVolume, () => _ = Task.Delay(500).ContinueWith(_ => Report(VolumeNotSetText), TaskScheduler.Default));

    /// <summary>What a start from the window needs of the controllers (<see cref="ControllerStart"/>): a first start
    /// gets a working port 1, and a player whose port 1 would give the game no controller gets the sentence that
    /// says so, to be shown before the race. Null: nothing to say (also while a game runs, or with no game
    /// installed). One check runs at a time; a second asker gets the first's answer.</summary>
    public Task<string?> CheckControllerAsync()
    {
        if (_controllerCheck is { IsCompleted: false } running)
        {
            return running;
        }
        var paths = GamePaths;
        if (Racing || _closed.Task.IsCompleted || RecompLaunch.Problem(paths) is not null)
        {
            return Task.FromResult<string?>(null);
        }
        // The Controller page's helper may hold the adapter: it goes first, and the page runs none while the check
        // asks (CheckingController) nor while a game starts or runs; it hears when it may again (Changed).
        ControllerHelper.StopAll();
        var check = _controllerCheck = CheckUntilClosedAsync(paths);
        Changed?.Invoke();
        _ = AfterCheckAsync(check);
        return check;
    }

    /// <summary>What <see cref="PlayOffline"/> answers in Quick Play, where it starts no game: a match start would find
    /// that game open and give the seat to the AI (<see cref="AlreadyOpenText"/>). The pages ask or say so before
    /// they come to it; this is the look at the moment of the start, after a page waited for the check above, and a
    /// join can land in that wait.</summary>
    public const string InQuickPlayText = "In Quick Play";

    /// <summary>The check before a start is asking the helper: the Controller page runs no helper of its own.</summary>
    public bool CheckingController => _controllerCheck is { IsCompleted: false };

    /// <summary>Said a moment after the check: a game that starts on its answer is running by then, and the page
    /// starts no helper for the instant between the two.</summary>
    private async Task AfterCheckAsync(Task check)
    {
        await check.ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
        if (!_closed.Task.IsCompleted)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The check, which ends when the launcher closes: its helper is ended and nothing more is written.</summary>
    private async Task<string?> CheckUntilClosedAsync(RecompPaths paths)
    {
        using var stop = new CancellationTokenSource();
        var check = ControllerStart.PrepareAsync(paths, Settings.UserFolder, !Debug.LeaveAdapter, cancel: stop.Token);
        if (await Task.WhenAny(check, _closed.Task).ConfigureAwait(false) != check)
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
        return await check.ConfigureAwait(false);
    }

    /// <summary>The launcher closes (<see cref="Shutdown"/>): a check that is asking its helper ends now, the helper
    /// with it, and has ended when this returns; none is started after it. A RACE or a Local network Join starts
    /// the check and joins beside it: when the join failed at once, or the window was closed, the check went on by
    /// itself and wrote the game's controller settings after its launcher was gone.</summary>
    private void EndControllerCheck()
    {
        _closed.TrySetResult();
        try
        {
            _controllerCheck?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // the check's own error: nobody is left to tell
        }
    }
}
