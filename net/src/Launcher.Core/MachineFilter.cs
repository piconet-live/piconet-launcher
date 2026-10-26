namespace Launcher.Core;

/// <summary>
/// What the Garage's search box and its Sort &amp; filter panel leave on a shelf: a machine shows when its name holds every word typed (any case, any order) and, for each of body, boost
/// and grip with letters chosen, its letter is one of them (none chosen: every letter). The letters are the ones the
/// page shows. The game groups machines by no weight class, so neither does the Garage.
/// </summary>
/// <param name="Body">The body letters chosen, a bit each: A the lowest; 0 none.</param>
public sealed record MachineFilter(string Query = "", int Body = 0, int Boost = 0, int Grip = 0)
{
    /// <summary>The letters, in their order.</summary>
    public const string Letters = "ABCDE";

    /// <summary>The stats filtered by letter, with their labels, in the order they stand.</summary>
    public static IReadOnlyList<(MachineStat Stat, string Label)> Graded { get; } =
        [(MachineStat.BodyRating, "Body"), (MachineStat.BoostRating, "Boost"), (MachineStat.GripRating, "Grip")];

    /// <summary>Nothing typed and no letter chosen: every machine shows.</summary>
    public bool Open => string.IsNullOrWhiteSpace(Query) && !Lettered;

    /// <summary>Some letter is chosen.</summary>
    public bool Lettered => (Body | Boost | Grip) != 0;

    /// <summary>How many letters are chosen, of all three stats.</summary>
    public int LetterCount => System.Numerics.BitOperations.PopCount((uint)Body) + System.Numerics.BitOperations.PopCount((uint)Boost)
        + System.Numerics.BitOperations.PopCount((uint)Grip);

    /// <summary>The letters chosen for one of the graded stats (0 for any other).</summary>
    public int LettersOf(MachineStat stat) => stat switch
    {
        MachineStat.BodyRating => Body,
        MachineStat.BoostRating => Boost,
        MachineStat.GripRating => Grip,
        _ => 0,
    };

    /// <summary>Whether a letter (0 for A) is chosen for a stat.</summary>
    public bool Has(MachineStat stat, int letter) => (LettersOf(stat) & (1 << letter)) != 0;

    /// <summary>The filter with a stat's letter (0 for A) chosen, or no longer when it was.</summary>
    public MachineFilter Toggled(MachineStat stat, int letter)
    {
        if (letter is < 0 or >= 5)
        {
            return this;
        }
        var bit = 1 << letter;
        return stat switch
        {
            MachineStat.BodyRating => this with { Body = Body ^ bit },
            MachineStat.BoostRating => this with { Boost = Boost ^ bit },
            MachineStat.GripRating => this with { Grip = Grip ^ bit },
            _ => this,
        };
    }

    /// <summary>Whether a machine of this name and these values shows (<paramref name="value"/>: its letters, 1 for
    /// A; null where not known, which no chosen letter takes).</summary>
    public bool Matches(string name, Func<MachineStat, float?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var words = (Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!words.All(w => (name ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return Graded.All(g => LettersOf(g.Stat) == 0 || (value(g.Stat) is { } v && v is >= 1 and <= 5 && Has(g.Stat, (int)v - 1)));
    }
}
