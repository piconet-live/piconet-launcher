using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>The race as the game's status board holds it (<see cref="BoardSnapshot"/>, binary), as the launcher's pages
/// and the coordinator take it (<see cref="RaceSnapshot"/>): the same numbers the game sent as JSON over UDP before
/// the board.</summary>
public static class BoardSnapshots
{
    /// <summary>The snapshot, or null when it names more racers than a race has.</summary>
    public static RaceSnapshot? ToRace(BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Count > snapshot.Racers.Length || snapshot.LocalSeat >= RacerLive.Seats || snapshot.Frame > int.MaxValue)
        {
            return null;
        }
        var racers = new RacerLive[snapshot.Count];
        for (var i = 0; i < racers.Length; i++)
        {
            ref var r = ref snapshot.Racers[i];
            racers[i] = new RacerLive(
                r.Seat, r.Rank, r.Lap, Ms(r.LapMs), Ms(r.TotalMs),
                (r.Flags & BoardRacerFlags.HasBestLap) != 0 ? Ms(r.BestLapMs) : null,
                r.Speed, r.Energy, r.MaxEnergy, r.Kos, r.TopSpeed, State(r.State), r.X, r.Y, r.Z, r.Distance);
        }
        return new RaceSnapshot((int)snapshot.Frame, (int)snapshot.LocalSeat, racers);
    }

    private static int Ms(uint ms) => (int)Math.Min(ms, RacerLive.MaxMs);

    private static string State(BoardRacerState state) => state switch
    {
        BoardRacerState.Finished => RacerLive.Finished,
        BoardRacerState.Out => RacerLive.Out,
        _ => RacerLive.Racing,
    };
}
