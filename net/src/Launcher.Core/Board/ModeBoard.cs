using System.Globalization;
using System.Text;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A <c>board</c> line of the race's mode file as the status board names it (ModeHead): what it shows, and how
/// a value of it reads.</summary>
/// <param name="Words">What the value shows when it is an index into them; empty: the line names none.</param>
public sealed record ShownModeLine(BoardModeBoardScope Scope, BoardModeBoardType Type, int Column, string Label, IReadOnlyList<string> Words)
{
    /// <summary>A value of this line in words: its word when it is one of the line's (an empty word is a blank cell);
    /// otherwise what its type reads as: a number, Yes or No, a racer's name, the racers of a set.</summary>
    public string Text(int value, Func<int, string> seatName)
    {
        ArgumentNullException.ThrowIfNull(seatName);
        if (Type != BoardModeBoardType.Real && value >= 0 && value < Words.Count)
        {
            return Words[value];
        }
        return Type switch
        {
            BoardModeBoardType.Real => Real(BitConverter.Int32BitsToSingle(value)),
            BoardModeBoardType.Bool => value != 0 ? "Yes" : "No",
            BoardModeBoardType.Seat when value == -1 => "-",
            BoardModeBoardType.Seat when value is >= 0 and < RacerLive.Seats => seatName(value),
            BoardModeBoardType.Seats => Set(value, seatName),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string Real(float value) =>
        float.IsFinite(value) ? (Math.Round((double)value, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture) : "-";

    private static string Set(int bits, Func<int, string> seatName)
    {
        var names = Enumerable.Range(0, RacerLive.Seats).Where(seat => (bits >> seat & 1) != 0).Select(seatName).ToList();
        return names.Count == 0 ? "-" : string.Join(", ", names);
    }
}

/// <summary>The race's mode as the status board names it, written once by the game: its name, its hash's first bytes,
/// its lines.</summary>
public sealed record ModeBoardHead(string Name, string Hash, IReadOnlyList<ShownModeLine> Lines)
{
    private const int WordsALine = 16;

    /// <summary>The head as the game wrote it, or null when it says no mode. The board is another process's file, so a
    /// length past its field is cut and a line of no scope, type or column there is left out.</summary>
    public static ModeBoardHead? Of(BoardModeHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Serial == 0)
        {
            return null;
        }
        var lines = new List<ShownModeLine>();
        for (var i = 0; i < Math.Min(head.Count, (uint)head.Lines.Length); i++)
        {
            ref var line = ref head.Lines[i];
            if (line.Scope > BoardModeBoardScope.Team || line.Type > BoardModeBoardType.Seats || line.Column >= ModeBoardNow.Columns)
            {
                continue;
            }
            var words = new string[Math.Min(line.WordCount, WordsALine)];
            for (var w = 0; w < words.Length; w++)
            {
                ref var word = ref head.Words[(i * WordsALine) + w];
                words[w] = Text(word.Text, word.Length);
            }
            lines.Add(new ShownModeLine(line.Scope, line.Type, line.Column, Text(line.Label, line.LabelLength), words));
        }
        return new ModeBoardHead(Text(head.Name, head.NameLength), Convert.ToHexStringLower(head.Hash), lines);
    }

    private static string Text(byte[] bytes, uint length) =>
        Encoding.UTF8.GetString(bytes, 0, (int)Math.Min(length, (uint)bytes.Length));
}

/// <summary>The mode's values at one picture of the game (ModeValues), with the head that says how they read.</summary>
public sealed class ModeBoardNow(ModeBoardHead head, uint frame, int[] globals, int[] seats, int[] teams, uint fault = 0)
{
    /// <summary>The values a scope has a seat, a team or a race: a line's <see cref="ShownModeLine.Column"/> is below.</summary>
    public const int Columns = 8;

    public ModeBoardHead Head { get; } = head;

    /// <summary>The step of the picture.</summary>
    public uint Frame { get; } = frame;

    /// <summary>What ended the race when the mode broke (D6), in words; null while it runs as it should.</summary>
    public string? Fault { get; } = fault switch
    {
        0 => null,
        1 => "Mode error: the race ended for everyone. The mode did too much in one step.",
        2 => "Mode error: the race ended for everyone. The mode asked for too many actions in one step.",
        3 => "Mode error: the race ended for everyone. The mode gave the game a number it can't use.",
        4 => "Mode error: the race ended for everyone. The mode's code did something it may not.",
        5 => "Mode error: the race ended for everyone. The game had no room for the mode.",
        _ => "Mode error: the race ended for everyone. Too many of the mode's rules were waiting at once.",
    };

    /// <summary>The numbers in <paramref name="values"/> as the game wrote them.</summary>
    public static ModeBoardNow Of(ModeBoardHead head, BoardModeValues values)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(values);
        return new ModeBoardNow(head, values.Frame, [.. values.Globals.Select(v => v.Value)],
            [.. values.Seats.Select(v => v.Value)], [.. values.Teams.Select(v => v.Value)], values.Fault);
    }

    public int Global(ShownModeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return globals[line.Column];
    }

    public int Seat(ShownModeLine line, int seat)
    {
        ArgumentNullException.ThrowIfNull(line);
        return seats[(seat * Columns) + line.Column];
    }

    public int Team(ShownModeLine line, int team)
    {
        ArgumentNullException.ThrowIfNull(line);
        return teams[(team * Columns) + line.Column];
    }
}

/// <summary>A label and its value in words.</summary>
public sealed record ModeFigure(string Label, string Value);

/// <summary>A team's lines: "Team 1", and its figures.</summary>
public sealed record ModeTeamFigures(string Team, IReadOnlyList<ModeFigure> Lines);

/// <summary>A mode's values as the race page draws them: the race's own lines, a column for each seat line, the team
/// lines for each team in the race.</summary>
public sealed record ModeBoardRows(IReadOnlyList<ModeFigure> Globals, IReadOnlyList<string> SeatHeads,
    IReadOnlyDictionary<int, IReadOnlyList<string>> SeatCells, IReadOnlyList<ModeTeamFigures> Teams)
{
    /// <summary>No lines: the page shows nothing of a mode.</summary>
    public static readonly ModeBoardRows None = new([], [], new Dictionary<int, IReadOnlyList<string>>(), []);

    public bool Any => Globals.Count > 0 || SeatHeads.Count > 0 || Teams.Count > 0;

    /// <param name="now">The values; null: no mode, or none with a line, or none written yet.</param>
    /// <param name="seats">The seats in the race.</param>
    /// <param name="teams">Each seat's team (<c>setup.mode.teams</c>): a seat past its end, or with no list every seat,
    /// is a team of its own, its number.</param>
    /// <param name="seatName">A seat's racer, for the lines that name a seat.</param>
    public static ModeBoardRows Of(ModeBoardNow? now, IEnumerable<int> seats, IReadOnlyList<int>? teams, Func<int, string> seatName)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(seatName);
        if (now is null || now.Head.Lines.Count == 0)
        {
            return None;
        }
        var inRace = seats.Where(s => s is >= 0 and < RacerLive.Seats).ToList();
        var lines = now.Head.Lines;
        var seatLines = lines.Where(l => l.Scope == BoardModeBoardScope.Seat).ToList();
        var teamLines = lines.Where(l => l.Scope == BoardModeBoardScope.Team).ToList();
        var cells = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var seat in inRace)
        {
            cells[seat] = [.. seatLines.Select(l => l.Text(now.Seat(l, seat), seatName))];
        }
        IReadOnlyList<ModeTeamFigures> teamRows = teamLines.Count == 0 ? [] :
            [.. inRace.Select(s => teams is not null && s < teams.Count ? teams[s] : s)
                .Where(t => t is >= 0 and < ModeTeams.Most).Distinct().Order()
                .Select(t => new ModeTeamFigures(ModeTeams.Word(t), [.. teamLines.Select(l => new ModeFigure(l.Label, l.Text(now.Team(l, t), seatName)))]))];
        return new ModeBoardRows(
            [.. lines.Where(l => l.Scope == BoardModeBoardScope.Global).Select(l => new ModeFigure(l.Label, l.Text(now.Global(l), seatName)))],
            [.. seatLines.Select(l => l.Label)], cells, teamRows);
    }

    /// <summary>Whether the page would draw just this: the lines refresh 10 times a second, and most times nothing moved.</summary>
    public bool Same(ModeBoardRows other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Globals.SequenceEqual(other.Globals) && SeatHeads.SequenceEqual(other.SeatHeads) &&
            Teams.Count == other.Teams.Count &&
            Teams.Zip(other.Teams).All(t => t.First.Team == t.Second.Team && t.First.Lines.SequenceEqual(t.Second.Lines));
    }
}
