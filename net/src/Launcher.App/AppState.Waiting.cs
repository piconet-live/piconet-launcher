using System.Diagnostics;
using Launcher.Core;

namespace Launcher.App;

// Practice while the lobby waits (LobbyRecall), and the game ended at the player's word.
public sealed partial class AppState
{
    private LobbyRecall? _recall;
    private Process? _practiceGame;

    /// <summary>The lobby's call back to a player who practises while it gathers: begun the last
    /// <see cref="LobbyRecall.Standard"/> of its start countdown (or when it moves on without one), the practice game
    /// ended as the lobby starts; the game is told.</summary>
    public LobbyRecall Recall => _recall ??= MakeRecall();

    private LobbyRecall MakeRecall()
    {
        var recall = new LobbyRecall(LobbyRecall.Standard, EndPractice);
        recall.Began += () =>
        {
            Board?.Notice(recall.GameLine);
            Noted?.Invoke("the lobby calls the player back from their practice: it ends as the lobby starts");
        };
        return recall;
    }

    /// <summary>A practice game started while the player is in a lobby or its queue: the lobby may call them back.</summary>
    private void PractiseInLobby(Process game)
    {
        _practiceGame = game;
        Recall.Started();
        game.Exited += (_, _) =>
        {
            if (_practiceGame == game)
            {
                _practiceGame = null;
                Recall.Ended();
                Changed?.Invoke();
            }
        };
        HearRecall();
    }

    /// <summary>The lobby changed while the player practises: its start countdown's last seconds, or a lobby that moved
    /// on, begin the recall (<see cref="LobbyRecall.Heard"/>).</summary>
    private void HearRecall()
    {
        if (_practiceGame is not null && Session is { } session)
        {
            Recall.Heard(session.Lobby, DateTimeOffset.UtcNow, session.StartsAt);
        }
    }

    /// <summary>The recall's end (or "Back now"): the practice game is asked to stop, then ended.</summary>
    private void EndPractice()
    {
        if (_practiceGame is { HasExited: false } game)
        {
            _ = StopGameAsync(game, StopGrace);
        }
    }

    /// <summary>The redesigned interface's "End game" (after its question): the game this launcher started is asked
    /// to stop as its window's close would, then ended by its process. A race's game ended so keeps its race for
    /// Rejoin, as after a crash.</summary>
    public void EndGame()
    {
        if (Fork is { HasExited: false } game)
        {
            Noted?.Invoke("the player ended the game");
            _ = StopGameAsync(game, StopGrace);
        }
    }
}
