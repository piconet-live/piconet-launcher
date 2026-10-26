using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The race's order from a live snapshot: finished racers first, by finish time; then those still racing, by distance
/// covered over all laps; then the knocked out, the furthest first. The game's ranking byte (<see cref="RacerLive.Rank"/>)
/// is not the race position (seen live: a racer a lap behind ranked first).
/// </summary>
public static class RaceStandings
{
    public static IReadOnlyList<RacerLive> Order(IEnumerable<RacerLive> racers) => RaceSnapshot.Standings(racers);

    /// <summary>Each racer's place (1 first) by seat; knocked-out racers have none.</summary>
    public static IReadOnlyDictionary<int, int> Places(IEnumerable<RacerLive> racers)
    {
        var places = new Dictionary<int, int>();
        var place = 0;
        foreach (var r in Order(racers).Where(r => r.State != RacerLive.Out))
        {
            places[r.Seat] = ++place;
        }
        return places;
    }
}
