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

    /// <summary>The page shows: the settings as they are on disk, and the helper started.</summary>
    public void Shown()
    {
        _shown = true;
        _quickEnds = 0;
        Enabled = !_state.Racing;
        try
        {
            // the game's own settings file is made as a game start makes it; the controller files only by a change here
            RecompLaunch.PrepareUserFolder(UserFolder, _state.Settings.UserFolder);
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            Notice = $"Settings folder not made · {e.Message}";
        }
        Load();
        StartHelper();
        LookForTheGame();
    }

    /// <summary>The page is left: the helper ends, and every device is free again.</summary>
    public void Hidden()
    {
        _shown = false;
        StopHelper();
    }

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
        if (MayRun && _helper is null)
        {
            Load();
            StartHelper();
        }
        else if (!MayRun && _helper is not null)
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
        // the pad reader's helper goes first (PageHelperRuns waits for it); null: the runner is not there, or did not
        // start, and the reader may run its own again
        _state.PageHelperRuns(true);
        if (ControllerHelper.Start(_state.GamePaths) is not { } helper)
        {
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
        // ending it waits for the program to go: off the window's thread
        if (helper is not null)
        {
            _ = Task.Run(helper.Dispose);
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
            if (MayRun && _helper is null)
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
    /// (<see cref="ControllerStart"/>): the page then shows a port 1 that works.</summary>
    private void FirstStart()
    {
        try
        {
            var first = StandardLayouts.FirstStart(_devices, GamepadProfile());
            if (ControllerFiles.WriteFirstStart(UserFolder, first))
            {
                Load();
                Reloaded();
            }
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            Notice = $"Controller settings not written · {e.Message}";
        }
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
