using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Controller page's helper: the launcher's own (AppState.Pads, Launcher.Core/Pads/PadReader.cs), which runs for as
// long as the launcher runs and no game does, its window in front or not. The page uses it while it shows: what it
// says is taken onto the window's thread, and the port the page shows is the one it says as it moves. A page that
// shows starts no program and takes no device from anybody; while a game runs there is none, and the adapter is the
// game's ("In use by the game").
public sealed partial class ControllerViewModel
{
    /// <summary>How long "press to set" waits for the player.</summary>
    private const int CaptureMs = 6000;

    private ControllerHelper? _helper;   // the launcher's, while the page shows; null: none runs (a game, no runner)
    private HelperAdapter? _adapter;
    private bool _shown;
    private Launcher.Core.Pads.PadReader? _pads;   // the launcher's helper the page is attached to
    private int? _watched;

    /// <summary>The page shows: the settings as they are on disk, and the launcher's helper taken.</summary>
    public void Shown()
    {
        _shown = true;
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
        Attach();
        LookForTheGame();
    }

    /// <summary>The page is left: a listening ends, and the launcher's helper watches the pad's port again.</summary>
    public void Hidden()
    {
        CancelCapture();
        _shown = false;
        Detach();
    }

    private void Attach()
    {
        var pads = _state.Pads;   // (made and started at its first use: a page shown with no window, as a test's)
        if (_pads != pads)
        {
            Detach();
            _pads = pads;
            pads.Said += OnSaid;
            pads.HelperChanged += OnHelperChanged;
            pads.FirstStartWritten += OnFirstStartWritten;
        }
        Take(pads.Helper);
    }

    private void Detach()
    {
        if (_pads is not { } pads)
        {
            return;
        }
        _pads = null;
        pads.Said -= OnSaid;
        pads.HelperChanged -= OnHelperChanged;
        pads.FirstStartWritten -= OnFirstStartWritten;
        pads.PageWatch(false);
        _helper = null;
        _adapter = null;
        _watched = null;
        Pad = null;
    }

    private void OnSaid(ControllerHelper helper, ControllerMessage message) => OnUi(() => Handle(helper, message));

    private void OnHelperChanged(ControllerHelper? helper) => OnUi(() =>
    {
        if (_pads is not null)
        {
            Take(helper);
        }
    });

    /// <summary>A game with no controller settings was given its first ones (the launcher's helper did it, as a start
    /// would): the page shows them.</summary>
    private void OnFirstStartWritten() => OnUi(() =>
    {
        if (_shown)
        {
            Load();
        }
    });

    /// <summary>The launcher's helper as it is now: what it said so far (its devices, the adapter) shown, and the port
    /// the page shows watched. None (a game started, the check before a start asks its own): the test is empty until
    /// it is back.</summary>
    private void Take(ControllerHelper? helper)
    {
        if (!_shown || _pads is not { } pads)
        {
            return;
        }
        if (helper != _helper)
        {
            EndCapture();   // (a listening was the helper's that went)
        }
        _helper = helper;
        _adapter = helper is null ? null : pads.Adapter;
        if (helper is not null && pads.Devices is { } devices)
        {
            _devices = devices.Devices;
        }
        Pad = null;
        _watched = null;
        if (helper is not null)
        {
            WatchPort();
        }
        else if (pads.GaveUp)
        {
            Notice = "Controller test stopped";
        }
        ShowPort();
    }

    /// <summary>The launcher's state changed (a game started or ended, the check before a start began or is over):
    /// the page's buttons, and the adapter's words (the game's while it runs).</summary>
    private void GameChanged()
    {
        Enabled = !_state.Racing;
        ShowSetUpInGame();
        ShowAdapter();
        ShowPadNote();
    }

    /// <summary>The port the helper says as it moves: the one shown, unless it is no pad's (off, the keyboard) and a
    /// pad's port is set: that one then, so that the pad still works the window on this page.</summary>
    private void WatchPort()
    {
        if (_helper is null || _pads is not { } pads)
        {
            return;
        }
        var port = pads.IsPadPort(PortIndex) ? PortIndex : pads.NavPort ?? PortIndex;
        if (port != _watched)
        {
            _watched = port;
            pads.PageWatch(true, port);
        }
    }

    private void Handle(ControllerHelper helper, ControllerMessage message)
    {
        if (!_shown || helper != _helper)
        {
            return;   // one that was ended meanwhile, or the page was left
        }
        switch (message)
        {
            case HelperPorts:
                WatchPort();
                break;
            case HelperDevices devices:
                _devices = devices.Devices;
                ShowPort();
                break;
            case HelperAdapter:
                _adapter = _pads?.Adapter;   // (the reader's: a "not asked" before the question is no state)
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

    /// <summary>The files changed: the launcher's helper reads them again (it is started anew when whether a port is the
    /// adapter's changed: the emulator sets its devices up by that at its start).</summary>
    private void Reloaded()
    {
        if (_helper is not null)
        {
            _pads?.Reload();
        }
    }

    private void ShowAdapter()
    {
        if (!IsAdapter)
        {
            (AdapterText, AdapterValue, AdapterProblem, AdapterReady) = ("", "", false, false);
            return;
        }
        if (_state.Racing)
        {
            // the launcher's helper gave the adapter to the game at its start, and takes it back when the game has gone
            (AdapterText, AdapterValue, AdapterProblem, AdapterReady) = (ControllerWords.InUseByTheGame, "", false, false);
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
