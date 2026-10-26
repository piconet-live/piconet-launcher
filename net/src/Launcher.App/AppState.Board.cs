using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Launcher.Core;
using Launcher.Core.Board;

namespace Launcher.App;

/// <summary>The status board of the game this launcher runs (<see cref="GameBoard"/>): made for every start, named in the
/// game's launch file, looked at while the game runs and removed when it has ended. From it the launcher has the race
/// as it runs, where the game is, and whether it stands still; through it the launcher asks the game to stop.</summary>
[SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",
    Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
public sealed partial class AppState
{
    /// <summary>What the player is told of a game whose heartbeat has stood for <see cref="FreezeJudge.Default"/>; the
    /// notice offers to end it (<see cref="EndHungGame"/>).</summary>
    public const string HungText = "Game not responding";

    /// <summary>How long a game asked to stop through its board has before it is ended by its process.</summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);

    /// <summary>The board of the game that runs now, or of the last one until the next start (null before the first,
    /// or when none could be made: the game then runs without one).</summary>
    public GameBoard? Board { get; private set; }

    /// <summary>The game that runs stands still (its heartbeat stood <see cref="FreezeJudge.Default"/>).</summary>
    public bool Hung => Racing && Board is { Frozen: true };

    /// <summary>A board was made for a game about to start (tests and the modes without a window watch it).</summary>
    public event Action<GameBoard>? BoardMade;

    /// <summary>The game's window, with a new board for the game about to start; the board before is let go.</summary>
    private GameWindow WindowWithBoard()
    {
        Board?.Dispose();
        Board = null;
        GameBoard board;
        try
        {
            board = GameBoard.Create(Path.Combine(dataFolder, "boards"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Noted?.Invoke($"no status board for the game ({e.Message}): it runs without one");
            return GameWindow;
        }
        Board = board;
        board.TakenChanged += taken =>
        {
            if (!taken)
            {
                Noted?.Invoke("the game refused its status board (another version): no live race and no freeze watch for it");
            }
        };
        board.FrozenChanged += frozen =>
        {
            if (frozen)
            {
                Noted?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"the game has stood still for {board.Stood.TotalSeconds:0} s ({board.Phase}{(board.WholeProcess ? ", its whole process" : "")}): offered to end it"));
                Problem = HungText;
            }
            else
            {
                Noted?.Invoke("the game moves again");
                if (Problem == HungText)
                {
                    Problem = null;
                }
            }
            Changed?.Invoke();
        };
        BoardMade?.Invoke(board);
        board.Start();
        return GameWindow with { Board = board.Path };
    }

    /// <summary>The game ended: its board goes (the race's own end handler ran first), and so does a notice that it
    /// stood still.</summary>
    private void GameEnded(GameBoard? board)
    {
        board?.Dispose();
        if (Problem == HungText)
        {
            Problem = null;
        }
    }

    /// <summary>The notice's "End game": a game that stands still is ended by its process (this launcher's own handle:
    /// that process alone). What it recorded stays: a race's replay holds every step made final, and the race is kept
    /// for a rejoin as after a crash.</summary>
    public void EndHungGame()
    {
        if (!Hung || Fork is not { } game)
        {
            return;
        }
        Noted?.Invoke("the player ended the game that stood still");
        Kill(game);
    }

    /// <summary>Asks the game to stop through its board, as its window's close would; one still running after
    /// <paramref name="grace"/> (it stands still, or has no board) is ended by its process.</summary>
    private async Task StopGameAsync(Process game, TimeSpan grace)
    {
        if (Fork == game)
        {
            Board?.AskStop();
        }
        using var wait = new CancellationTokenSource(grace);
        try
        {
            await game.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(game);
        }
    }

    private static void Kill(Process game)
    {
        Ended.AddOrUpdate(game, Ended);
        try
        {
            game.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // it exited meanwhile, or cannot be ended from here
        }
    }

    /// <summary>The games this launcher ended by their process (their exit code is its, not an error of theirs).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Process, object> Ended = new();

    private static bool EndedByLauncher(Process game) => Ended.TryGetValue(game, out _);

    /// <summary>For a launcher that ends with its game still running (a test visit's end): asked to stop, then ended.</summary>
    public void StopGame(TimeSpan grace)
    {
        if (Fork is { HasExited: false } game)
        {
            StopGameAsync(game, grace).Wait(grace + TimeSpan.FromSeconds(5));
        }
    }
}
