using Launcher.Core.Board;

namespace Launcher.Core;

/// <summary>
/// Whether the game stands still, from its status board's heartbeat (<see cref="GameBoard"/>): a heartbeat that has
/// not moved for <see cref="FrozenAfter"/> while the game is where it should beat. Not judged before the game's first
/// beat (a start that never shows a picture is the start limit's matter), at the start gate (the game's own wait
/// begins no frame, for as long as the other games load), and while the core waits for the relay's inputs (it gives
/// up by itself). The time is the caller's, so that a test can move it.
/// </summary>
public sealed class FreezeJudge(TimeSpan frozenAfter)
{
    /// <summary>How long a heartbeat may stand before the game counts as frozen. The game's own watch reports a stand
    /// of 2 s in its log (5 s in the countdown, whose loads take most of a second on a fast PC); the launcher offers to
    /// end a game only when it is surely hung.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    private ulong _beat;
    private TimeSpan _since;
    private bool _judging;

    public TimeSpan FrozenAfter { get; } = frozenAfter;

    /// <summary>The game counts as frozen now.</summary>
    public bool Frozen { get; private set; }

    /// <summary>How long the heartbeat has stood (0 while it moves or is not judged).</summary>
    public TimeSpan Stood { get; private set; }

    /// <summary>One look at the board. True when <see cref="Frozen"/> changed.</summary>
    /// <param name="now">The caller's clock (any steady one).</param>
    public bool Look(TimeSpan now, ulong heartbeat, BoardPhase phase, uint flags)
    {
        var judged = heartbeat != 0 && Judged(phase) && (flags & BoardStatusFlags.WaitsForRelay) == 0;
        if (!judged || !_judging || heartbeat != _beat)
        {
            _beat = heartbeat;
            _since = now;
        }
        _judging = judged;
        Stood = judged ? now - _since : TimeSpan.Zero;
        var frozen = judged && Stood >= FrozenAfter;
        if (frozen == Frozen)
        {
            return false;
        }
        Frozen = frozen;
        return true;
    }

    /// <summary>Where the heartbeat must move: everywhere but before the board is taken, at the start gate and before the
    /// first picture. A game that is stopping must be gone soon after: one still there is hung too.</summary>
    public static bool Judged(BoardPhase phase) => phase is not (BoardPhase.None or BoardPhase.Starting or BoardPhase.Gate);
}
