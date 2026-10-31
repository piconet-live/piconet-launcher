namespace Launcher.Core;

/// <summary>
/// A replay as the Replays page lists it, kept in the replay index (<see cref="ReplayIndex"/>) so that the page shows
/// thousands of replays without opening their files: what a row says, in <see cref="ReplayWords"/>'s words, and the
/// players' names. The race's results are not kept: a row that opens them reads its file then.
/// </summary>
/// <param name="Racer">The racer the replay is seen from (<see cref="ReplayWords.Racer"/>).</param>
/// <param name="Raced">Their machine; null: the file names no recorder.</param>
/// <param name="Place">Their place as a row says it ("1st", "KO", none).</param>
/// <param name="Of">"of 30", or none.</param>
/// <param name="Time">Their finish time as a row says it, "Disqualified", or none.</param>
/// <param name="Placed">Their place as a number, while they were not knocked out; null: none (out, or no results).</param>
/// <param name="Racers">The racers in the results (0: none).</param>
/// <param name="Players">The player seats' names (<see cref="ReplayInfo.Players"/>).</param>
public sealed record ReplayListing(string Path, DateTimeOffset Recorded, int Course, int Laps, bool Practice,
    string StatPackLabel, string Racer, Online.Protocol.Rgb? Color, RacedMachine? Raced, string Place, string Of, string Time,
    int? Placed, long? TimeMs, int Racers, IReadOnlyList<string> Players, string ModeLabel = "")
{
    public static ReplayListing From(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var own = info.RecorderResult;
        return new ReplayListing(info.Path, info.Recorded, info.Course, info.Laps, info.Practice, info.StatPackLabel,
            ReplayWords.Racer(info), info.Recorder?.Color, info.Recorder?.Raced, ReplayWords.Place(info), ReplayWords.Of(info),
            ReplayWords.Time(info), own is { Out: false } ? own.Place : null,
            own?.Time is { } time ? (long)time.TotalMilliseconds : null,
            Math.Min(info.Results?.Count ?? 0, ReplayWords.MostResults), [.. info.Players.Take(ReplayWords.MostResults).Select(ReplayWords.Name)], info.ModeLabel);
    }

    public string CourseName => Courses.Name(Course);

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>A race with results to open (a practice run has none).</summary>
    public bool HasResults => !Practice && Racers > 0;

    /// <summary>The recorder was knocked out.</summary>
    public bool Out => Place == ReplayWords.Out;
}
