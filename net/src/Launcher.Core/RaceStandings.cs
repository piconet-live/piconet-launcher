using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The race's order from a live snapshot (<see cref="RaceSnapshot.Standings"/>): the places the race's mode gives
/// (<see cref="RacerLive.Place"/>: the order the game's HUD shows, by points, distance, cuts, whatever the mode scores
/// by) when it gives them; else finished racers first, by finish time; then those still racing, by distance covered
/// over all laps; then the knocked out, the furthest first. The game's ranking byte (<see cref="RacerLive.Rank"/>) by
/// itself is not the race position (seen live: a racer a lap behind ranked first).
/// </summary>
public static class RaceStandings
{
    public static IReadOnlyList<RacerLive> Order(IEnumerable<RacerLive> racers) => RaceSnapshot.Standings(racers);

    /// <summary>Each racer's place (1 first) by seat. With the mode's places every racer has one, a knocked-out racer
    /// the mode placed too (a cut Knockout racer keeps its place); without, knocked-out racers have none.</summary>
    public static IReadOnlyDictionary<int, int> Places(IEnumerable<RacerLive> racers)
    {
        var ordered = Order(racers);
        var placed = RaceSnapshot.Placed(ordered);
        var places = new Dictionary<int, int>();
        var place = 0;
        foreach (var r in ordered.Where(r => placed || r.State != RacerLive.Out))
        {
            places[r.Seat] = ++place;
        }
        return places;
    }

    /// <summary>A racer's score as the standings write it: whole points; metres with their unit, in thousands ("12,345 m");
    /// nothing for a racer without one, or a snapshot without a kind.</summary>
    public static string ScoreText(RaceSnapshot snapshot, RacerLive racer)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(racer);
        if (racer.Score is not { } score || !float.IsFinite(score))
        {
            return "";
        }
        return snapshot.ScoreKind switch
        {
            RaceSnapshot.Points => Math.Round(score).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            RaceSnapshot.Metres => Math.Floor(score).ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture) + " m",
            _ => "",
        };
    }
}
