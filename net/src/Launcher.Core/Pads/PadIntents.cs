namespace Launcher.Core.Pads;

/// <summary>A direction the pad moves the focus in.</summary>
public enum PadMove
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>The pad's buttons the launcher acts on, by their GameCube names.</summary>
public enum PadButton
{
    A,
    B,
    Start,
    L,
    R,
    Z,
}

/// <summary>What the player did with the pad, for the window to act on.</summary>
public abstract record PadIntent;

/// <summary>A move of the focus: a press (<see cref="Repeat"/> false) or a repeat of a direction held.</summary>
public sealed record PadMoved(PadMove Move, bool Repeat) : PadIntent;

/// <summary>A button pressed (its edge: a button held does not repeat).</summary>
public sealed record PadPressed(PadButton Button) : PadIntent;

/// <summary>The C-stick held up or down: scroll by so many pixels (up is negative).</summary>
public sealed record PadScrolled(double By) : PadIntent;

/// <summary>
/// The GameCube controller as the game reads it (<see cref="HelperPad"/>) turned into the window's intents: a
/// direction from the D-pad or the main stick moves the focus once when pressed, and again while held, slowly at
/// first and faster the longer it is held (<see cref="RepeatAfter"/>); a button acts once when pressed; the C-stick
/// scrolls. The times are the caller's (a steady clock), so that the repeat is tested without waiting.
/// </summary>
public sealed class PadIntents
{
    /// <summary>The main stick counts as a direction beyond this much from its rest (of about 100 to its gate), and
    /// stops counting below <see cref="StickRelease"/>: the gap keeps a stick near the line from flickering.</summary>
    public const int StickEngage = 48, StickRelease = 32;

    /// <summary>The C-stick scrolls beyond this much from its rest.</summary>
    public const int ScrollDeadZone = 24;

    /// <summary>The C-stick at its gate scrolls this many pixels a second.</summary>
    public const double ScrollSpeed = 1400;

    private HelperPad? _last;
    private PadMove? _held;
    private TimeSpan _next;
    private int _repeats;
    private TimeSpan _lastTick;

    /// <summary>A direction or the C-stick is held: the caller ticks (<see cref="Tick"/>) until it is let go.</summary>
    public bool Holding => _held is not null || ScrollOf(_last) != 0;

    /// <summary>How long a held direction waits before its first repeat, and then between repeats: 400 ms, then from
    /// 200 ms down by an easing curve to 55 ms (each 82% of the one before), so that a short hold steps once or twice
    /// and a long one runs through a list.</summary>
    public static TimeSpan RepeatAfter(int repeats) => repeats <= 0
        ? TimeSpan.FromMilliseconds(400)
        : TimeSpan.FromMilliseconds(Math.Max(55, 200 * Math.Pow(0.82, repeats - 1)));

    /// <summary>The controller's new reading (the helper says one only when it changed).</summary>
    public IReadOnlyList<PadIntent> Read(HelperPad pad, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(pad);
        var intents = new List<PadIntent>();
        var before = _last;
        _last = pad;
        if (!pad.Connected)
        {
            _held = null;
            return intents;
        }
        var direction = DirectionOf(pad, _held);
        if (direction != _held)
        {
            _held = direction;
            _repeats = 0;
            if (direction is { } move)
            {
                intents.Add(new PadMoved(move, Repeat: false));
                _next = now + RepeatAfter(0);
            }
        }
        foreach (var button in Enum.GetValues<PadButton>())
        {
            var name = button.ToString();
            if (pad.Buttons.Contains(name) && before?.Buttons.Contains(name) != true)
            {
                intents.Add(new PadPressed(button));
            }
        }
        if (ScrollOf(before) == 0)
        {
            _lastTick = now;
        }
        return intents;
    }

    /// <summary>Time passed: a held direction's repeats that are due, and the C-stick's scroll since the last tick.</summary>
    public IReadOnlyList<PadIntent> Tick(TimeSpan now)
    {
        var intents = new List<PadIntent>();
        while (_held is { } move && now >= _next)
        {
            intents.Add(new PadMoved(move, Repeat: true));
            _repeats++;
            _next += RepeatAfter(_repeats);
        }
        var scroll = ScrollOf(_last);
        if (scroll != 0 && now > _lastTick)
        {
            intents.Add(new PadScrolled(scroll * ScrollSpeed * (now - _lastTick).TotalSeconds));
        }
        _lastTick = now;
        return intents;
    }

    /// <summary>The pad is no longer read (the helper ended, the window was left): nothing is held.</summary>
    public void Reset()
    {
        _last = null;
        _held = null;
        _repeats = 0;
    }

    /// <summary>The direction the D-pad or the main stick holds: the D-pad first; the stick by its larger axis,
    /// with the gap between <see cref="StickEngage"/> and <see cref="StickRelease"/> for the direction held now.
    /// The GameCube's stick reads higher upward.</summary>
    public static PadMove? DirectionOf(HelperPad pad, PadMove? held = null)
    {
        ArgumentNullException.ThrowIfNull(pad);
        foreach (var move in DPad)
        {
            if (pad.Buttons.Contains(move.ToString()))
            {
                return move;
            }
        }
        int x = pad.StickX - 128, y = pad.StickY - 128;
        var stick = Math.Abs(x) >= Math.Abs(y)
            ? x > 0 ? PadMove.Right : PadMove.Left
            : y > 0 ? PadMove.Up : PadMove.Down;
        var reach = Math.Max(Math.Abs(x), Math.Abs(y));
        return reach >= StickEngage || (stick == held && reach >= StickRelease) ? stick : null;
    }

    private static readonly PadMove[] DPad = [PadMove.Up, PadMove.Down, PadMove.Left, PadMove.Right];

    /// <summary>The C-stick's scroll, -1 (up) to 1 (down); 0 within its dead zone.</summary>
    private static double ScrollOf(HelperPad? pad)
    {
        if (pad is not { Connected: true })
        {
            return 0;
        }
        var y = pad.CStickY - 128;
        return Math.Abs(y) < ScrollDeadZone ? 0 : -Math.Clamp(y / 100.0, -1, 1);
    }
}
