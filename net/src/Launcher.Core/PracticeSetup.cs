using System.Text.Json.Serialization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A practice run as the player sets it up on Play's Practice page: solo, alone, no
/// CPU racers, in Practice's game mode (<see cref="PracticeMode"/>), whose parameters are the page's Practice rows as a
/// custom lobby's mode's are its host's; beside them a custom lobby's rules that mean something alone
/// (<see cref="HostRules.Alone"/>), Quick Play's until the player changes them. Every value is kept inside the game's
/// own ranges whatever is asked.</summary>
/// <param name="Course">The course's number (<see cref="CourseCups"/>).</param>
/// <param name="Machine">The machine, 0 to 40.</param>
/// <param name="StatPack">The stat pack's name, null for the disc's own stats.</param>
/// <param name="MachineSetting">The machine setting, 0 (acceleration) to 100 (top speed).</param>
/// <param name="BoostFromStart">Boosting allowed on the first lap.</param>
/// <param name="MineRegenMs">A mine comes back this long after it went off, in steps of 50 ms; 0: it stays gone.</param>
/// <param name="Params">The mode's parameters by their names in the mode file, as the player last set them; one the
/// mode no longer has is left alone, and one it has but the set-up does not name is on its default.</param>
/// <param name="Custom">A garage-built machine raced in place of <paramref name="Machine"/>, as an online seat races
/// one (the seat's <c>custom</c>); null: the stock machine. Taken from the Garage at each start and not kept in the
/// settings' <c>practice</c> (written only when set); <see cref="Kept"/> keeps it only when the game can build it.</param>
public sealed record PracticeSetup(int Course, int Machine, string? StatPack, int MachineSetting, bool BoostFromStart = false,
    int MineRegenMs = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParamDictionary? Params = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomMachine? Custom = null)
{
    /// <summary>The most parameters a set-up keeps.</summary>
    public const int MostParams = 64;

    /// <summary>The longest parameter name a set-up keeps.</summary>
    public const int LongestParam = 32;

    /// <summary>The first course, the first machine, the disc's stats, the mode's own parameters and Quick Play's
    /// other rules.</summary>
    public static PracticeSetup Default { get; } = new(1, 0, null, HostRules.MachineSetting.Default,
        HostRules.BoostStart.Default != 0, HostRules.MinesRespawn.Default);

    /// <summary>The laps of a settings file written before the page's rows were the mode's (0: unlimited); read, never
    /// written: the mode's parameter that sets the laps takes it until the next start (<see cref="PracticeMode.Values"/>).</summary>
    [JsonPropertyName("laps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OldLaps { get; init; }

    /// <summary>The Restore of a settings file written before the page's rows were the mode's; read, never written, as
    /// <see cref="OldLaps"/>: the mode's parameter labelled Restore takes it.</summary>
    [JsonPropertyName("restore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OldRestore { get; init; }

    /// <summary>The race's rules the game applies (a match's <c>setup.rules</c>): the set-up's; no CPUs, no finish
    /// limit (the one racer's finish ends the race). Restore is the mode's (its seat's <c>respawns</c>).</summary>
    [JsonIgnore]
    public RaceRules Rules => new(BoostFromStart: BoostFromStart, MineRegenMs: MineRegenMs);

    /// <summary>The set-up with every value inside its range: an unknown course is the first one, a machine or a
    /// number outside its range the nearest end, the mines' delay on its steps; of the parameters, at most
    /// <see cref="MostParams"/> with names a mode file can have (each one's range is the mode's,
    /// <see cref="PracticeMode.Values"/>).</summary>
    public PracticeSetup Kept() => this with
    {
        Course = CourseCups.CupOf(Course).Length > 0 ? Course : Default.Course,
        Machine = Math.Clamp(Machine, 0, 40),
        MachineSetting = HostRules.MachineSetting.Kept(MachineSetting),
        MineRegenMs = HostRules.MinesRespawn.Kept(MineRegenMs),
        StatPack = StatPack is { Length: > 0 and <= 64 } pack && !pack.Any(char.IsControl) ? pack : null,
        Params = Params is null ? null : new ParamDictionary(Params.Where(p => IsName(p.Key)).Take(MostParams)),
        OldLaps = OldLaps is { } laps ? Math.Clamp(laps, 0, GameFacts.Limits.LapsMax) : null,
        Custom = CustomMachine.Checked(Custom),
    };

    /// <summary>A name a mode file's parameter can have: letters, digits and underscores, not too long.</summary>
    private static bool IsName(string name) =>
        name is { Length: > 0 and <= LongestParam } && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The machine raced: the custom machine when there is one, else the stock one.</summary>
    [JsonIgnore]
    public RacedMachine Raced => new(Machine, Custom);
}

/// <summary>A practice mode's parameter values by the parameter's name, in the mode file's order; two are equal when
/// they hold the same names and values (a settings file read back is the one written).</summary>
public sealed class ParamDictionary : Dictionary<string, int>, IEquatable<ParamDictionary>
{
    public ParamDictionary()
        : base(StringComparer.Ordinal)
    {
    }

    public ParamDictionary(IEnumerable<KeyValuePair<string, int>> values)
        : base(StringComparer.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (name, value) in values)
        {
            this[name] = value;
        }
    }

    public bool Equals(ParamDictionary? other) =>
        other is not null && Count == other.Count && this.All(p => other.TryGetValue(p.Key, out var v) && v == p.Value);

    public override bool Equals(object? obj) => Equals(obj as ParamDictionary);

    public override int GetHashCode() => this.Aggregate(Count, (hash, p) => hash ^ HashCode.Combine(p.Key, p.Value));
}
