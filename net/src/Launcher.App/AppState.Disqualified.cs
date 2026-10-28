using Launcher.Core;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.App;

// A racer disqualified by the lobby's finish limit: their game is closed, so that it does not keep them out of the
// lobby (the lobby forms again only once its racers' games are done).
public sealed partial class AppState
{
    /// <summary>How long a disqualified racer's game shows the results before the launcher closes it.</summary>
    public static readonly TimeSpan DisqualifiedShown = TimeSpan.FromSeconds(5);

    /// <summary>Whether the player's own racer was disqualified: the race has a finish limit, the game says the race
    /// is over for everyone (its phase is Finished, protocol/board.toml), and the player's racer is still racing,
    /// neither over the line nor out (the game's list says "Disqualified").</summary>
    public static bool Disqualified(BoardPhase phase, RaceSnapshot snapshot, RaceRules? rules)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return phase == BoardPhase.Finished && rules is { FinishLimitSeconds: > 0 } &&
               snapshot.Racers.FirstOrDefault(r => r.Seat == snapshot.LocalSeat) is { State: RacerLive.Racing };
    }

    /// <summary>A race's game whose racer was disqualified is asked to stop once, <see cref="DisqualifiedShown"/>
    /// later: a stop the launcher asks for ends the game with 0, so the race server hears the player is back.</summary>
    private void CloseIfDisqualified(GameBoard board, RaceSnapshot snapshot, ForkRace race, DisqualifiedClose close)
    {
        if (race.Spectate || !Disqualified(board.Phase, snapshot, race.Start.Setup.Rules) ||
            Interlocked.Exchange(ref close.Asked, 1) != 0)
        {
            return;
        }
        Noted?.Invoke($"the player was disqualified in match {race.Start.MatchId} (the finish limit): the game closes " +
                      $"in {DisqualifiedShown.TotalSeconds:0} s");
        _ = Task.Run(async () =>
        {
            await Task.Delay(DisqualifiedShown).ConfigureAwait(false);
            if (close.Game is { HasExited: false } game)
            {
                await StopGameAsync(game, StopGrace).ConfigureAwait(false);
            }
        });
    }

    /// <summary>A race's game, once started, and whether its close was asked for.</summary>
    private sealed class DisqualifiedClose
    {
        public volatile System.Diagnostics.Process? Game;
        public int Asked;
    }
}
