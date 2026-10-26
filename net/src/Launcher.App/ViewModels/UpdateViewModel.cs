using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;

namespace Launcher.App.ViewModels;

/// <summary>
/// The launcher's update as the player sees it (<see cref="Updates"/>): a line and a button in the left column's foot
/// while there is something to do ("Update ready" and "Restart to update"), and on the Settings page's Launcher tab
/// ("Update automatically", where the update stands, and the one action that fits). The left column says nothing
/// while there is nothing to do, and nothing of an update that failed unless a server asked for a newer launcher
/// (then the line says so). Restart is refused while a game runs, with the reason in its place.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly Updates? _updates;

    /// <summary>A server's answer hinted that this launcher is too old for it (<see cref="AppState.NewerBuildHinted"/>).</summary>
    private volatile bool _wanted;

    /// <summary>The open launcher's looks (<see cref="UpdateTimes"/>): a tick a minute from <see cref="Start"/>.</summary>
    private DispatcherTimer? _clock;

    /// <summary>The last look the next periodic one was set from, and that one's time (its spread drawn once).</summary>
    private DateTimeOffset? _from;
    private DateTimeOffset _next;

    /// <summary>A game ran, and the player was in Quick Play, at the last refresh: the ends are seen there.</summary>
    private bool _wasRacing, _wasInQuickPlay;

    /// <param name="updates">The launcher's updates; null: none (a test, a mode without the window).</param>
    public UpdateViewModel(AppState state, Updates? updates)
    {
        ArgumentNullException.ThrowIfNull(state);
        (_state, _updates) = (state, updates);
        if (updates is not null)
        {
            updates.Changed += () => OnUi(Refresh);
            state.NewerBuildHinted += () =>
            {
                // the server may be for a newer launcher: looked for at once, downloaded too (without it this launcher
                // cannot race there), and offered in place of the server's words when one is found
                _wanted = true;
                _ = updates.CheckAsync(download: true);
                OnUi(Refresh);
            };
        }
        state.Changed += () => OnUi(Refresh);
        Refresh();
    }

    /// <summary>This launcher is updated by itself (it was installed by Setup).</summary>
    public bool Installed => _updates?.Installed == true;

    /// <summary>The left column shows the update's line.</summary>
    [ObservableProperty]
    public partial bool Shown { get; set; }

    /// <summary>The line: what stands ("Update ready · 0.5.1"); in the left column and on the Settings page.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "";

    /// <summary>Under it, why the button cannot be used now ("Close the game first"); empty: nothing.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    /// <summary>The one action that fits ("Restart to update", "Download update", "Check now"); empty: none.</summary>
    [ObservableProperty]
    public partial string ActionText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial bool CanAct { get; set; }

    /// <summary>The Settings page's own line: also "Up to date" and "Not checked yet", which the column leaves out.</summary>
    [ObservableProperty]
    public partial string SettingsLine { get; set; } = "";

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Act()
    {
        if (_updates is not { } updates)
        {
            return;
        }
        switch (updates.Stage)
        {
            case UpdateStage.Ready:
                if (!Relaunch.ToUpdate(updates))
                {
                    Refresh();
                }
                break;
            case UpdateStage.Available:
                _ = updates.DownloadAsync();
                break;
            case UpdateStage.NotChecked or UpdateStage.UpToDate or UpdateStage.Failed:
                _ = updates.CheckAsync(download: _state.Settings.AutoUpdate || _wanted || updates.Stage == UpdateStage.Failed);
                break;
        }
    }

    /// <summary>At the launcher's start (and when "Update automatically" is switched on): a look for a newer version,
    /// downloaded in the background, when the player's "Update automatically" is on; then the open launcher's looks
    /// every 2 hours or so (<see cref="Tick"/>).</summary>
    public void Start()
    {
        if (_updates is not { Installed: true } updates)
        {
            return;
        }
        if (_state.Settings.AutoUpdate)
        {
            _ = updates.CheckAsync(download: true);
        }
        if (_clock is null)
        {
            _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
            _clock.Tick += (_, _) => Tick();
            _clock.Start();
        }
    }

    /// <summary>The open launcher's look, on the window's thread: due 2 hours and a few minutes after the last
    /// (<see cref="UpdateTimes.Next"/>), with "Update automatically" on and no game running (one due during a game
    /// is the game's end's, <see cref="AfterPlay"/>). Quiet: a download in the background, and "Restart to update"
    /// as ever, never a restart.</summary>
    private void Tick()
    {
        if (_updates is not { } updates || updates.LastLook is not { } last)
        {
            // no look yet in this run ("Update automatically" off at the start): the first is Start's, or the player's
            return;
        }
        if (_from != last)
        {
            (_from, _next) = (last, UpdateTimes.Next(last, Random.Shared.NextDouble()));
        }
        if (DateTimeOffset.UtcNow >= _next)
        {
            Look(updates);
        }
    }

    /// <summary>A game ended or the player left Quick Play (seen at a refresh): a look when the last one is over 30
    /// minutes old.</summary>
    private void AfterPlay()
    {
        if (_updates is { } updates && UpdateTimes.AfterPlayDue(DateTimeOffset.UtcNow, updates.LastLook))
        {
            Look(updates);
        }
    }

    /// <summary>A look the launcher makes by itself: only with "Update automatically" on, never while a game runs,
    /// and not while one is at work or an update is ready.</summary>
    private void Look(Updates updates)
    {
        if (_state.Settings.AutoUpdate && !_state.Racing
            && updates.Stage is not (UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Ready or UpdateStage.NotInstalled))
        {
            _ = updates.CheckAsync(download: true);
        }
    }

    private void Refresh()
    {
        var gameOpen = _state.Racing;
        var inQuickPlay = _state.InQuickPlay;
        var ended = (_wasRacing && !gameOpen) || (_wasInQuickPlay && !inQuickPlay);
        (_wasRacing, _wasInQuickPlay) = (gameOpen, inQuickPlay);
        if (ended && _clock is not null)
        {
            AfterPlay();
        }
        if (_updates is not { Installed: true } updates)
        {
            (Shown, Line, Reason, ActionText, CanAct) = (false, "", "", "", false);
            SettingsLine = "Updates come with the installed launcher (Setup from the releases page)";
            return;
        }
        var version = updates.Version is { } v ? " · " + v : "";
        (Shown, Line, Reason, ActionText, CanAct, SettingsLine) = updates.Stage switch
        {
            UpdateStage.Ready => (true, "Update ready" + version, gameOpen ? "Close the game first" : "", "Restart to update", !gameOpen,
                "Update ready" + version + " · it goes in when the launcher next starts"),
            UpdateStage.Downloading => (true, $"Downloading update · {updates.Percent}%", "", "", false, $"Downloading update{version} · {updates.Percent}%"),
            UpdateStage.Available => (true, "Update available" + version, "", "Download update", true, "Update available" + version),
            UpdateStage.Checking => (_wanted, "Looking for an update", "", "", false, "Looking for an update"),
            UpdateStage.Failed => (_wanted, "Update not reached · check your connection", "", "Try again", true,
                "Update not reached · check your connection"),
            UpdateStage.UpToDate => (false, "", "", "Check now", true, "Up to date"),
            _ => (false, "", "", "Check now", true, "Not checked yet"),
        };
        if (_wanted && updates.Stage == UpdateStage.UpToDate)
        {
            // the server's words stand: there is no newer launcher to offer
            _wanted = false;
        }
    }
}
