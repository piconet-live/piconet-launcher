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
                r.Speed, r.Energy, r.MaxEnergy, r.Kos, r.TopSpeed, State(r.State), r.X, r.Y, r.Z, r.Distance,
                Math.Min((int)r.Place, RacerLive.Seats), (r.Flags & BoardRacerFlags.HasScore) != 0 ? r.Score : null);
        }
        var kind = snapshot.ScoreKind switch
        {
            BoardScoreKind.Points => RaceSnapshot.Points,
            BoardScoreKind.Metres => RaceSnapshot.Metres,
            _ => null,
        };
        return new RaceSnapshot((int)snapshot.Frame, (int)snapshot.LocalSeat, racers, kind,
            kind == RaceSnapshot.Points ? Label(snapshot.ScoreLabel, snapshot.ScoreLabelLength) : null);
    }

    /// <summary>The score's label as the game wrote it (UTF-8, the board's field); a length past the field is cut to it.</summary>
    private static string Label(byte[] bytes, uint length) =>
        System.Text.Encoding.UTF8.GetString(bytes, 0, (int)Math.Min(length, (uint)bytes.Length));

    private static int Ms(uint ms) => (int)Math.Min(ms, RacerLive.MaxMs);

    private static string State(BoardRacerState state) => state switch
    {
        BoardRacerState.Finished => RacerLive.Finished,
        BoardRacerState.Out => RacerLive.Out,
        _ => RacerLive.Racing,
    };
}
