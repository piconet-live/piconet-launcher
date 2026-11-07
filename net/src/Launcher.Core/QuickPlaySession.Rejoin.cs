using Online.Protocol;

namespace Launcher.Core;

// A race the player's game left while it ran, and going back into it.
public sealed partial class QuickPlaySession
{
    /// <summary>The race this player's game left while it ran (<see cref="RaceOverAsync"/>); null: none.</summary>
    public ForkRace? RejoinableRace { get; private set; }

    /// <summary>The player can go back into the race their game left: the lobby still races it, and its race server
    /// has not said it is over (<see cref="LobbyRace.Over"/>).</summary>
    public bool CanRejoin => RejoinableRace is { Start.NoRejoin: false } left && Race is null &&
        Lobby is { Stage: LobbyState.Racing, Race: { Over: false } race } && race.MatchId == left.Start.MatchId;

    /// <summary>What a player reads who asks to rejoin a race whose mode has no rejoin.</summary>
    public const string NoRejoinText = "This mode has no CPUs to drive your seat, so you can't rejoin. You can watch the race.";

    /// <summary>The race this player's game left while it ran, of a mode without rejoin (<see cref="MatchStart.NoRejoin"/>):
    /// the seat is gone, and the race can only be watched. Null: none.</summary>
    public ForkRace? LeftForGood { get; private set; }

    /// <summary>The player was refused a rejoin, and has not left this race behind yet (<see cref="NoRejoinText"/>).</summary>
    public string? RejoinRefused { get; private set; }

    private string? _watchingOnlyMatch;

    /// <summary>Watch is offered where Rejoin would be: the lobby still races the race the player left for good.</summary>
    public bool CanWatchLeftRace => LeftForGood is { } left && Race is null && CanWatch && !Watching &&
        Lobby is { Stage: LobbyState.Racing, Race: { Over: false } race } && race.MatchId == left.Start.MatchId;

    /// <summary>Watches the race the player left for good, as any watcher (the server's <see cref="WatchStart"/>); they
    /// race again from the lobby's next race, this being no choice to stop racing.</summary>
    public Task WatchLeftRaceAsync()
    {
        if (!CanWatchLeftRace)
        {
            return Task.CompletedTask;
        }
        _watchingOnlyMatch = LeftForGood!.Start.MatchId;
        return WatchAsync(true);
    }

    /// <summary>The lobby moved on from the race the player left for good: nothing of it stays, and a player who
    /// watched it alone races again.</summary>
    private void LeftRaceBehind(LobbyState lobby)
    {
        if (LeftForGood is { } left && lobby.Race?.MatchId != left.Start.MatchId)
        {
            LeftForGood = null;
            RejoinRefused = null;
        }
        if (_watchingOnlyMatch is { } match && lobby.Race?.MatchId != match)
        {
            _watchingOnlyMatch = null;
            _ = WatchAsync(false);
        }
    }

    /// <summary>Starts the game again into the race it left (the same match file and replay path, the launch file's
    /// <c>rejoin</c>): the race server takes the seat back, or not. False: nothing to rejoin.</summary>
    public bool Rejoin()
    {
        if (LeftForGood is not null)
        {
            RejoinRefused = NoRejoinText;
            Changed?.Invoke();
            return false;
        }
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
