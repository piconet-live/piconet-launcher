using System.Globalization;

namespace Launcher.Core;

/// <summary>
/// One value of a machine, as the Garage shows and sorts by it. The names are the game's where its machine select
/// shows the value (body, boost, grip, the weight) and otherwise the community's. The names
/// are written into the settings (<see cref="MachineSort"/>) and are what a stat pack would name its columns by, so
/// they are added to and never renamed.
/// </summary>
public enum MachineStat
{
    /// <summary>The machine's number in the game's machine select: its id plus one. Every table has it.</summary>
    Number,

    /// <summary>The three letters of the machine select, A (1) to E (5).</summary>
    BodyRating,
    BoostRating,
    GripRating,

    /// <summary>The weight in kilograms, as the machine select shows it.</summary>
    Weight,
    Acceleration,
    MaxSpeed,

    /// <summary>What multiplies the damage a machine takes: the smaller, the tougher.</summary>
    Body,
    BoostStrength,
    BoostDuration,
    Grip1,
    Grip2,
    Grip3,
    TurnTension,
    TurnMovement,
    TurnReaction,
    TurnDeceleration,
    DriftAcceleration,
    Strafe,
    StrafeTurn,
    Drag,
    CameraReorienting,
    CameraRepositioning,
}

/// <summary>How a stat's value reads.</summary>
public enum MachineStatKind
{
    /// <summary>The machine's number.</summary>
    Number,

    /// <summary>A letter, A to E.</summary>
    Rating,

    /// <summary>Kilograms.</summary>
    Weight,

    /// <summary>A number of the game's own, without a unit.</summary>
    Value,
}

/// <summary>A stat as the launcher knows it: its label on the Garage page, how its value reads, where it stands in a
/// machine's block of the car data file (<see cref="MachineStatFile"/>; -1: not there), and whether the Garage shows
/// it (the two camera values are a machine's, and no measure of one).</summary>
public sealed record MachineStatInfo(MachineStat Stat, string Label, MachineStatKind Kind, int At = -1, bool Shown = true)
{
    /// <summary>The two orders a list can be put in by this stat, in words: the first is rising values.</summary>
    public (string Rising, string Falling) Orders => Kind switch
    {
        MachineStatKind.Number => ("1 to 41", "41 to 1"),
        MachineStatKind.Rating => ("A to E", "E to A"),
        _ => ("low to high", "high to low"),
    };
}

/// <summary>Every stat, in the order the Garage lists them.</summary>
public static class MachineStats
{
    public static IReadOnlyList<MachineStatInfo> All { get; } =
    [
        new(MachineStat.Number, "Number", MachineStatKind.Number),
        new(MachineStat.BodyRating, "Body", MachineStatKind.Rating),
        new(MachineStat.BoostRating, "Boost", MachineStatKind.Rating),
        new(MachineStat.GripRating, "Grip", MachineStatKind.Rating),
        new(MachineStat.Weight, "Weight", MachineStatKind.Weight, 0x04),
        new(MachineStat.Acceleration, "Acceleration", MachineStatKind.Value, 0x08),
        new(MachineStat.MaxSpeed, "Max speed", MachineStatKind.Value, 0x0C),
        new(MachineStat.Body, "Body value", MachineStatKind.Value, 0x44),
        new(MachineStat.BoostStrength, "Boost strength", MachineStatKind.Value, 0x34),
        new(MachineStat.BoostDuration, "Boost duration", MachineStatKind.Value, 0x38),
        new(MachineStat.Grip1, "Grip 1", MachineStatKind.Value, 0x10),
        new(MachineStat.Grip2, "Grip 2", MachineStatKind.Value, 0x30),
        new(MachineStat.Grip3, "Grip 3", MachineStatKind.Value, 0x14),
        new(MachineStat.TurnTension, "Turn tension", MachineStatKind.Value, 0x18),
        new(MachineStat.TurnMovement, "Turn movement", MachineStatKind.Value, 0x20),
        new(MachineStat.TurnReaction, "Turn reaction", MachineStatKind.Value, 0x2C),
        new(MachineStat.TurnDeceleration, "Turn deceleration", MachineStatKind.Value, 0x3C),
        new(MachineStat.DriftAcceleration, "Drift acceleration", MachineStatKind.Value, 0x1C),
        new(MachineStat.Strafe, "Strafe", MachineStatKind.Value, 0x28),
        new(MachineStat.StrafeTurn, "Strafe turn", MachineStatKind.Value, 0x24),
        new(MachineStat.Drag, "Drag", MachineStatKind.Value, 0x40),
        new(MachineStat.CameraReorienting, "Camera reorienting", MachineStatKind.Value, 0x4C, Shown: false),
        new(MachineStat.CameraRepositioning, "Camera repositioning", MachineStatKind.Value, 0x50, Shown: false),
    ];

    /// <summary>The stats the Garage lists.</summary>
    public static IReadOnlyList<MachineStatInfo> Shown { get; } = [.. All.Where(s => s.Shown)];

    /// <summary>Whether this is one of the stats (a settings file can hold any number).</summary>
    public static bool Has(MachineStat stat) => (int)stat >= 0 && (int)stat < All.Count;

    public static MachineStatInfo Of(MachineStat stat) => All[(int)stat];

    /// <summary>A value as the Garage writes it: "7", "B", "1260 kg", "0.45"; "" when it is not known.</summary>
    public static string Text(MachineStat stat, float? value) => value is not { } v || !Has(stat) ? "" : Of(stat).Kind switch
    {
        MachineStatKind.Rating => v is >= 1 and <= 5 ? ((char)('A' + (int)v - 1)).ToString() : "",
        MachineStatKind.Weight => string.Create(CultureInfo.InvariantCulture, $"{v:0.#} kg"),
        _ => v.ToString("0.####", CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Every machine's stats as one table: a value for a machine and a stat, or none where it is not known (the letters
/// come from another file of the disc than the numbers, and a disc can give one without the other). The table does
/// not say where its values came from: the launcher's is read from the player's disc
/// (<see cref="DiscArt.MachineStats()"/>), and a stat pack (a table of changed values for some machines) is laid over
/// it with <see cref="With(Online.Protocol.StatPack)"/>, after which everything that shows or sorts by stats goes on
/// as before.
/// </summary>
public sealed class MachineStatTable
{
    private readonly float?[] _values;   // a row a machine, a column a stat

    private MachineStatTable(float?[] values) => _values = values;

    /// <summary>A table of what <paramref name="value"/> gives for each machine (0 to 40) and stat; null: not known.
    /// A machine's number is not asked: it is its id plus one.</summary>
    public static MachineStatTable Of(Func<int, MachineStat, float?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var stats = MachineStats.All.Count;
        var values = new float?[Machines.Names.Count * stats];
        for (var machine = 0; machine < Machines.Names.Count; machine++)
        {
            values[machine * stats] = machine + 1;
            for (var stat = 1; stat < stats; stat++)
            {
                values[(machine * stats) + stat] = value(machine, (MachineStat)stat) is { } v && float.IsFinite(v) ? v : null;
            }
        }
        return new MachineStatTable(values);
    }

    /// <summary>A machine's value, or null when the table does not have it (or there is no such machine or stat).</summary>
    public float? Value(int machine, MachineStat stat) =>
        Machines.Has(machine) && MachineStats.Has(stat) ? _values[(machine * MachineStats.All.Count) + (int)stat] : null;

    /// <summary>Whether the table has this stat for any machine.</summary>
    public bool Knows(MachineStat stat) => Enumerable.Range(0, Machines.Names.Count).Any(m => Value(m, stat) is not null);

    /// <summary>A machine's value as the Garage writes it: "7", "B", "1260 kg", "0.45"; "" when it is not known.</summary>
    public string Text(int machine, MachineStat stat) => MachineStats.Text(stat, Value(machine, stat));

    /// <summary>This table with one value another: how a stat pack's changed values are laid over the disc's. The
    /// machine's number is not a value to change, and a value that is no number changes nothing.</summary>
    public MachineStatTable With(int machine, MachineStat stat, float value)
    {
        if (!Machines.Has(machine) || !MachineStats.Has(stat) || stat == MachineStat.Number || !float.IsFinite(value))
        {
            return this;
        }
        var values = (float?[])_values.Clone();
        values[(machine * MachineStats.All.Count) + (int)stat] = value;
        return new MachineStatTable(values);
    }

    /// <summary>This table with a stat pack laid over it (the protocol's handbook/stat-packs.md): every value of the pack
    /// that is one of a machine's stats here. What else a pack holds has no place in this table and changes nothing
    /// in it: the custom parts, a machine's two bytes and its corner points. The letters stay the disc's: they are
    /// another table of the game's, which a pack does not carry.</summary>
    public MachineStatTable With(Online.Protocol.StatPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var values = (float?[])_values.Clone();
        foreach (var entry in pack.Entries)
        {
            if (Machines.Has(entry.Block) && MachineStats.All.FirstOrDefault(s => s.At == entry.At) is { } stat && float.IsFinite(entry.Value))
            {
                values[(entry.Block * MachineStats.All.Count) + (int)stat.Stat] = entry.Value;
            }
        }
        return new MachineStatTable(values);
    }
}

/// <summary>
/// The order a list of machines is shown in: by a stat, its values rising or falling (the Garage's choice, kept in
/// the settings). It orders what is shown and nothing else: a machine is its id in the settings, every message and
/// the game, whatever its place in a list.
/// </summary>
public sealed record MachineSort(MachineStat By = MachineStat.Number, bool Descending = false)
{
    /// <summary>By number, 1 to 41: the game's own order, and the order without a choice.</summary>
    public static MachineSort ByNumber { get; } = new();

    /// <summary>The machines' ids in this order. Machines whose value is the same, or not known, stand by number,
    /// and those whose value is not known come last either way; without a table every machine stands by number.</summary>
    public IReadOnlyList<int> Order(MachineStatTable? table)
    {
        var ids = Enumerable.Range(0, Machines.Names.Count);
        if (By == MachineStat.Number || !MachineStats.Has(By))
        {
            return [.. Descending && MachineStats.Has(By) ? ids.Reverse() : ids];
        }
        var known = ids.Where(m => table?.Value(m, By) is not null);
        var ordered = Descending ? known.OrderByDescending(m => table!.Value(m, By)) : known.OrderBy(m => table!.Value(m, By));
        return [.. ordered.ThenBy(m => m), .. ids.Where(m => table?.Value(m, By) is null)];
    }
}
