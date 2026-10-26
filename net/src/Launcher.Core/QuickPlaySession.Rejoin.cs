using Online.Protocol;

namespace Launcher.Core;

// A race the player's game left while it ran, and going back into it.
public sealed partial class QuickPlaySession
{
    /// <summary>The race this player's game left while it ran (<see cref="RaceOverAsync"/>); null: none.</summary>
    public ForkRace? RejoinableRace { get; private set; }

    /// <summary>The player can go back into the race their game left: the lobby still races it, and its race server
    /// has not said it is over (<see cref="LobbyRace.Over"/>).</summary>
    public bool CanRejoin => RejoinableRace is { } left && Race is null &&
        Lobby is { Stage: LobbyState.Racing, Race: { Over: false } race } && race.MatchId == left.Start.MatchId;

    /// <summary>Starts the game again into the race it left (the same match file and replay path, the launch file's
    /// <c>rejoin</c>): the race server takes the seat back, or not. False: nothing to rejoin.</summary>
    public bool Rejoin()
    {
        if (!CanRejoin)
        {
            return false;
        }
        Race = RejoinableRace;
        RejoinableRace = null;
        Set("Racing");
        startFork(Race!);
        return true;
    }
}
