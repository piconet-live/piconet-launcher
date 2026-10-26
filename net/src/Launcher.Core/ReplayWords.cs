using System.Globalization;

namespace Launcher.Core;

/// <summary>One racer's line in a replay's results, as the Replays page shows it.</summary>
public sealed record ReplayResultLine(string Place, string Name, string Machine, string ColorHex, string Time, string BestLap, bool Focused)
{
    /// <summary>Knocked out: the time's place says <see cref="ReplayWords.Out"/>, in red.</summary>
    public bool Out => Time == ReplayWords.Out;

    /// <summary>The race ended before the line: <see cref="ReplayWords.Disqualified"/>, in amber.</summary>
    public bool Disqualified => Time == ReplayWords.Disqualified;
}

/// <summary>
/// What the Replays page says of a replay, in the player's words. A replay is a file somebody sent: its names,
/// machines, places and times are whatever the file says, so everything here is made fit to show (a name on one
/// line and of a name's length, a machine there is or none, at most a race's 30 results) and nothing throws.
/// </summary>
public static class ReplayWords
{
    /// <summary>The longest name shown (the game's names are 16 characters; a file can say anything).</summary>
    public const int MostName = 24;

    /// <summary>The most result lines shown: a race has 30 seats.</summary>
    public const int MostResults = 30;

    private const string NoColor = "#7D8794";

    /// <summary>A course's venue and track from its name ("Mute City: Twist Road"); a course without a venue is
    /// its own track.</summary>
    public static (string Venue, string Track) Course(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var at = name.IndexOf(": ", StringComparison.Ordinal);
        return at < 0 ? ("", name) : (name[..at], name[(at + 2)..]);
    }

    /// <summary>A name from a file on one line: no control characters and none of the marks that turn the reading
    /// direction (a name could show as another's: <see cref="ServerMessages.Text"/>), at most <see cref="MostName"/>
    /// characters.</summary>
    public static string Name(string? name)
    {
        var plain = string.Concat(ServerMessages.Text(name, int.MaxValue).Where(c => !char.IsSurrogate(c))).Trim();
        return plain.Length <= MostName ? plain : plain[..MostName].TrimEnd();
    }

    /// <summary>A machine's name, or none for a number that is no machine.</summary>
    public static string Machine(int machine) => machine >= 0 && machine < Machines.Names.Count ? Machines.Names[machine] : "";

    /// <summary>The racer the replay is seen from.</summary>
    public static string Racer(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Name(info.Recorder?.Name ?? info.You);
    }

    /// <summary>What stands for a racer knocked out, in a place and in a time: "KO", the game's word.</summary>
    public const string Out = "KO";

    /// <summary>The racer's own place ("8th", or "KO" when knocked out); none for a replay without results.</summary>
    public static string Place(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.RecorderResult switch
        {
            null => "",
            { Out: true } => Out,
            { Place: var place } => Ordinal(place),
        };
    }

    /// <summary>"of 30" beside the place; none without a place.</summary>
    public static string Of(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.RecorderResult is not null && info.Results is { Count: > 0 } all ? Of(Math.Min(all.Count, MostResults)) : "";
    }

    /// <summary>"of 30" beside a place, as every page says it (the race's page in capitals).</summary>
    public static string Of(int racers) => string.Create(CultureInfo.InvariantCulture, $"of {racers}");

    /// <summary>What stands in a time's place for a racer the race ended for before the line (the host's rule: so
    /// long after the first finish), as the game's own results list says it. A racer who went out is "KO".</summary>
    public const string Disqualified = "Disqualified";

    /// <summary>The racer's own finish time, or <see cref="Disqualified"/>; none when they went out or the replay
    /// has no results.</summary>
    public static string Time(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.RecorderResult is { } own ? Time(own) : "";
    }

    /// <summary>A result's time, or <see cref="Disqualified"/> for a racer with no finish who is not out.</summary>
    public static string Time(RaceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Time is { } time ? Clock(time) : result is { Finished: false, Out: false } ? Disqualified : "";
    }

    public static string Laps(int laps) => laps == 1 ? "1 lap" : string.Create(CultureInfo.InvariantCulture, $"{laps} laps");

    /// <summary>The race's results by place, the first <see cref="MostResults"/> of them; empty without results.</summary>
    public static IReadOnlyList<ReplayResultLine> Results(ReplayInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return [.. (info.Results ?? []).Take(MostResults).Select(r => new ReplayResultLine(
            r.Out ? "-" : Ordinal(r.Place),
            ResultName(r.Racer),
            Machine(r.Racer.Machine), Hex(r.Racer.Color),
            r.Out ? Out : Time(r),
            r.BestLap is { } b ? Clock(b) : "",
            r.Racer.Seat == info.Recorder?.Seat))];
    }

    /// <summary>A racer's name in the results: a player's own; a CPU's with "(CPU)", or just that without a name.</summary>
    private static string ResultName(ReplayRacer racer)
    {
        var name = Name(racer.Name);
        return racer.IsPlayer ? name.Length > 0 ? name : "Racer" : name.Length > 0 && name != "CPU" ? $"{name} (CPU)" : "CPU";
    }

    /// <summary>A racer's colour as "#RRGGBB"; a grey for a racer without one (a CPU in its machine's own colours).</summary>
    public static string Hex(Online.Protocol.Rgb? color) =>
        color is { } c ? Online.Protocol.Rgb.Hex(c) : NoColor;

    /// <summary>A time as the series writes it: 2'01"182, minutes, seconds, thousandths; past an hour the minutes
    /// go on counting (72 hours: 4320'00"000). A time before the start is none: 0'00"000.</summary>
    public static string Clock(TimeSpan time)
    {
        var t = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return string.Create(CultureInfo.InvariantCulture, $"{(long)t.TotalMinutes}'{t.Seconds:00}\"{t.Milliseconds:000}");
    }

    /// <summary>Whole seconds as the series writes them, for a countdown or a span the launcher says: 0'29", 1'05";
    /// minutes go on counting (10'00"). Below none is none: 0'00".</summary>
    public static string Seconds(int seconds)
    {
        var s = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 60}'{s % 60:00}\"");
    }

    public static string Ordinal(int n) =>
        string.Create(CultureInfo.InvariantCulture, $"{n}") + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
