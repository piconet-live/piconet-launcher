namespace Launcher.Core;

/// <summary>What every status card carries besides its lines: the Discord application (its id; its name is the
/// "Playing ..." Discord shows), the large picture and its text, and one button.</summary>
/// <param name="ApplicationId">The Discord application's id: a number, which is no secret. Anything else (a
/// placeholder) turns the status off (<see cref="DiscordPresence.Available"/>).</param>
/// <param name="LargeImage">The large picture's key in the application's art.</param>
public sealed record PresenceBrand(string ApplicationId, string LargeImage, string LargeText, string ButtonLabel,
    string ButtonUrl);

/// <summary>What a link to Discord says happened, from its own thread.</summary>
public enum PresenceLinkEvent
{
    /// <summary>Discord is there and takes the status.</summary>
    Ready,

    /// <summary>No Discord to talk to (not running, or not this user's): the link tries again by itself.</summary>
    NotFound,

    /// <summary>The connection ended: the link tries again by itself.</summary>
    Lost,
}

/// <summary>A connection to the player's own Discord, by its local IPC (<see cref="DiscordLink"/>; a test's double).
/// It connects, and connects again when Discord comes or comes back, by itself.</summary>
public interface IPresenceLink : IDisposable
{
    void Show(PresenceCard card);

    void Clear();
}

/// <summary>
/// The launcher's status on Discord ("Playing" and the application's name, then what the player does:
/// <see cref="PresenceWords"/>), on the player's own Discord only, while the player's "Show on Discord" setting is on. Nothing here tells the player
/// anything: without Discord, or when it goes, the status waits and the link tries again quietly; a line in the
/// launcher's log says each change of the connection once. Cards are sent when they change, at most one every
/// <see cref="LeastGap"/> (Discord limits how often a status changes); the last one wanted is sent when the gap is over.
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>The least time between two cards sent (Discord takes five changes in twenty seconds).</summary>
    public static readonly TimeSpan LeastGap = TimeSpan.FromSeconds(5);

    private readonly PresenceBrand _brand;
    private readonly Action<string> _log;
    private readonly Func<PresenceBrand, Action<PresenceLinkEvent>, IPresenceLink> _connect;
    private readonly TimeProvider _time;
    private readonly TimeSpan _gap;
    private readonly Lock _gate = new();
    private IPresenceLink? _link;
    private PresenceCard? _wanted, _sent;
    private DateTimeOffset _sentAt = DateTimeOffset.MinValue;
    private ITimer? _later;
    private PresenceLinkEvent? _heard;
    private bool _faulted, _disposed;

    /// <param name="log">A line for the launcher's log (each change of the connection, once).</param>
    /// <param name="connect">Makes the link (a test's double); null: Discord's own (<see cref="DiscordLink"/>).</param>
    /// <param name="gap">The least time between two cards; null: <see cref="LeastGap"/>.</param>
    public DiscordPresence(PresenceBrand brand, Action<string> log,
        Func<PresenceBrand, Action<PresenceLinkEvent>, IPresenceLink>? connect = null, TimeProvider? time = null,
        TimeSpan? gap = null)
    {
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(log);
        _brand = brand;
        _log = log;
        _connect = connect ?? ((b, heard) => new DiscordLink(b, heard));
        _time = time ?? TimeProvider.System;
        _gap = gap ?? LeastGap;
        Available = IsApplicationId(brand.ApplicationId);
    }

    /// <summary>The build names a Discord application: without one there is no status and no connection.</summary>
    public bool Available { get; }

    /// <summary>The player's setting: the status shows while it is on.</summary>
    public bool On { get; private set; }

    /// <summary>The card Discord was last given (none after a clear).</summary>
    public PresenceCard? Shown
    {
        get
        {
            lock (_gate)
            {
                return _sent;
            }
        }
    }

    /// <summary>A Discord application's id: a number of 17 to 20 digits (a Discord "snowflake").</summary>
    public static bool IsApplicationId(string? id) =>
        id is { Length: >= 17 and <= 20 } && id.All(char.IsAsciiDigit);

    /// <summary>The player's setting changed (or is read at the start). Off: the status goes at once and the
    /// connection is closed; on: the card wanted is shown.</summary>
    public void SetOn(bool on)
    {
        lock (_gate)
        {
            if (on == On || _disposed)
            {
                return;
            }
            On = on;
            if (!on)
            {
                Close(clear: true);
                return;
            }
            Pump();
        }
    }

    /// <summary>The card the launcher's state makes now: sent when it differs from the last one, at once or when the
    /// gap since the last is over.</summary>
    public void Show(PresenceCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        lock (_gate)
        {
            _wanted = card;
            Pump();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Close(clear: true);
        }
    }

    /// <summary>Sends the card wanted when there is one to send and the gap allows; otherwise sets the timer for
    /// the gap's end. Under the lock.</summary>
    private void Pump()
    {
        if (!Available || !On || _disposed || _wanted is null || _wanted == _sent || _later is not null)
        {
            return;
        }
        var now = _time.GetUtcNow();
        var wait = _sentAt + _gap - now;
        if (wait > TimeSpan.Zero)
        {
            _later = _time.CreateTimer(_ => Later(), null, wait, Timeout.InfiniteTimeSpan);
            return;
        }
        var card = _wanted;
        if (Fails(() => (_link ??= _connect(_brand, Heard)).Show(card)))
        {
            // the link is dropped and made again with the next card after the gap
            Close(clear: false);
        }
        else
        {
            _sent = card;
            _faulted = false;
        }
        _sentAt = now;
    }

    private void Later()
    {
        lock (_gate)
        {
            _later?.Dispose();
            _later = null;
            Pump();
        }
    }

    /// <summary>The link's word, from its thread: written to the log when it is news.</summary>
    private void Heard(PresenceLinkEvent what)
    {
        lock (_gate)
        {
            if (_heard == what || _disposed)
            {
                return;
            }
            _heard = what;
        }
        _log(what switch
        {
            PresenceLinkEvent.Ready => "Discord status: connected",
            PresenceLinkEvent.NotFound => "Discord status: no Discord found (trying again quietly)",
            _ => "Discord status: connection lost (trying again quietly)",
        });
    }

    /// <summary>Ends the link (cleared first when asked) and the timer. Under the lock.</summary>
    private void Close(bool clear)
    {
        _later?.Dispose();
        _later = null;
        if (_link is { } link)
        {
            _link = null;
            if (clear)
            {
                _ = Fails(link.Clear);
            }
            _ = Fails(link.Dispose);
        }
        _sent = null;
        _heard = null;
    }

    /// <summary>Runs a call to the link; an error is written to the log (once until a card goes through) and
    /// never reaches the player.</summary>
    private bool Fails(Action call)
    {
        try
        {
            call();
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (!_faulted)
            {
                _faulted = true;
                _log($"Discord status: {e.GetType().Name}: {e.Message} (trying again quietly)");
            }
            return true;
        }
    }
}
