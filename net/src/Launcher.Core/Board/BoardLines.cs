using System.Globalization;
using System.Text.Json;
using Launcher.Core.Board;

namespace Launcher.Core;

/// <summary>A game's status board as lines of JSON (<c>{"event":"board","values":{...}}</c>), for the launcher's modes
/// without a window (a test visit, a race or replay from the command line): taken or refused, each phase, the first
/// race snapshot and one every ten seconds, a freeze judged and its end, and the game's events.</summary>
public static class BoardLines
{
    /// <summary>Snapshots between two lines after the first.</summary>
    public const int SnapshotEvery = 600;

    public static void Follow(GameBoard board, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(write);
        void Line(object values) => write(JsonSerializer.Serialize(new { @event = "board", values }));
        var snapshots = 0;
        board.TakenChanged += taken => Line(new { taken, file = Path.GetFileName(board.Path) });
        board.PhaseChanged += phase => Line(new { phase = phase.ToString(), beat = board.Heartbeat });
        board.Received += snapshot =>
        {
            if (snapshots++ % SnapshotEvery == 0)
            {
                Line(new { snapshot = snapshot.Frame, racers = snapshot.Racers.Count, localSeat = snapshot.LocalSeat, count = snapshots });
            }
        };
        board.FrozenChanged += frozen => Line(new
        {
            frozen,
            stood = Math.Round(board.Stood.TotalSeconds, 1),
            phase = board.Phase.ToString(),
            beat = board.Heartbeat,
            wholeProcess = board.WholeProcess,
        });
        board.EventRead += (e, lost) =>
        {
            if (e.Kind != BoardEventKind.Phase)
            {
                Line(new { kind = e.Kind.ToString(), e.A, e.B, e.Step, ms = e.Ms, lost });
            }
        };
    }

    /// <summary>The line said when the game has ended: what the board last held.</summary>
    public static string Last(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return JsonSerializer.Serialize(new
        {
            @event = "board",
            values = new
            {
                ended = true,
                phase = board.Phase.ToString(),
                beat = board.Heartbeat,
                stop = board.StopReason.ToString(),
                lastFrame = board.Latest?.Frame.ToString(CultureInfo.InvariantCulture),
            },
        });
    }
}
