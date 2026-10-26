namespace Launcher.Core.Pads;

/// <summary>When the launcher may read the pads: its window is in front, no game of this launcher is starting or
/// running, the check before a start is not asking its own helper, and the Controller page is not running one (its
/// lines are handed on instead: <see cref="PadReader.Heard"/>).</summary>
public sealed record PadConditions(bool Focused, bool GameRunning, bool Checking, bool PageHelper)
{
    /// <summary>Whether a helper of the reader's own may run: never while a game holds the devices
    /// (<see cref="ControllerHelper.Held"/>), nor while the window is not in front: the game, or any other program,
    /// must find the pads and the GameCube adapter free.</summary>
    public bool MayRead => Focused && !GameRunning && !Checking && !PageHelper && !ControllerHelper.Held;
}

/// <summary>
/// The pad that works the launcher's window: the game's own controller helper (<see cref="ControllerHelper"/>),
/// started while the launcher may read the pads (<see cref="PadConditions.MayRead"/>) and ended the moment it may
/// not, watching the first port whose controller is a pad (<see cref="NavPort"/>). So the launcher reads what the game
/// reads, in the player's own mapping, from a gamepad and from the GameCube adapter alike, and a game started from
/// the launcher finds every device free (<see cref="ControllerHelper.HoldForGame"/> ends this helper too). What the
/// pad does is said on the helper's own thread (<see cref="Pad"/>).
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
    private bool _disposed;
    private int _quickEnds;
    private DateTimeOffset _started;
    private IReadOnlyList<HelperPort> _ports = [];

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

    /// <summary>The watched port's controller as the game would read it now; on the helper's thread.</summary>
    public event Action<HelperPad>? Pad;

    /// <summary>The reader's helper ended or was ended: nothing is held any more.</summary>
    public event Action? Released;

    /// <summary>A helper of the reader's own is running (it holds the devices).</summary>
    public bool Holding
    {
        get
        {
            lock (_gate)
            {
                return _helper is not null;
            }
        }
    }

    /// <summary>The end of the reader's last helper: done once its program has gone (a helper of another's is
    /// started after it).</summary>
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
            return port >= 0 && port < _ports.Count && IsPad(_ports[port]);
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

    /// <summary>The conditions changed: a helper is started when the reader may read and has none, and ended at
    /// once when it may not.</summary>
    public void Update(PadConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var may = conditions.MayRead;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (_wanted && !may)
            {
                _quickEnds = 0;
            }
            _wanted = may;
            if (!may)
            {
                EndLocked();
                return;
            }
            if (_helper is not null)
            {
                return;
            }
        }
        StartAfterEnding();
    }

    /// <summary>A line of a helper that is not the reader's own (the Controller page's, while it shows): its ports
    /// count as the reader's, and so do its pad lines of any pad's port (the page watches the port it shows).</summary>
    public void Heard(ControllerMessage message)
    {
        switch (message)
        {
            case HelperPorts ports:
                lock (_gate)
                {
                    _ports = ports.Ports;
                }
                break;
            case HelperPad pad when IsPadPort(pad.Port):
                Pad?.Invoke(pad);
                break;
            default:
                break;
        }
    }

    public void Dispose()
    {
        Task ending;
        lock (_gate)
        {
            _disposed = true;
            _wanted = false;
            EndLocked();
            ending = _ending;
        }
        ending.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>A helper is started once the last one has gone (two never hold the adapter at once), if it is still
    /// wanted then.</summary>
    private void StartAfterEnding()
    {
        Task ending;
        lock (_gate)
        {
            ending = _ending;
        }
        _ = Task.Run(async () =>
        {
            await ending.ConfigureAwait(false);
            // another's helper (the Controller page's, the check before a start) that is still ending holds what it
            // held until it has gone: waited for, a few seconds at most
            for (var waited = 0; waited < 60 && ControllerHelper.AnyRunning; waited++)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            StartNow();
        });
    }

    private void StartNow()
    {
        lock (_gate)
        {
            if (!_wanted || _disposed || _helper is not null || !_ending.IsCompleted || ControllerHelper.Held)
            {
                return;
            }
        }
        // (started outside the lock: the window's thread asks it, and a program's start takes a moment)
        if (_start() is not { } helper)
        {
            return;
        }
        lock (_gate)
        {
            if (!_wanted || _disposed || _helper is not null || ControllerHelper.Held)
            {
                _ = Task.Run(helper.Dispose);   // no longer wanted while it started
                return;
            }
            _helper = helper;
            _started = DateTimeOffset.UtcNow;
            helper.Received += message => Received(helper, message);
            helper.Ended += () => Ended(helper);
        }
        helper.Listen();
        if (_askAdapter)
        {
            helper.AskAdapter();
        }
    }

    private void EndLocked()
    {
        if (_helper is not { } helper)
        {
            return;
        }
        _helper = null;
        _ports = [];
        var before = _ending;
        _ending = Task.Run(async () =>
        {
            await before.ConfigureAwait(false);
            helper.Dispose();
        });
        Released?.Invoke();
    }

    private void Received(ControllerHelper helper, ControllerMessage message)
    {
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;   // one that was ended meanwhile
            }
        }
        switch (message)
        {
            case HelperDevices devices when _firstStart?.Invoke(devices.Devices) == true:
                helper.Reload();
                break;
            case HelperPorts ports:
                int? before, after;
                lock (_gate)
                {
                    before = NavPortOf(_ports);
                    _ports = ports.Ports;
                    after = NavPortOf(_ports);
                }
                if (after != before || after is null)
                {
                    helper.Watch(after);
                }
                break;
            case HelperPad:
                Heard(message);
                break;
            default:
                break;
        }
    }

    /// <summary>The helper ended and the reader did not end it: for a game's start (<see cref="ControllerHelper.StopAll"/>)
    /// it stays away until the conditions say it may again; ended by a fault of its own it is started again after a
    /// moment, and not for ever when it ends at once each time.</summary>
    private void Ended(ControllerHelper helper)
    {
        bool again;
        lock (_gate)
        {
            if (helper != _helper)
            {
                return;
            }
            EndLocked();
            _quickEnds = DateTimeOffset.UtcNow - _started < TimeSpan.FromSeconds(5) ? _quickEnds + 1 : 0;
            again = _wanted && !ControllerHelper.Held && _quickEnds < 3;
        }
        if (again)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(1.5)).ContinueWith(_ => StartAfterEnding(), TaskScheduler.Default);
        }
    }
}
