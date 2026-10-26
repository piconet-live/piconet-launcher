using Online.Protocol;

namespace Launcher.Core;

/// <summary>A racer's own pack in a lobby with mixed packs (the protocol's handbook/stat-pack-constants.md, "A pack a seat").</summary>
public sealed partial class QuickPlaySession
{
    /// <summary>In a lobby with mixed packs (<see cref="LobbyState.SeatPacks"/>): the pack the player's machine races, a
    /// pack's fingerprint, "" the original stats; null: the lobby's own. It goes with the next machine choice, and with
    /// the choice already made when there is one.</summary>
    public string? SeatPack { get; private set; }

    public Task ChooseSeatPackAsync(string? fingerprint)
    {
        SeatPack = fingerprint;
        Changed?.Invoke();
        return Machine is { } machine && ChoiceOpen && Choice?.Mine is not null ? ChooseMachineAsync(machine) : Task.CompletedTask;
    }
}
