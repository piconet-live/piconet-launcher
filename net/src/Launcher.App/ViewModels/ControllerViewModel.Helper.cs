using Avalonia.Threading;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Controller page's helper: started while the page shows and no game runs, ended when the page is left or a game
// starts, and what it says taken onto the window's thread.
public sealed partial class ControllerViewModel
{
    /// <summary>How long "press to set" waits for the player.</summary>
    private const int CaptureMs = 6000;

    private ControllerHelper? _helper;
    private HelperAdapter? _adapter;
    private bool _shown;
    private bool _helperAdapter;   // a port was the adapter's when the helper started (it sets up its devices by that)
    private int _quickEnds;
    private DateTimeOffset _started;
    private int _showing;          // each showing's number: a look that lands after the page was left is dropped
    private int _starts;           // each helper start's number: one that lands after it was called off is ended
    private bool _starting;        // a start is on its way

    /// <summary>The page shows at once, with the ports it last knew; everything that needs the disk or the game's
    /// program comes after, off the window's thread (<see cref="LookAtTheFiles"/>, <see cref="StartHelper"/>).</summary>
    public void Shown()
    {
        _shown = true;
        _quickEnds = 0;
        Enabled = !_state.Racing;
        var showing = ++_showing;
        ShowPort();
        _ = LookAtTheFiles().ContinueWith(look => OnUi(() =>
        {
            if (showing == _showing && _shown)
            {
                Take(look.Result);
                StartHelper();
            }
        }), TaskScheduler.Default);
    }

    /// <summary>The page is left: the helper ends, and every device is free again.</summary>
    public void Hidden()
    {
        _shown = false;
        _showing++;   // what the page's look finds when it lands is no one's now
        StopHelper();
    }

    /// <summary>What the page reads off the window's thread: the game's own settings file made as a game start
    /// makes it (the controller files only by a change here), the ports, the runner's gamepad profile, and whether
    /// the game could be started.</summary>
    private Task<FilesLook> LookAtTheFiles()
    {
        var (folder, userFolder, disc) = (UserFolder, _state.Settings.UserFolder, _state.Settings.DiscPath);
        return Task.Run(() =>
        {
            string? problem = null;
            try
            {
                RecompLaunch.PrepareUserFolder(folder, userFolder);
            }
            catch (Exception e) when (ControllerFiles.Failed(e))
            {
                problem = $"Settings folder not made · {e.Message}";
            }
            var paths = _state.GamePaths;
            var files = RecompLaunch.Problem(paths);
            return new FilesLook(problem, ControllerFiles.Read(folder), ControllerFiles.GamepadProfile(paths.Runner), files,
                DiscCheck.Check(disc) != DiscStatus.Ok ? "No disc image" : files);
        });
    }

    /// <summary>What <see cref="LookAtTheFiles"/> found, on the window's thread.</summary>
    private void Take(FilesLook look)
    {
        _profile = look.Profile;
        Notice = look.Problem ?? Notice;
        Took(look.Ports);
        (_startProblem, _filesProblem, _cannotStart) = ("", look.FilesProblem, look.CannotStart);
        ShowSetUpInGame();
    }

    private sealed record FilesLook(string? Problem, ControllerFileSet Ports, IReadOnlyDictionary<string, string>? Profile,
        string? FilesProblem, string? CannotStart);

    /// <summary>The page may run its helper: it shows, the window is in front (no device is held for a program the
    /// player went to; a page shown with no window that says so, as a test's, runs it), and no game of this
    /// launcher runs or is starting (the check before a start asks a helper of its own, and a game must find every
    /// device free: <see cref="ControllerHelper.Held"/>).</summary>
    private bool MayRun => _shown && !_state.Racing && !_state.CheckingController && !ControllerHelper.Held
        && (_state.WindowActive || !_state.FocusKnown);

    /// <summary>The launcher's state changed (a game started or ended, the check before a start began or is over):
    /// the helper runs exactly while it may.</summary>
    private void GameChanged()
    {
        Enabled = !_state.Racing;
        ShowSetUpInGame();
        if (MayRun && _helper is null && !_starting)
        {
            var showing = _showing;
            _ = LookAtTheFiles().ContinueWith(look => OnUi(() =>
            {
                if (showing == _showing && MayRun && _helper is null && !_starting)
                {
                    Take(look.Result);
                    StartHelper();
                }
            }), TaskScheduler.Default);
        }
        else if (!MayRun && (_helper is not null || _starting))
        {
            StopHelper();
            ShowPort();
        }
        ShowPadNote();
    }

    private void StartHelper()
    {
        StopHelper();
        if (!MayRun)
        {
            return;
        }
        // the pad reader's helper goes first (it may hold the adapter): waited for, and the start made, off the
        // window's thread; the page goes on showing meanwhile
        var start = ++_starts;
        _starting = true;
        _state.PageHelperRuns(true);
        var ending = _state.PadsEnding;
        _ = Task.Run(async () =>
        {
            await Task.WhenAny(ending, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            return _state.StartPageHelper();
        }).ContinueWith(made =>
        {
            var helper = made.IsCompletedSuccessfully ? made.Result : null;
            OnUi(() => Started(start, helper));
        }, TaskScheduler.Default);
    }

    /// <summary>The start is done: it is the page's if the page still wants it. One called off meanwhile (the page
    /// was left, a game is starting) is ended at once. Null: the runner is not there, or did not start, and the
    /// reader may run its own again.</summary>
    private void Started(int start, ControllerHelper? helper)
    {
        if (start != _starts)
        {
            if (helper is not null)
            {
                _ = Task.Run(helper.Dispose);
            }
            return;
        }
        _starting = false;
        if (helper is null || !MayRun)
        {
            if (helper is not null)
            {
                _ = Task.Run(helper.Dispose);
            }
            _state.PageHelperRuns(false);
            ShowPort();
            return;
        }
        _helper = helper;
        _started = DateTimeOffset.UtcNow;
        _helperAdapter = _ports.Any(p => p.Kind == PortKind.Adapter);
        helper.Received += message => OnUi(() => Handle(helper, message));
        helper.Ended += () => OnUi(() => HelperEnded(helper));
        helper.Listen();
        if (!_state.Debug.LeaveAdapter)
        {
            helper.AskAdapter();
        }
        _watched = null;
        WatchPort();
        ShowPort();
    }

    private int? _watched;

    /// <summary>The port the helper says as it moves: the one shown, unless it is no pad's (off, the keyboard) and a
    /// pad's port is set: that one then, so that the pad still works the window on this page.</summary>
    private void WatchPort()
    {
        var port = _state.Pads.IsPadPort(PortIndex) ? PortIndex : _state.Pads.NavPort ?? PortIndex;
        if (_helper is { } helper && port != _watched)
        {
            _watched = port;
            helper.Watch(port);
        }
    }

    private void StopHelper()
    {
        EndCapture();
        var helper = _helper;
        _helper = null;
        _adapter = null;
        Pad = null;
        var wasStarting = _starting;
        _starting = false;
        _starts++;   // a start on its way is called off (Started ends what it made)
        // ending it waits for the program to go: off the window's thread
        if (helper is not null)
        {
            _ = Task.Run(helper.Dispose);
        }
        if (helper is not null || wasStarting)
        {
            _state.PageHelperRuns(false);
        }
    }

    /// <summary>The helper ended and the page did not end it. Ended for a game's start or for the check before
    /// one, it stays away until the page hears that it may run again (<see cref="GameChanged"/>): never a helper
    /// while a game is starting or running. Ended by a fault of its own, it is started again after a moment, and
    /// not for ever when it ends at once each time.</summary>
    private void HelperEnded(ControllerHelper helper)
    {
        if (helper != _helper)
        {
            return;
        }
        StopHelper();
        ShowPort();
        if (!MayRun)
        {
            return;
        }
        _quickEnds = DateTimeOffset.UtcNow - _started < TimeSpan.FromSeconds(5) ? _quickEnds + 1 : 0;
        if (_quickEnds >= 3)
        {
            Notice = "Controller test stopped";
            return;
        }
        DispatcherTimer.RunOnce(() =>
        {
            if (MayRun && _helper is null && !_starting)
            {
                StartHelper();
            }
        }, TimeSpan.FromSeconds(1.5));
    }

    private void Handle(ControllerHelper helper, ControllerMessage message)
    {
        if (helper != _helper)
        {
            return;   // one that was ended meanwhile
        }
        if (message is HelperPorts or HelperPad && !Capturing)
        {
            _state.Pads.Heard(message);   // the pad works the window while this page holds the devices
        }
        switch (message)
        {
            case HelperPorts:
                WatchPort();
                break;
            case HelperDevices devices:
                _devices = devices.Devices;
                FirstStart();
                ShowPort();
                break;
            case HelperAdapter adapter:
                _adapter = adapter;
                ShowAdapter();
                ShowPadNote();
                break;
            case HelperPad pad when pad.Port == PortIndex:
                Pad = pad;
                ShowPadNote();
                break;
            case HelperCaptured captured:
                Captured(captured);
                break;
            default:
                break;
        }
    }

    /// <summary>A game with no controller settings at all gets its first ones here too, as before a start
    /// (<see cref="ControllerStart"/>): the page then shows a port 1 that works. Off the window's thread, in turn
    /// with the page's other writes.</summary>
    private void FirstStart()
    {
        var (first, folder) = (StandardLayouts.FirstStart(_devices, GamepadProfile()), UserFolder);
        Written(() => ControllerFiles.WriteFirstStart(folder, first) ? ControllerFiles.Read(folder) : null, read =>
        {
            if (read is not null)
            {
                Took(read);
                Reloaded();
            }
        }, e => Notice = $"Controller settings not written · {e.Message}");
    }

    /// <summary>One write of the controller files after the one before it, off the window's thread (the check
    /// before a start waits for them: <see cref="AppState.ControllerWrites"/>), and what it gave back on the
    /// window's thread.</summary>
    private void Written<T>(Func<T> write, Action<T> done, Action<Exception> failed)
    {
        _state.ControllerWrites = _state.ControllerWrites.ContinueWith(_ =>
        {
            try
            {
                var result = write();
                OnUi(() => done(result));
            }
            catch (Exception e) when (ControllerFiles.Failed(e))
            {
                OnUi(() => failed(e));
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>The files changed: the helper reads them again. It set its devices up by whether a port is the
    /// adapter's, as the game does at its start, so when that changed it is started anew.</summary>
    private void Reloaded()
    {
        if (_helper is null)
        {
            return;
        }
        if (_ports.Any(p => p.Kind == PortKind.Adapter) != _helperAdapter)
        {
            StartHelper();
        }
        else
        {
            _helper.Reload();
        }
    }

    private void ShowAdapter()
    {
        if (!IsAdapter)
        {
            (AdapterText, AdapterValue, AdapterProblem, AdapterReady) = ("", "", false, false);
            return;
        }
        var adapter = _helper is null ? null : _adapter;
        AdapterText = _helper is null ? "" : ControllerWords.Adapter(adapter);
        AdapterValue = _helper is null ? ""
            : adapter is { State: AdapterState.Found } ? ControllerWords.AdapterPort(adapter.Ports[PortIndex], PortIndex + 1)
            : ControllerWords.AdapterValue(adapter);
        AdapterReady = adapter is { State: AdapterState.Found };
        AdapterProblem = adapter is { State: AdapterState.None or AdapterState.NoDriver or AdapterState.InUse or AdapterState.Error };
    }
}
