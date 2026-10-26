using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The machine a racer races: a garage-built machine (<see cref="Custom"/>) when they have one, which the game races
/// in place of the stock machine, else the stock machine <see cref="Machine"/> (0-40). The stock id is always there
/// (a server checks it against the machines it allows, and a custom seat sends it beside the parts), so a page that
/// reads <see cref="Machine"/> alone shows the wrong machine: every page asks this record (else both
/// seem selected, and the stock machine wins).
/// </summary>
public sealed record RacedMachine(int Machine, CustomMachine? Custom)
{
    /// <summary>A custom machine's name while the disc's naming tables are not read (or cannot be): never the stock
    /// machine's.</summary>
    public const string UnnamedCustom = "Custom machine";

    /// <summary>A custom machine is raced.</summary>
    public bool IsCustom => Custom is not null;

    /// <summary>The machine's name: a custom machine's by the game's rule from its parts
    /// (<see cref="CustomPartTables.NameOf"/>; <see cref="UnnamedCustom"/> without the tables), a stock machine's
    /// from the game's list ("" for an id that is none).</summary>
    public string Name(CustomPartTables? tables) =>
        Custom is { } c ? (tables?.NameOf(c.Body, c.Cockpit, c.Booster) is { Length: > 0 } name ? name : UnnamedCustom)
        : Machines.Has(Machine) ? Machines.Names[Machine] : "";

    /// <summary>The same machine raced: the same stock id and the same custom machine, colours included.</summary>
    public bool Same(RacedMachine? other) =>
        other is not null && Machine == other.Machine && (Custom is null ? other.Custom is null
            : other.Custom is not null && Custom with { Colors = null } == other.Custom with { Colors = null }
              && (Custom.Colors ?? []).SequenceEqual(other.Custom.Colors ?? []));
}
