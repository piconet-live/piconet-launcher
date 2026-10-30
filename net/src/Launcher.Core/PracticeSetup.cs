using System.Text.Json.Serialization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>A practice run as the player sets it up on Play's Practice page: solo, alone, no
/// CPU racers, in the game's own Practice mode, with a minute's rewind and one save; its rules are a custom lobby's
/// for one player (<see cref="HostRules.Alone"/>), Quick Play's until the player changes
/// them. Every value is kept inside the game's own ranges whatever is asked.</summary>
/// <param name="Course">The course's number (<see cref="CourseCups"/>).</param>
/// <param name="Machine">The machine, 0 to 40.</param>
/// <param name="StatPack">The stat pack's name, null for the disc's own stats.</param>
/// <param name="MachineSetting">The machine setting, 0 (acceleration) to 100 (top speed).</param>
/// <param name="Laps">1 to <see cref="MostLaps"/>, or <see cref="Unlimited"/>.</param>
/// <param name="Restore">The game's Restore rule (rule flag 0x8000): a machine that crashes or falls is put back on
/// the track instead of being out.</param>
/// <param name="BoostFromStart">Boosting allowed on the first lap.</param>
/// <param name="MineRegenMs">A mine comes back this long after it went off, in steps of 50 ms; 0: it stays gone.</param>
/// <param name="Custom">A garage-built machine raced in place of <paramref name="Machine"/>, as an online seat races
/// one (the seat's <c>custom</c>); null: the stock machine. Taken from the Garage at each start and not kept in the
/// settings' <c>practice</c> (written only when set); <see cref="Kept"/> keeps it only when the game can build it.</param>
public sealed record PracticeSetup(int Course, int Machine, string? StatPack, int MachineSetting, int Laps, bool Restore = false,
    bool BoostFromStart = false, int MineRegenMs = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomMachine? Custom = null)
{
    /// <summary>The most laps: two figures, as the game's lap counter shows them and the game's match reader takes
    /// them (port/core/src/files.h MAX_LAPS).</summary>
    public const int MostLaps = 99;

    /// <summary>No lap count: the game's free run (Practice's race type 3, which a match's laps 0 asks for: the race
    /// never ends by laps).</summary>
    public const int Unlimited = 0;

    /// <summary>How far a rewind reaches, in seconds (the game's rolling buffer).</summary>
    public const int RewindSeconds = 60;

    /// <summary>The stat pack a fresh practice opens on, by the pack's own name: a practice has no server to say one, so
    /// this launcher does (<see cref="Fresh"/>). A practice saved earlier keeps its own choice, Stock too.</summary>
    public const string FreshStatPack = "GX Unleashed";

    /// <summary>A practice with nothing saved: <see cref="Default"/> on <see cref="FreshStatPack"/>, or on the disc's stats
    /// when this launcher does not hold that pack's file.</summary>
    /// <param name="packs">The names of the packs this launcher holds.</param>
    public static PracticeSetup Fresh(IEnumerable<string> packs) =>
        Default with { StatPack = packs.Contains(FreshStatPack) ? FreshStatPack : null };

    /// <summary>The first course, the first machine, the disc's stats and Quick Play's rules.</summary>
    public static PracticeSetup Default { get; } = new(1, 0, null, HostRules.MachineSetting.Default, HostRules.Laps.Default,
        HostRules.Restore.Default != 0, HostRules.BoostStart.Default != 0, HostRules.MinesReturn.Default);

    /// <summary>The race's rules the game applies (a match's <c>setup.rules</c>): the set-up's; no CPUs, no finish
    /// limit (the one racer's finish ends the race).</summary>
    [JsonIgnore]
    public RaceRules Rules => new(BoostFromStart: BoostFromStart, Restore: Restore, MineRegenMs: MineRegenMs);

    /// <summary>The set-up with every value inside its range: an unknown course is the first one, a machine or a
    /// number outside its range the nearest end, the mines' delay on its steps.</summary>
    public PracticeSetup Kept() => this with
    {
        Course = CourseCups.CupOf(Course).Length > 0 ? Course : Default.Course,
        Machine = Math.Clamp(Machine, 0, 40),
        MachineSetting = HostRules.MachineSetting.Kept(MachineSetting),
        Laps = Laps == Unlimited ? Unlimited : Math.Clamp(Laps, 1, MostLaps),
        MineRegenMs = HostRules.MinesReturn.Kept(MineRegenMs),
        StatPack = StatPack is { Length: > 0 and <= 64 } pack && !pack.Any(char.IsControl) ? pack : null,
        Custom = CustomMachine.Checked(Custom),
    };

    /// <summary>The machine raced: the custom machine when there is one, else the stock one.</summary>
    [JsonIgnore]
    public RacedMachine Raced => new(Machine, Custom);
}
