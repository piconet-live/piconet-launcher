using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The status board of one running game: a small file in the launcher's own data folder that the launcher and the
/// game both map (protocol/board.toml; protocol/formats.md section 8; no localhost socket,
/// no pipe). The launcher makes it before the game starts and names it in the game's launch file
/// (<see cref="GameWindow.Board"/>); the game takes it and writes its heartbeat, where it is, the race as it runs,
/// why it stops, and events; the launcher writes the mailbox (a graceful stop, a notice). Neither ever waits on the
/// other: the launcher looks every <see cref="Every"/> on a thread of its own. Underneath stays the floor: the game's
/// exit code and its log's stop lines.
/// </summary>
public sealed class GameBoard : IDisposable
{
    /// <summary>How often the board is looked at: about every picture (the race page draws what it gets).</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMilliseconds(15);

    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly BoardView _view;
    private readonly FreezeJudge _judge;
    private readonly BoardStatus _status = new();
    private readonly BoardSnapshot _snapshot = new();
    private readonly BoardMailbox _mail = new();
    private readonly BoardModeHead _modeHead = ModeBoardHead.Blank();
    private readonly BoardModeValues _modeValues = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _writing = new();
    private uint _serial;
    private uint _modeSerial;
    private ModeBoardHead? _mode;
    private ulong _nextEvent;
    private int _disposed;

    private unsafe GameBoard(MemoryMappedFile map, string path, FreezeJudge judge)
    {
        Path = path;
        _judge = judge;
        _map = map;
        _accessor = _map.CreateViewAccessor(0, BoardLayout.Size);
        byte* at = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
        _view = new BoardView(at + _accessor.PointerOffset);
        _view.Make((uint)Environment.ProcessId);
    }

    /// <summary>Makes a board in <paramref name="folder"/> (<see cref="BoardFile.Make"/>).</summary>
    /// <exception cref="IOException">The folder or the file cannot be written.</exception>
    public static GameBoard Create(string folder, TimeSpan? frozenAfter = null)
    {
        var map = BoardFile.Make(folder, out var path);
        return new GameBoard(map, path, new FreezeJudge(frozenAfter ?? FreezeJudge.Default));
    }

    /// <summary>The board's file, for the game's launch file.</summary>
    public string Path { get; }

    /// <summary>Whether the game took the board: null not yet; false it refused it (another version: the launcher then
    /// goes without, and says so once).</summary>
    public bool? Taken { get; private set; }

    public BoardPhase Phase { get; private set; }

    public ulong Heartbeat { get; private set; }

    /// <summary>The game stands still (<see cref="FreezeJudge"/>), and for how long.</summary>
    public bool Frozen => _judge.Frozen;

    public TimeSpan Stood => _judge.Stood;

    /// <summary>Also the game's own watching thread stood still with it: the whole process hangs.</summary>
    public bool WholeProcess { get; private set; }

    /// <summary>The newest race snapshot that can be shown (null before the first).</summary>
    public RaceSnapshot? Latest { get; private set; }

    /// <summary>The race's mode values at the newest picture (null: no mode, no <c>board</c> line, or none written yet).
    /// Picture only, as the snapshot is: rollback can correct them a moment later.</summary>
    public ModeBoardNow? Mode { get; private set; }

    /// <summary>Why the game was asked to stop, first ask (None: not asked).</summary>
    public BoardStopReason StopReason { get; private set; }

    /// <summary>A race snapshot that can be shown (<see cref="ServerMessages.Sound(RaceSnapshot?)"/>), each new one (on the
    /// board's thread).</summary>
    public event Action<RaceSnapshot>? Received;

    /// <summary>The game moved to another phase (on the board's thread).</summary>
    public event Action<BoardPhase>? PhaseChanged;

    /// <summary>The game was judged frozen, or moves again (on the board's thread).</summary>
    public event Action<bool>? FrozenChanged;

    /// <summary>The game's events, each once and in order (on the board's thread); <paramref name="lost"/>: how many
    /// before it were overwritten unread.</summary>
    public event Action<BoardEvent, ulong>? EventRead;

    /// <summary>The game took the board, or refused it (on the board's thread).</summary>
    public event Action<bool>? TakenChanged;

    /// <summary>Looks at the board every <see cref="Every"/> until disposed.</summary>
    public void Start() => _ = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                Look(_clock.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    });

    /// <summary>One look now, on the caller's thread (what came after the board's last look, once its game has ended).</summary>
    public void LookNow() => Look(_clock.Elapsed);

    /// <summary>One look (the board's thread, or a test with its own clock): the header, the pulse, the status, the
    /// race, the events, the judge. What it found is told after the look, outside the lock that keeps the mapping.</summary>
    public void Look(TimeSpan now)
    {
        var told = new List<Action>();
        lock (_writing)
        {
            if (_disposed != 0)
            {
                return;
            }
            LookLocked(now, told);
        }
        foreach (var tell in told)
        {
            tell();
        }
    }

    private void LookLocked(TimeSpan now, List<Action> told)
    {
        if (Taken is null && _view.Taken is { } taken)
        {
            Taken = taken;
            told.Add(() => TakenChanged?.Invoke(taken));
        }
        if (Taken != true)
        {
            return;
        }
        Heartbeat = _view.PulseHeartbeat;
        StopReason = _view.PulseStopReason;
        var watch = _view.PulseWatchBeat;
        if (Heartbeat != _lastBeat)
        {
            _lastBeat = Heartbeat;
            _watchAtBeat = watch;
        }
        if (_view.TryReadStatus(_status) && _status.Phase != Phase)
        {
            var phase = Phase = _status.Phase;
            told.Add(() => PhaseChanged?.Invoke(phase));
        }
        if (_judge.Look(now, Heartbeat, Phase, _status.Flags))
        {
            // the game's own watching thread beats four times a second while the game's thread stands
            WholeProcess = _judge.Frozen && watch - _watchAtBeat <= 1;
            var frozen = _judge.Frozen;
            told.Add(() => FrozenChanged?.Invoke(frozen));
        }
        LookAtMode();
        if (_view.TryReadSnapshot(_snapshot) && _snapshot.Serial != _serial && _snapshot.Serial != 0)
        {
            _serial = _snapshot.Serial;
            if (BoardSnapshots.ToRace(_snapshot) is { } race && ServerMessages.Sound(race))
            {
                Latest = race;
                told.Add(() => Received?.Invoke(race));
            }
        }
        var head = _view.EventsHead;
        ulong lost = 0;
        if (head > _nextEvent && head - _nextEvent > BoardLayout.EventsSlots)
        {
            // the ring holds the last EventsSlots: the ones before are overwritten (and a count without end is no
            // loop without end under the lock the window's thread takes)
            lost = head - _nextEvent - BoardLayout.EventsSlots;
            _nextEvent = head - BoardLayout.EventsSlots;
        }
        while (_nextEvent < head)
        {
            var read = _view.ReadEvents(_nextEvent, out var e);
            if (read == BoardRead.NotYet)
            {
                break;
            }
            _nextEvent++;
            if (read == BoardRead.Lost)
            {
                lost++;
                continue;
            }
            var before = lost;
            told.Add(() => EventRead?.Invoke(e, before));
            lost = 0;
        }
    }

    /// <summary>The mode's head until the game has written it (once, at its first picture), then each new set of values:
    /// before the race's snapshot, so a snapshot told comes with the values of its picture or a newer one.</summary>
    private void LookAtMode()
    {
        if (_mode is null && _view.TryReadModeHead(_modeHead))
        {
            _mode = ModeBoardHead.Of(_modeHead);
        }
        if (_mode is { Lines.Count: > 0 } head && _view.TryReadModeValues(_modeValues) && _modeValues.Serial != 0 && _modeValues.Serial != _modeSerial)
        {
            _modeSerial = _modeValues.Serial;
            Mode = ModeBoardNow.Of(head, _modeValues);
        }
    }

    private ulong _lastBeat;
    private uint _watchAtBeat;

    /// <summary>Asks the game to stop as its window's close does (it says so in its log and ends with exit code 0).
    /// A game that stands still reads no mailbox: it is ended by its process (the caller's).</summary>
    public void AskStop() => Write(m => m.StopSerial++);

    /// <summary>A line for the game (into its log; shown in the game later).</summary>
    public void Notice(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var length = Math.Min(bytes.Length, _mail.NoticeText.Length);
        Write(m =>
        {
            Array.Clear(m.NoticeText);
            bytes.AsSpan(0, length).CopyTo(m.NoticeText);
            m.NoticeLength = (uint)length;
            m.NoticeSerial++;
        });
    }

    private void Write(Action<BoardMailbox> change)
    {
        lock (_writing)
        {
            if (_disposed != 0)
            {
                return;
            }
            change(_mail);
            _view.WriteMailbox(_mail);
        }
    }

    /// <summary>Unmaps the board and removes its file (a game that still maps it keeps it until it ends).</summary>
    public void Dispose()
    {
        lock (_writing)
        {
            if (_disposed != 0)
            {
                return;
            }
            _disposed = 1;
            _stop.Cancel();
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _map.Dispose();
        }
        _stop.Dispose();
        BoardFile.Remove(Path);
    }
}
