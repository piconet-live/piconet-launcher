using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// What the lobby page shows (one lobby page with states, not a page per state). The lobby's
/// stage decides it, so everyone in the lobby sees the same.
/// </summary>
public enum LobbyScreen
{
    /// <summary>The lobby gathering: its players, the votes to start and for CPUs, chat.</summary>
    Gathering,

    /// <summary>A match forming: the course vote and its draw when the server has a vote, otherwise the wait for the
    /// race.</summary>
    Starting,

    /// <summary>The course decided: the racers choose their machines (the others wait for the race).</summary>
    Choosing,

    /// <summary>The lobby's race as it runs, raced or watched, with chat.</summary>
    Race,
}

/// <summary>The steps of the line across the lobby's top, in their order (one line that says
/// where the lobby is). A server without a course vote has neither the vote nor the draw.</summary>
public enum LobbyStep
{
    Gathering,
    CourseVote,
    Draw,
    Machines,
    Starting,
    Race,
}

/// <summary>One of the three moments the launcher makes itself heard at (a few sounds,
/// for the big moments): the course vote opens, the machine stage opens, the player's race starts;
/// and a fourth sound that is no lobby's: an urgent notice from the server (<see cref="NoticeLevel.Urgent"/>).</summary>
public enum LobbyMoment
{
    CourseVote,
    Machines,
    RaceStart,
    UrgentNotice,
}

public static class LobbyScreens
{
    /// <summary>Whether the lobby's Garage is open to this player: all through the lobby, the machine stage and
    /// every other step; the machine and colour selected there are what the player races.</summary>
    public static bool GarageOpen(bool inLobby) => inLobby;

    /// <summary>The screen for the lobby's stage; the gathering one while queueing (no lobby yet).</summary>
    public static LobbyScreen Of(LobbyState? lobby) => lobby?.Stage switch
    {
        LobbyState.Starting => LobbyScreen.Starting,
        LobbyState.Choosing => LobbyScreen.Choosing,
        LobbyState.Racing => LobbyScreen.Race,
        _ => LobbyScreen.Gathering,
    };

    /// <summary>The steps this server's lobbies go through: all six with a course vote, four without.</summary>
    public static IReadOnlyList<LobbyStep> Steps(bool courseVote) => courseVote
        ? [LobbyStep.Gathering, LobbyStep.CourseVote, LobbyStep.Draw, LobbyStep.Machines, LobbyStep.Starting, LobbyStep.Race]
        : [LobbyStep.Gathering, LobbyStep.Machines, LobbyStep.Starting, LobbyStep.Race];

    /// <summary>Where the lobby is on the line.</summary>
    /// <param name="courseVote">The server's lobbies vote for the course.</param>
    /// <param name="drawn">The vote's result has come.</param>
    /// <param name="running">The lobby's race is seen running (a game has reported it).</param>
    public static LobbyStep Step(LobbyState? lobby, bool courseVote, bool drawn, bool running) => lobby?.Stage switch
    {
        LobbyState.Starting when drawn => LobbyStep.Draw,
        LobbyState.Starting when courseVote => LobbyStep.CourseVote,
        // (a match forming without a vote: the relays are measured for a moment, the machines are next)
        LobbyState.Starting => LobbyStep.Machines,
        LobbyState.Choosing when lobby.Waiting is not null => LobbyStep.Starting,
        LobbyState.Choosing => LobbyStep.Machines,
        LobbyState.Racing when running => LobbyStep.Race,
        LobbyState.Racing => LobbyStep.Starting,
        _ => LobbyStep.Gathering,
    };

    /// <summary>The lobby's stat pack on the line: its name and, where the server names one, its author ("GX
    /// Unleashed · AKC12"); "" for a lobby on the original stats.</summary>
    public static string PackLabel(StatPackName? pack) =>
        pack is null ? "" : pack.Author is { Length: > 0 } by ? $"{pack.Name} · {by}" : pack.Name;

    /// <summary>The pack's label when it is pointed at: the label, then the game's code constants the pack changes, a
    /// line each ("Boost energy 0.15"), from the pack's values when the launcher has them (<paramref name="values"/>,
    /// the same fingerprint); the label alone otherwise.</summary>
    public static string PackTip(StatPackName? pack, StatPack? values)
    {
        var label = PackLabel(pack);
        if (pack is null || values is null || values.Fingerprint != pack.Fingerprint || values.Constants.Count == 0)
        {
            return label;
        }
        return string.Join('\n', [label, .. StatPackConstants.Described(values.Constants).Select(c => $"{c.Constant.What} {c.Value}")]);
    }

    /// <summary>A step's name on the line.</summary>
    public static string Name(LobbyStep step) => step switch
    {
        LobbyStep.CourseVote => "Course vote",
        LobbyStep.Draw => "Draw",
        LobbyStep.Machines => "Machines",
        LobbyStep.Starting => "Starting",
        LobbyStep.Race => "Race",
        _ => "Gathering",
    };

    /// <summary>The votes a start needs: more than half of the members present (those not out in a race the lobby
    /// moved on from, and not watching), as the server counts them.</summary>
    public static int StartVotesNeeded(LobbyState lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        return (lobby.Players.Count(p => !p.Racing && !p.Watching) / 2) + 1;
    }

    /// <summary>The lobby's racers: its members but those who watch its races (a watcher has no seat).</summary>
    public static int Racers(LobbyState lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        return lobby.Players.Count(p => !p.Watching);
    }

    /// <summary>Whether the launcher brings the player to the lobby page as its screen changes: for the course vote and
    /// the machine choice from any page (they last seconds and the choices are theirs), for the race only from the home
    /// page (a player busy with their settings or replays stays there).</summary>
    public static bool BringsToLobby(LobbyScreen before, LobbyScreen now, bool onHome) =>
        now != before && (now is LobbyScreen.Starting or LobbyScreen.Choosing || (now == LobbyScreen.Race && onHome));
}
