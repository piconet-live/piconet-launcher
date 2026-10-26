using Online.Protocol;

namespace Launcher.Core;

/// <summary>The 41 machines a racer can choose, by the game's machine id (<see cref="GameMachines"/>): the thirty of
/// the Grand Prix (0 to 29), Dark Schneider (30) and the ten from the arcade game (31 to 40). The names are the one
/// list the server's status page has too (<see cref="GameMachines.Names"/>): a list can name a machine without the
/// disc (a replay's row, the lobby). Each machine's files on the disc: <see cref="DiscArt.MachineFiles"/>.</summary>
public static class Machines
{
    public static IReadOnlyList<string> Names => GameMachines.Names;

    /// <summary>Whether this is a machine's id.</summary>
    public static bool Has(int id) => GameMachines.Has(id);
}
