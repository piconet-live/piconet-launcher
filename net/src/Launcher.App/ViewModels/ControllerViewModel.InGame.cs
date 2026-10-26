using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Controller page's "Set up in the game": F-Zero GX started straight on its own controller options (calibration,
// button settings, rumble), where a player sets what this page cannot; the game closes when the player leaves them.
// The start is offline play's (AppState.PlayOffline), with the game's "controllerSetup" in its launch file. It can be
// pressed whenever "Play F-Zero GX" could start the game at once: not while a game runs, not in Quick Play (the left
// column's button asks there; a race could start meanwhile), and not without a disc image or the game's files.
// The button shows the launcher's state as it changes, the Quick Play session's own changes included (a connection
// made or lost is said by the session alone); and a press never trusts what the button shows: it reads the state
// itself, when it is pressed and again after it waited for the controllers' check.
public sealed partial class ControllerViewModel
{
    /// <summary>The line under the button while it can be pressed: none (the button
    /// says what it does). Under an off button the line is the reason, as a label.</summary>
    public const string SetUpInGameWhat = "";

    private bool _settingUp;
    private bool _openedHere;      // the game that runs was started by the button
    private string? _cannotStart;  // no disc image, or the game's files are not there (looked at when the page shows)
    private string _startProblem = "";

    /// <summary>What the game's files lack for a start (<see cref="RecompLaunch.Problem"/>), which the left column says.</summary>
    private string? _filesProblem;
    private QuickPlaySession? _followed;   // the session whose changes the button hears

    /// <summary>"Set up in the game" can be pressed.</summary>
    [ObservableProperty]
    public partial bool CanSetUpInGame { get; set; } = true;

    /// <summary>The line under the button: what it does, or why it cannot be pressed now.</summary>
    [ObservableProperty]
    public partial string SetUpInGameNote { get; set; } = SetUpInGameWhat;

    /// <summary>The controllers first, as before offline play (a first start gets a working port 1), then the game on
    /// its controller options. Whether it may start is read from the launcher's state at the press and again after
    /// the controllers' check, never from <see cref="CanSetUpInGame"/>: a join can land between what the button
    /// last showed and the press, and while the check is waited for.</summary>
    [RelayCommand]
    private async Task SetUpInGameAsync()
    {
        if (_settingUp || WhyNot() is not null)
        {
            ShowSetUpInGame();
            return;
        }
        _settingUp = true;
        try
        {
            CancelCapture();
            var controller = await _state.CheckControllerAsync().ConfigureAwait(true);
            if (WhyNot() is null)
            {
                _startProblem = _state.PlayOffline(controllerSetup: true) ?? "";
                _openedHere = _startProblem.Length == 0;
                if (_openedHere && controller is not null)
                {
                    _state.Report(controller);
                }
            }
        }
        finally
        {
            _settingUp = false;
        }
        ShowSetUpInGame();
    }

    /// <summary>The page shows: whether the game could be started at all (a disc image, the game's files).</summary>
    private void LookForTheGame()
    {
        _startProblem = "";
        _filesProblem = RecompLaunch.Problem(_state.GamePaths);
        _cannotStart = DiscCheck.Check(_state.Settings.DiscPath) != DiscStatus.Ok ? "No disc image" : _filesProblem;
        ShowSetUpInGame();
    }

    /// <summary>Why the game cannot be started on its controller options at this moment, from the launcher's state
    /// as it stands; null: it can.</summary>
    private string? WhyNot()
    {
        _openedHere = _openedHere && _state.Racing;
        return _state.Racing
                ? _openedHere ? "Open on the controller options"
                  : _state.Session is { Racing: true } ? "In a race" : AppState.GameOpenText
            : _state.InQuickPlay || _state.Joining ? "In Quick Play"
            : _cannotStart;
    }

    private void ShowSetUpInGame()
    {
        FollowTheSession();
        var why = WhyNot();
        CanSetUpInGame = why is null;
        // (what the left column says already, a game running or the game's files, is not said again under the button)
        SetUpInGameNote = why == AppState.GameOpenText || why == "In a race" || why is not null && why == _filesProblem ? ""
            : why ?? (_startProblem.Length > 0 && _startProblem != _filesProblem ? _startProblem : SetUpInGameWhat);
    }

    /// <summary>The button hears the Quick Play session too. The launcher's own word of a join
    /// (<see cref="AppState.Changed"/>) comes before the connection is made, and the connection made, a race begun
    /// or over and a connection lost are said by the session alone: heard only from the launcher, the button would
    /// stay on in Quick Play, and off after a lost connection, until the page is shown again. The state is read
    /// after the session is heard (<see cref="ShowSetUpInGame"/>), so a change in between is not missed.</summary>
    private void FollowTheSession()
    {
        if (_state.Session is { } session && session != _followed)
        {
            _followed = session;
            session.Changed += () => OnUi(ShowSetUpInGame);
        }
    }
}
