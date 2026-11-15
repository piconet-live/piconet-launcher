using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// Practice while the lobby waits (the redesigned interface's "Practice while you wait"): the player's seat in the
/// lobby is kept while a practice game runs, and the lobby calls the player back once it moves on from gathering
/// (its course vote begins, or a race is being started): the practice game is warned of, then ended after
/// <see cref="Warning"/>, and the lobby's own page takes over (the vote brings the player to it). The player's vote to
/// start counts on while they practise. A practice is started in the queue or while the lobby gathers, never once it
/// has moved on (<see cref="MayPractise"/>). Plain C#: <see cref="Changed"/> comes on whatever thread heard the lobby,
/// and the end of the practice is the caller's (<c>stop</c>).
/// </summary>
public sealed class LobbyRecall
{
    private readonly Action _stop;
    private readonly Lock _lock = new();
    private long _practice;
    private long _wake;
    private bool _practising;

    /// <param name="warning">How long the player is warned before the practice game ends.</param>
    /// <param name="stop">Ends the practice game (asked through its board, then by its process).</param>
    public LobbyRecall(TimeSpan warning, Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        Warning = warning;
        _stop = stop;
    }

    /// <summary>The warning's length: the owner's 10 seconds (the plan's decision 8) unless a test says otherwise.</summary>
    public static TimeSpan Standard { get; } = TimeSpan.FromSeconds(10);

    public TimeSpan Warning { get; }

    /// <summary>When the practice game ends: the recall runs; null: none runs.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>The recall ran its course (or "Back now") and the practice game was ended: the lobby's page is next.</summary>
    public event Action? Recalled;

    /// <summary>A recall began or ended.</summary>
    public event Action? Changed;

    /// <summary>A recall began (by a lobby that moved on, or by its start countdown's last seconds): the game is told.</summary>
    public event Action? Began;

    /// <summary>What the player is told while the recall runs, with the seconds left.</summary>
    public static string Line(int seconds) =>
        $"The lobby starts in {Math.Max(0, seconds)} s. Your practice ends and you are brought back.";

    /// <summary>What the game is told (its log, and its screen where it shows notices).</summary>
    public string GameLine => $"Your lobby is starting: back to it in {(At is { } at ? (int)Math.Ceiling(Math.Max(0, (at - DateTimeOffset.UtcNow).TotalSeconds)) : (int)Math.Round(Warning.TotalSeconds))} seconds.";

    /// <summary>Why a practice does not start now, in a few words; null: it may.</summary>
    public const string MovedOnText = "Lobby starting a race";

    /// <summary>A practice may start: out of Quick Play, in its queue, or in a lobby that still gathers.</summary>
    /// <param name="inQuickPlay">The player is in Quick Play or a custom lobby.</param>
    public static bool MayPractise(bool inQuickPlay, LobbyState? lobby) => !inQuickPlay || !MovedOn(lobby);

    /// <summary>The lobby has moved on from gathering: its vote, its machines or its race.</summary>
    public static bool MovedOn(LobbyState? lobby) => lobby is not null && lobby.Stage != LobbyState.Forming;

    /// <summary>A practice game started while the player is in a lobby or its queue.</summary>
    public void Started()
    {
        lock (_lock)
        {
            _practising = true;
            _practice++;
            At = null;
        }
    }

    /// <summary>The practice game ended (by the recall, the player, or itself): no recall runs any more. True when
    /// the recall ended it.</summary>
    public bool Ended()
    {
        bool recalled;
        lock (_lock)
        {
            recalled = _practising && At is not null;
            _practising = false;
            _practice++;
            At = null;
        }
        if (recalled)
        {
            Recalled?.Invoke();
        }
        Changed?.Invoke();
        return recalled;
    }

    /// <summary>Out of Quick Play while practising: the practice goes on, and nobody calls the player back.</summary>
    public void Left()
    {
        bool was;
        lock (_lock)
        {
            was = At is not null;
            _practising = false;
            _practice++;
            At = null;
        }
        if (was)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>The lobby as the server says it now, while the player practises: the recall begins (once) so that the
    /// practice ends as the lobby starts. With its start countdown running (<paramref name="startsAt"/>), the recall
    /// begins <see cref="Warning"/> before the countdown runs out (now, when less is left), and ends the practice as it
    /// runs out: the player is back for the vote, not some seconds into it. A lobby that moved on without it (its start
    /// vote, a full lobby) calls back at once, and the practice ends after <see cref="Warning"/>. True when it began now.</summary>
    public bool Heard(LobbyState? lobby, DateTimeOffset now, DateTimeOffset? startsAt = null)
    {
        long practice;
        DateTimeOffset at;
        lock (_lock)
        {
            if (!_practising || At is not null)
            {
                return false;
            }
            if (MovedOn(lobby))
            {
                at = now + Warning;
            }
            else if (startsAt is { } start && start - now <= Warning)
            {
                at = start < now ? now : start;
            }
            else
            {
                if (startsAt is { } later)
                {
                    // the countdown's last seconds are not told as they come: woken then (a newer countdown, a later wake)
                    _ = WakeAsync(_practice, ++_wake, later, later - Warning - now);
                }
                return false;
            }
            At = at;
            practice = _practice;
        }
        Begin(practice, at - now);
        return true;
    }

    private void Begin(long practice, TimeSpan left)
    {
        Changed?.Invoke();
        Began?.Invoke();
        _ = EndAfterAsync(practice, left);
    }

    /// <summary>The countdown's last <see cref="Warning"/>: the recall begins, the practice ending as it runs out.</summary>
    private async Task WakeAsync(long practice, long wake, DateTimeOffset startsAt, TimeSpan after)
    {
        await Task.Delay(after < TimeSpan.Zero ? TimeSpan.Zero : after).ConfigureAwait(false);
        lock (_lock)
        {
            if (practice != _practice || wake != _wake || !_practising || At is not null)
            {
                return;   // the practice ended, a newer countdown came, or a recall runs already
            }
            At = startsAt;
        }
        Begin(practice, startsAt - DateTimeOffset.UtcNow);
    }

    /// <summary>"Back now": the practice game ends at once.</summary>
    public void Now()
    {
        lock (_lock)
        {
            if (!_practising)
            {
                return;
            }
        }
        _stop();
    }

    private async Task EndAfterAsync(long practice, TimeSpan left)
    {
        await Task.Delay(left < TimeSpan.Zero ? TimeSpan.Zero : left).ConfigureAwait(false);
        lock (_lock)
        {
            if (practice != _practice || At is null)
            {
                return;   // the game ended meanwhile, or the player left
            }
        }
        _stop();
    }
}
