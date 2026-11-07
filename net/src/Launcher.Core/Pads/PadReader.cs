namespace Launcher.Core.Pads;

/// <summary>Where the launcher stands for its controller helper: its window in front or not, a game of this launcher
/// starting or running, the check before a start asking a helper of its own.</summary>
public sealed record PadConditions(bool Focused, bool GameRunning, bool Checking)
{
    /// <summary>Whether the launcher's helper may hold the devices: whenever no game of this launcher holds them
    /// (<see cref="ControllerHelper.Held"/>) and the check before a start is not asking its own. In front or not: the
    /// GameCube adapter stays the launcher's while it runs, and is handed to a game at its start.</summary>
    public bool MayHold => !GameRunning && !Checking && !ControllerHelper.Held;

    /// <summary>Whether the pad works the window: only while it is in front.</summary>
    public bool MayAct => Focused;
}

/// <summary>
/// The launcher's controller helper: the game's own program (<see cref="ControllerHelper"/>), run for as long as the
/// launcher runs (<see cref="PadConditions.MayHold"/>), its window in front or not, so that it holds the GameCube adapter
/// from the launcher's start to its end. A game's start ends it (<see cref="ControllerHelper.HoldForGame"/>: released,
/// and the game opens the adapter itself), and it is started again once the game has gone. It is the window's one
/// helper: the pad that works the window reads it (the first port whose controller is a pad, <see cref="NavPort"/>; what
/// the pad does is said on the helper's thread, <see cref="Pad"/>, and only while the window is in front), and the
/// Controller page uses it while it shows (<see cref="Said"/>, <see cref="PageWatch"/>): a page that shows starts no
/// program and takes no device from anybody. So the launcher reads what the game reads, in the player's own mapping,
/// from a gamepad and from the GameCube adapter alike.
/// </summary>
public sealed class PadReader : IDisposable
{
    private readonly Func<ControllerHelper?> _start;
    private readonly bool _askAdapter;
    private readonly Func<IReadOnlyList<ControllerDevice>, bool>? _firstStart;
    private readonly Lock _gate = new();
    private ControllerHelper? _helper;
    private Task _ending = Task.CompletedTask;
    private bool _wanted;
    private bool _focused;
    private bool _disposed;
    private bool _startPending;   // a start is on its way (StartAfterEnding): one at a time
    private bool _gaveUp;
    private int _quickEnds;
    private DateTimeOffset _started;
    private IReadOnlyList<HelperPort> _ports = [];
    private HelperDevices? _devices;
    private HelperAdapter? _adapter;
    private bool? _adapterAtStart;   // whether a port was the adapter's in the helper's first ports line
    private bool _pageWatching;
    private int? _pageWatch;
    private int? _watched;
    private bool _watchSaid;

    /// <param name="start">Starts a helper (<see cref="ControllerHelper.Start"/>, or a test's double).</param>
    /// <param name="askAdapter">Look for the GameCube adapter, as the game does (a test's launcher never does).</param>
    /// <param name="firstStart">A game with no controller settings gets its first ones from the devices plugged in
    /// (true: written, and the helper reads them again), as before a start (<see cref="ControllerStart"/>).</param>
    public PadReader(Func<ControllerHelper?> start, bool askAdapter = true, Func<IReadOnlyList<ControllerDevice>, bool>? firstStart = null)
    {
        _start = start;
        _askAdapter = askAdapter;
        _firstStart = firstStart;
    }

    /// <summary>The watched port's controller as the game would read it now, while the window is in front; on the
    /// helper's thread.</summary>
    public event Action<HelperPad>? Pad;

    /// <summary>The reader's helper ended or was ended: nothing is held any more.</summary>
    public event Action? Released;

    /// <summary>Everything the helper said (<see cref="ControllerMessages"/>), with the helper that said it; on the
    /// helper's thread. The Controller page's test, devices, adapter and "press to set" come from here.</summary>
    public event Action<ControllerHelper, ControllerMessage>? Said;

    /// <summary>The helper is another: a new one started (given) or the one there was ended (null).</summary>
    public event Action<ControllerHelper?>? HelperChanged;

    /// <summary>A game with no controller settings was given its first ones (<c>firstStart</c>): the files changed.</summary>
    public event Action? FirstStartWritten;

    /// <summary>A helper of the reader's own is running (it holds the devices).</summary>
    public bool Holding => Helper is not null;

    /// <summary>The running helper; null: none (a game holds the devices, there is no runner, one is starting).</summary>
    public ControllerHelper? Helper
    {
        get
        {
            lock (_gate)
            {
                return _helper;
            }
        }
    }

    /// <summary>The helper ended by a fault of its own three times at once: none is started again until the
    /// conditions change (a game, the check before a start).</summary>
    public bool GaveUp
    {
        get
        {
            lock (_gate)
            {
                return _gaveUp;
            }
        }
    }

    /// <summary>The devices as the running helper last said them; null: not said yet.</summary>
    public HelperDevices? Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices;
            }
        }
    }

    /// <summary>The GameCube adapter as the running helper last said it; null: not known yet (while the launcher asks
    /// for it, the helper's "not asked" before the question came is no state of the adapter's).</summary>
    public HelperAdapter? Adapter
    {
        get
        {
            lock (_gate)
            {
                return _adapter;
            }
        }
    }

    /// <summary>The end of the reader's last helper: done once its program has gone.</summary>
    public Task Ending
    {
        get
        {
            lock (_gate)
            {
                return _ending;
            }
        }
    }

    /// <summary>The port the pad is read from: the first set to the GameCube adapter or to a controller that is no
    /// keyboard, one that is plugged in first; null: none (a keyboard's port never works the window: its keys are
    /// heard wherever they are typed, a name's letters too).</summary>
    public int? NavPort
    {
        get
        {
            lock (_gate)
            {
                return NavPortOf(_ports);
            }
        }
    }

    /// <summary>Whether a port, as the last ports line said it, is a pad's (<see cref="IsPad"/>).</summary>
    public bool IsPadPort(int port)
    {
        lock (_gate)
        {
            return IsPadPortLocked(port);
        }
    }

    /// <summary>A port set to the GameCube adapter, or to a controller that is no keyboard.</summary>
    public static bool IsPad(HelperPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        return port.Kind == PortSettings.AdapterNumber
            || (port.Kind == PortSettings.ControllerNumber && port.Device.Length > 0 && !IsKeyboard(port.Device));
    }

    public static int? NavPortOf(IReadOnlyList<HelperPort> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        int? any = null;
        for (var i = 0; i < ports.Count; i++)
        {
            var port = ports[i];
            if (!IsPad(port))
            {
                continue;
            }
            if (port.Connected)
            {
                return i;
            }
            any ??= i;
        }
        return any;
    }

    /// <summary>A device named as the game's settings name it (<c>BACKEND/INDEX/NAME</c>) that is a keyboard.</summary>
    public static bool IsKeyboard(string device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.EndsWith("/Keyboard Mouse", StringComparison.OrdinalIgnoreCase)
            || device.EndsWith("/Virtual core pointer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The conditions changed: a helper is started when the reader may hold the devices and has none, and
    /// ended at once when it may not. The window's focus says only whether the pad works the window.</summary>
    public void Update(PadConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var may = conditions.MayHold;
        ControllerHelper? ended = null;
        var start = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _focused = conditions.MayAct;
            if (_wanted != may)
            {
                _quickEnds = 0;   // (a game, the check: a new run of faults counts from nothing)
                _gaveUp = false;
            }
            _wanted = may;
            if (!may)
            {
                ended = EndLocked();
            }
            else if (_helper is null && !_gaveUp && !_startPending)
            {
                start = _startPending = true;
            }
        }
        Ended(ended);
        if (start)
        {
            StartAfterEnding();
        }
    }

    /// <summary>The Controller page watches a port of its own while it shows (the one it shows, or the pad's while that
    /// one is no pad's); <paramref name="watching"/> false: the page was left, and the pad's port is watched again.</summary>
    public void PageWatch(bool watching, int? port = null)
    {
        ControllerHelper? helper;
        lock (_gate)
        {
            _pageWatching = watching;
            _pageWatch = watching ? port : null;
            helper = _helper;
        }
        if (helper is not null)
        {
            Watch(helper);
        }
    }

    /// <summary>The settings on disk changed: the helper reads them again (one that set its devices up by whether a
    /// port is the adapter's is started anew when that changed).</summary>
    public void Reload() => Helper?.Reload();

    public void Dispose()
    {
        Task ending;
        ControllerHelper? ended;
        lock (_gate)
        {
            _disposed = true;
            _wanted = false;
            ended = EndLocked();
            ending = _ending;
        }
        Ended(ended);
        ending.Wait(TimeSpan.FromSeconds(5));
    }

    private bool IsPadPortLocked(int port) => port >= 0 && port < _ports.Count && IsPad(_ports[port]);

    /// <summary>A helper is started once the last one, and any other's (the check before a start's), has gone: two
    /// never hold the adapter at once. Started only if it is still wanted then.</summary>
    private void StartAfterEnding()
    {
        Task ending;
        lock (_gate)
        {
            ending = _ending;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await ending.ConfigureAwait(false);
                // another's helper that is still ending holds what it held until it has gone: waited for, a few seconds
                // at a time (ControllerHelper.Start gives none while one runs: asked again a second later)
                while (true)
                {
                    for (var waited = 0; waited < 60 && ControllerHelper.AnyRunning && StillWanted(); waited++)
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    if (StartNow() || !ControllerHelper.AnyRunning || !StillWanted())
                    {
                        break;
                    }
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _startPending = false;
                }
            }
        });
    }

    private bool StillWanted()
    {
        lock (_gate)
        {
            return _wanted && !_disposed && !_gaveUp && _helper is null;
        }
    }

    /// <summary>Starts the helper if it is still wanted; false: none was started.</summary>
    private bool StartNow()
    {
        if (!StillWanted() || ControllerHelper.Held)
        {
            return false;
        }
        // (started outside the lock: a program's start takes a moment)
        if (_start() is not { } helper)
        {
            return false;
        }
        lock (_gate)
        {
            if (!_wanted || _disposed || _helper is not null || ControllerHelper.Held)
            {
                _ = Task.Run(helper.Dispose);   // no longer wanted while it started
                return false;
            }
            _helper = helper;
            _started = DateTimeOffset.UtcNow;
            _ports = [];
            _devices = null;
            _adapter = null;
            _adapterAtStart = null;
            _watched = null;
            _watchSaid = false;
            helper.Received += message => Received(helper, message);
            helper.Ended += () => HelperEnded(helper);
        }
        helper.Listen();
        if (_askAdapter)
        {
            helper.AskAdapter();
        }
        HelperChanged?.Invoke(helper);
        return true;
    }

    /// <summary>The helper is let go, under the lock; <see cref="Ended"/> says so once the lock is left.</summary>
    private ControllerHelper? EndLocked()
    {
        if (_helper is not { } helper)
        {
            return null;
        }
        _helper = null;
        _ports = [];
        _devices = null;
        _adapter = null;
        var before = _ending;
        _ending = Task.Run(async () =>
        {
            await before.ConfigureAwait(false);
            helper.Dispose();
        });
        return helper;
    }

    private void Ended(ControllerHelper? ended)
    {
        if (ended is null)
        {
            return;
        }
        Released?.Invoke();
        HelperChanged?.Invoke(null);
    }

    /// <summary>The port the helper says as it moves: the page's while it shows, else the pad's.</summary>
    private void Watch(ControllerHelper helper)
    {
        int? port;
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;
            }
            port = _pageWatching ? _pageWatch : NavPortOf(_ports);
            if (_watchSaid && port == _watched && port is not null)
            {
                return;
            }
            _watched = port;
            _watchSaid = true;
        }
        helper.Watch(port);
    }

    private void Received(ControllerHelper helper, ControllerMessage message)
    {
        var restart = false;
        var acts = false;
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;   // one that was ended meanwhile
            }
            switch (message)
            {
                case HelperDevices devices:
                    _devices = devices;
                    break;
                case HelperPorts ports:
                    _ports = ports.Ports;
                    // the emulator sets its devices up by whether a port is the adapter's, at its start: when that
                    // changed, a new helper, as a new game would be
                    var adapter = ports.Ports.Any(p => p.Kind == PortSettings.AdapterNumber);
                    restart = _adapterAtStart is { } was && was != adapter;
                    _adapterAtStart ??= adapter;
                    break;
                case HelperAdapter { State: AdapterState.NotAsked } when _askAdapter:
                    break;   // (said before the question came: nothing is known of the adapter yet)
                case HelperAdapter said:
                    _adapter = said;
                    break;
                case HelperPad pad:
                    acts = _focused && IsPadPortLocked(pad.Port);
                    break;
                default:
                    break;
            }
        }
        if (restart)
        {
            Restart(helper);
            return;
        }
        switch (message)
        {
            case HelperDevices devices when _firstStart?.Invoke(devices.Devices) == true:
                helper.Reload();
                FirstStartWritten?.Invoke();
                break;
            case HelperPorts:
                Watch(helper);
                break;
            case HelperPad pad when acts:
                Pad?.Invoke(pad);
                break;
            default:
                break;
        }
        Said?.Invoke(helper, message);
    }

    /// <summary>The helper is ended and a new one started once it has gone.</summary>
    private void Restart(ControllerHelper helper)
    {
        ControllerHelper? ended;
        var start = false;
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;
            }
            ended = EndLocked();
            if (_wanted && !_startPending)
            {
                start = _startPending = true;
            }
        }
        Ended(ended);
        if (start)
        {
            StartAfterEnding();
        }
    }

    /// <summary>The helper ended and the reader did not end it: for a game's start (<see cref="ControllerHelper.StopAll"/>)
    /// it stays away until the conditions say it may again; ended by a fault of its own it is started again after a
    /// moment, and not for ever when it ends at once each time (<see cref="GaveUp"/>).</summary>
    private void HelperEnded(ControllerHelper helper)
    {
        ControllerHelper? ended;
        var again = false;
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;
            }
            ended = EndLocked();
            _quickEnds = DateTimeOffset.UtcNow - _started < TimeSpan.FromSeconds(5) ? _quickEnds + 1 : 0;
            _gaveUp = _quickEnds >= 3;
            if (_wanted && !ControllerHelper.Held && !_gaveUp && !_startPending)
            {
                again = _startPending = true;
            }
        }
        Ended(ended);
        if (again)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(1.5)).ContinueWith(_ => StartAfterEnding(), TaskScheduler.Default);
        }
    }
}
