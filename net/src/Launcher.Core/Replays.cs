using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Launcher.Core;

/// <summary>A racer of a replay: the seat, the name the game showed, the machine and colour (null: the stock one), and
/// the custom machine the seat raced in place of the stock one (the set-up seat's <c>custom</c>; null: none).</summary>
public sealed record ReplayRacer(int Seat, string Name, int Machine, Online.Protocol.Rgb? Color, bool IsPlayer,
    Online.Protocol.CustomMachine? Custom = null)
{
    /// <summary>The machine raced (<see cref="RacedMachine"/>).</summary>
    public RacedMachine Raced => new(Machine, Custom);
}

/// <summary>One racer's result, from the results the Dolphin fork appends when the recorded race ends.</summary>
/// <param name="Place">1 for the winner (the game's order: finishers by time, then the racers still in, the ones knocked
/// out last).</param>
/// <param name="Time">The finish time; null when the racer did not finish.</param>
public sealed record RaceResult(ReplayRacer Racer, int Place, bool Finished, bool Out, TimeSpan? Time, TimeSpan? BestLap);

/// <summary>What the launcher shows of a replay file, from its header and its results.</summary>
/// <param name="Players">The player seats' names (CPU seats left out).</param>
/// <param name="You">The recorder's name.</param>
/// <param name="Length">The recording's length (its steps at 60 a second, the results screens included).</param>
/// <param name="Recorder">The racer the replay is seen from: the player who recorded it.</param>
/// <param name="Results">Every racer's result by place; null for a replay without them (recorded before they were
/// kept, or stopped before the race was over).</param>
/// <param name="StatPack">The name of the stat pack the race ran on, for a label; null: the original stats.</param>
/// <param name="StatPackProblem">Why the game would not play the file for its stat pack (the file's version and
/// its pack disagree, or the pack is not one this launcher takes), as <see cref="ReplayFile.Problem"/> says it;
/// null: nothing of the kind.</param>
/// <param name="Practice">A practice run (its header's <c>practice</c>: true): the player alone, rewinds left out.</param>
public sealed record ReplayInfo(
    string Path, DateTimeOffset Recorded, int Course, int Laps, IReadOnlyList<string> Players, string You, TimeSpan Length,
    ReplayRacer? Recorder = null, IReadOnlyList<RaceResult>? Results = null, string? StatPack = null, string? StatPackProblem = null,
    bool Practice = false, IReadOnlyList<int>? Teams = null, IReadOnlyList<ReplayRacer>? Racers = null)
{
    /// <summary>What a row says of the race's stats in a word or two: the pack's name, "Stat pack not playable"
    /// for a file the game would refuse, or nothing for the original stats.</summary>
    public string StatPackLabel => StatPackProblem is not null ? "Stat pack not playable" : StatPack ?? "";

    public string CourseName => Courses.Name(Course);

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The recorder's own result, if the replay has results.</summary>
    public RaceResult? RecorderResult => Results?.FirstOrDefault(r => r.Racer.Seat == Recorder?.Seat);
}

/// <summary>What the launcher does with a replay beside reading it (<see cref="ReplayFile"/>): the file shown in the
/// system's file browser, a line of its racers.</summary>
public static class Replays
{
    /// <summary>Shows a replay file in the system's file browser, selected.</summary>
    public static void Reveal(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + System.IO.Path.GetFullPath(path) } }
            : new ProcessStartInfo(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".") { UseShellExecute = true };
        Process.Start(info)?.Dispose();
    }

    /// <summary><see cref="Reveal"/> for a button: false when the file cannot be shown (a path that is none, no
    /// file browser), and the launcher goes on.</summary>
    public static bool TryReveal(string path)
    {
        try
        {
            Reveal(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or InvalidOperationException or System.ComponentModel.Win32Exception
                                      or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>A short line for lists: "LAUNCHER, P201 and 28 more".</summary>
    public static string Racers(IReadOnlyList<string> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var sb = new StringBuilder(string.Join(", ", players.Take(3)));
        if (players.Count > 3)
        {
            sb.Append(CultureInfo.InvariantCulture, $" and {players.Count - 3} more");
        }
        return sb.ToString();
    }
}
