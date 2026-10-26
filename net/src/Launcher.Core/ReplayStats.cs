namespace Launcher.Core;

/// <summary>The machine a replay's recorder raced, as the Stats page tells machines apart: the stock machine, or a
/// custom machine by its three parts (its colours and pilot are no other machine).</summary>
public sealed record StatsMachine(int Machine, int? Body = null, int? Cockpit = null, int? Booster = null)
{
    public static StatsMachine Of(RacedMachine raced)
    {
        ArgumentNullException.ThrowIfNull(raced);
        return raced.Custom is { } c ? new StatsMachine(raced.Machine, c.Body, c.Cockpit, c.Booster) : new StatsMachine(raced.Machine);
    }

    /// <summary>The machine to name (<see cref="RacedMachine.Name"/>).</summary>
    public RacedMachine Raced => new(Machine, Body is { } body && Cockpit is { } cockpit && Booster is { } booster
        ? new Online.Protocol.CustomMachine(body, cockpit, booster, 0)
        : null);
}

/// <summary>
/// One replay as the Stats page counts it: what its header and its results say of the player who recorded it
/// (<see cref="ReplayInfo"/>, read by <see cref="ReplayFile"/>). Kept in the replay index (<see cref="ReplayIndex"/>)
/// so that the files are not read again at every start.
/// </summary>
/// <param name="Place">The recorder's place (1 for a win; a racer knocked out is placed after the rest); null: the
/// replay has no results, or none for the recorder.</param>
/// <param name="Racers">The racers in its results (at most a race's 30); 0 without results.</param>
/// <param name="TimeMs">The recorder's finish time; null: they did not finish (or there are no results).</param>
public sealed record ReplayRow(DateTimeOffset Recorded, int Course, int Laps, bool Practice, StatsMachine Machine,
    int? Place, int Racers, bool Out, long? TimeMs, long? BestLapMs)
{
    public static ReplayRow Of(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var own = info.RecorderResult;
        var machine = info.Recorder is { } recorder ? StatsMachine.Of(recorder.Raced) : new StatsMachine(-1);
        return new ReplayRow(info.Recorded, info.Course, info.Laps, info.Practice, machine, own?.Place,
            Math.Min(info.Results?.Count ?? 0, ReplayWords.MostResults), own?.Out ?? false,
            own?.Time is { } time ? (long)time.TotalMilliseconds : null,
            own?.BestLap is { } lap ? (long)lap.TotalMilliseconds : null);
    }

    /// <summary>A place the recorder finished or was still racing in (not knocked out).</summary>
    public int? Placed => Out ? null : Place;

    public TimeSpan? Time => TimeMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
}

/// <summary>Places over a set of races: how many, the wins (1st), the podiums (1st to 3rd), the average and best
/// place over the races the player was not knocked out of, and the knock-outs.</summary>
public sealed record PlaceStats(int Races, int Wins, int Podiums, double? AveragePlace, int? BestPlace, int KnockedOut);

/// <summary>The best finish time on a course at a number of laps (times of other lap counts are other records),
/// with the machine it was set in and when; <paramref name="Raced"/>: how often that course and laps were raced (in
/// that machine, for a time by machine).</summary>
public sealed record BestTime(int Course, int Laps, StatsMachine Machine, TimeSpan Time, DateTimeOffset When, int Raced);

/// <summary>A course and how often it was raced.</summary>
public sealed record CourseCount(int Course, int Count);

/// <summary>The Stats page's figures for one kind of replay (online races, or practice runs).</summary>
/// <param name="Count">The replays counted.</param>
/// <param name="Places">Their places (practice runs have none to speak of: null for them).</param>
/// <param name="Finished">The replays the player finished.</param>
/// <param name="Recent">The latest replays with a place, newest first (at most <see cref="ReplayStats.RecentCount"/>).</param>
public sealed record StatsPart(int Count, PlaceStats? Places, int Finished, IReadOnlyList<BestTime> BestByCourse,
    IReadOnlyList<BestTime> BestByMachine, IReadOnlyList<CourseCount> MostRaced, IReadOnlyList<ReplayRow> Recent);

/// <summary>
/// The player's own figures, from their replays alone (a local Stats page, with no account):
/// races, wins, podiums, average and best place, best times per course and per machine, the courses raced most and
/// the recent form; online races and practice runs apart. Nothing is counted that a replay's header and results do
/// not say (boosts, side attacks: they wait for accounts). Plain values, no UI.
/// </summary>
public static class ReplayStats
{
    /// <summary>How many races the recent form shows.</summary>
    public const int RecentCount = 10;

    /// <summary>How many courses "most raced" lists.</summary>
    public const int MostCount = 5;

    /// <summary>The figures of the online races (<paramref name="practice"/> false) or the practice runs.</summary>
    public static StatsPart Of(IEnumerable<ReplayRow> rows, bool practice)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var part = rows.Where(r => r.Practice == practice).OrderByDescending(r => r.Recorded).ToList();
        var placed = part.Where(r => r.Place is not null).ToList();
        PlaceStats? places = practice ? null : Places(placed);
        return new StatsPart(part.Count, places, part.Count(r => r.TimeMs is not null),
            Best(part, byMachine: false), Best(part, byMachine: true),
            [.. part.GroupBy(r => r.Course).Select(g => new CourseCount(g.Key, g.Count()))
                .OrderByDescending(c => c.Count).ThenBy(c => c.Course).Take(MostCount)],
            [.. placed.Take(RecentCount)]);
    }

    private static PlaceStats Places(List<ReplayRow> placed)
    {
        var standing = placed.Where(r => r.Placed is not null).Select(r => r.Placed!.Value).ToList();
        return new PlaceStats(placed.Count, standing.Count(p => p == 1), standing.Count(p => p <= 3),
            standing.Count > 0 ? standing.Average() : null, standing.Count > 0 ? standing.Min() : null,
            placed.Count(r => r.Out));
    }

    /// <summary>The best time of each course and laps (and machine), the courses in their number's order.</summary>
    private static List<BestTime> Best(List<ReplayRow> rows, bool byMachine) =>
    [
        .. rows.GroupBy(r => (r.Course, r.Laps, Machine: byMachine ? r.Machine : null))
            .Select(g => (Raced: g.Count(), Best: g.Where(r => r.TimeMs is not null).MinBy(r => r.TimeMs)))
            .Where(x => x.Best is not null)
            .Select(x => new BestTime(x.Best!.Course, x.Best.Laps, x.Best.Machine, x.Best.Time!.Value, x.Best.Recorded, x.Raced))
            .OrderBy(b => b.Course).ThenBy(b => b.Laps).ThenBy(b => b.Time),
    ];
}
