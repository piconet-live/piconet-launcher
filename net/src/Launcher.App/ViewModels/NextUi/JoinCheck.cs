using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>The redesigned interface's check before a race is joined: what RACE, a server, a Quick Play lobby and a
/// custom lobby ask (<see cref="PlayControlViewModel.MayJoin"/>, <see cref="CustomLobbiesViewModel.MayJoin"/>).</summary>
public static class JoinCheck
{
    /// <summary>Whether a race can be joined now (RACE, a server, a Quick Play or custom lobby). When not, the player
    /// is shown the way at once, never left with a press that does nothing: game files that are not ready open the
    /// panel that prepares them; any other reason (no disc image, no game program, the game open) is said, with OK. A
    /// join on its way is no reason to say: its first press is still at work. The stand-in's lobbies need no game.</summary>
    public static bool CanJoin(ShellViewModel shell, AppState state, Action again)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(again);
        if (state.Session is { Custom: true, Lobby: not null })
        {
            // still in a custom lobby: left after a question, and the join goes on
            shell.Ask("Leave the custom lobby?", "You're still in a custom lobby. Leave it to join this race.", "Leave and join",
                () => _ = LeaveAndAsync(state, again));
            return false;
        }
        return Ready(shell, state);
    }

    /// <summary>Whether the player may move from the custom lobby they are in to another of the list (its row
    /// pressed, or Create): always asked first, "Leave LOBBY?"; on its yes, once nothing else stands in the way
    /// (<see cref="Ready"/>), the move is made. The visit is not ended for it: the server takes the player out of
    /// their lobby when the next one takes them, so the list stays, and the lobby chosen is joined (before, the whole
    /// visit was left and nothing joined: the list stood empty).</summary>
    public static bool CanMove(ShellViewModel shell, AppState state, string from, Action move)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(move);
        shell.Ask($"Leave {from}?", "You're in this custom lobby. Leave it for the one you chose.", "Leave and go", () =>
        {
            if (Ready(shell, state))
            {
                move();
            }
        });
        return false;
    }

    /// <summary>Whether nothing but the player's lobby stands in the way of a race: the game's files ready, no game
    /// open, a disc. When something does, the way is shown (the panel that prepares the files, or the reason).</summary>
    private static bool Ready(ShellViewModel shell, AppState state)
    {
        if (!state.StandIn && PrepareSheetViewModel.Wanted(state))
        {
            shell.OpenPrepare();
            return false;
        }
        if (state.WhyNotJoin is not { } why)
        {
            return true;
        }
        if (why.Length > 0)
        {
            shell.Ask("Can't race yet", why switch
            {
                AppState.GameOpenText => "F-Zero GX is open. Close the game to join a race.",
                AppState.NoDiscText => "No disc image is set. Choose yours under Settings, Launcher.",
                _ => why + ".",
            }, "", () => { }, keep: "OK");
        }
        return false;
    }

    /// <summary>The check set on the pages that join while the redesigned interface shows (RACE, the servers, the
    /// lobbies, the custom lobbies), and taken off in the classic one, which has its own ways.</summary>
    public static void Hear(ShellViewModel shell, MainViewModel main, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.ExperimentalUi == (main.Control.MayJoin is not null))
        {
            return;
        }
        Func<Action, bool>? may = main.ExperimentalUi ? again => CanJoin(shell, state, again) : null;
        Func<string, Action, bool>? move = main.ExperimentalUi ? (from, go) => CanMove(shell, state, from, go) : null;
        (main.Control.MayJoin, main.Lobbies.MayJoin, main.Lobbies.MayMove) = (may, may, move);
        main.Control.NoOpenLobby = main.ExperimentalUi ? answered => NoRace(shell, answered) : null;
    }

    /// <summary>A join of a race (RACE, a server's "Join a race") found no Quick Play lobby open: said, with OK alone,
    /// and nothing is joined nor made (the owner, 2026-11-15: "Host a lobby" is the way to a new one). With no answer
    /// from the server, that is said.</summary>
    public static void NoRace(ShellViewModel shell, bool answered)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Ask(answered ? NoRaceTitle : "No answer",
            answered ? "Nobody is in a Quick Play lobby on this server right now. Host a lobby, and the next racers join you."
                : "The server did not say which lobbies are open. Try again in a moment.",
            "", () => { }, keep: "OK");
    }

    public const string NoRaceTitle = "No race found";

    private static async Task LeaveAndAsync(AppState state, Action again)
    {
        await state.LeaveAsync().ConfigureAwait(true);
        again();
    }
}
