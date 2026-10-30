using System.IO.MemoryMappedFiles;
using System.Text.Json.Nodes;
using Launcher.Core;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The launcher's side of the status board (<see cref="GameBoard"/>), with a made-up game: a mapping of its own
/// of the board's file, written through the generated view as the game's core writes it (port/core/src/board.cpp).
/// The race the game wrote arrives as the race page took it from the UDP feed before; a heartbeat that stands is
/// judged where it should beat and nowhere else; the mailbox carries the stop and a notice; a game of another version
/// refuses and the launcher goes without; the file goes with the board.</summary>
public sealed unsafe class BoardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "board-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>The game's side: its own mapping of the board's file.</summary>
    private sealed class FakeGame : IDisposable
    {
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _accessor;
        private ulong _events;

        public FakeGame(string path, uint version = BoardLayout.Version)
        {
            _map = MemoryMappedFile.CreateFromFile(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete),
                null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            _accessor = _map.CreateViewAccessor();
            byte* at = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
            View = new BoardView(at + _accessor.PointerOffset);
            // as the core's Take: its version, size and process; nothing else when it refuses
            View.HeaderRunnerSize = BoardLayout.Size;
            View.HeaderRunnerPid = 4242;
            View.HeaderRunnerVersion = version;
        }

        public readonly BoardView View;

        public void Status(BoardPhase phase, uint flags = 0) => View.WriteStatus(new BoardStatus { Phase = phase, Flags = flags });

        public void Event(BoardEventKind kind, uint a) => View.WriteEvents(_events++, new BoardEvent { Kind = kind, A = a });

        public void Dispose()
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _map.Dispose();
        }
    }

    /// <summary>What the protocol's own vector would put on the board, every scope and type, written by its generated writer.</summary>
    private static BoardModeHead ModeHead(params (BoardModeBoardScope Scope, BoardModeBoardType Type, ushort Column, string Label, string[] Words)[] lines)
    {
        var head = new BoardModeHead();
        head.Serial = 1;
        head.NameLength = 5;
        head.Count = (uint)lines.Length;
        "Board"u8.CopyTo(head.Name);
        head.Hash = [1, 2, 3, 4, 5, 6, 7, 0xAB];
        for (var i = 0; i < lines.Length; i++)
        {
            var label = System.Text.Encoding.UTF8.GetBytes(lines[i].Label);
            ref var line = ref head.Lines[i];
            line.Scope = lines[i].Scope;
            line.Type = lines[i].Type;
            line.Column = lines[i].Column;
            line.LabelLength = (uint)label.Length;
            line.WordCount = (uint)lines[i].Words.Length;
            label.CopyTo(line.Label, 0);
            for (var w = 0; w < lines[i].Words.Length; w++)
            {
                var word = System.Text.Encoding.UTF8.GetBytes(lines[i].Words[w]);
                head.Words[(i * 16) + w].Length = (uint)word.Length;
                word.CopyTo(head.Words[(i * 16) + w].Text, 0);
            }
        }
        return head;
    }

    [Fact]
    public void AModesHeadAndValuesAreReadFromTheBoardByTheirLines()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new FakeGame(board.Path);
        board.Look(S(0));
        Assert.Null(board.Mode);   // no mode: nothing

        game.View.WriteModeHead(ModeHead(
            (BoardModeBoardScope.Global, BoardModeBoardType.Int, 0, "Next cut", []),
            (BoardModeBoardScope.Global, BoardModeBoardType.Real, 1, "Pace", []),
            (BoardModeBoardScope.Global, BoardModeBoardType.Bool, 2, "Armed", []),
            (BoardModeBoardScope.Global, BoardModeBoardType.Seat, 3, "Leader", []),
            (BoardModeBoardScope.Global, BoardModeBoardType.Seats, 4, "Alive", []),
            (BoardModeBoardScope.Seat, BoardModeBoardType.Int, 0, "Lives", []),
            (BoardModeBoardScope.Seat, BoardModeBoardType.Int, 1, "State", ["", "Sûr", "Out"]),
            (BoardModeBoardScope.Team, BoardModeBoardType.Int, 0, "Tank", [])));
        board.Look(S(1));
        Assert.Null(board.Mode);   // the head alone: no values yet

        var values = new BoardModeValues { Serial = 9, Frame = 1260 };
        values.Globals[0].Value = 7;
        values.Globals[1].Value = BitConverter.SingleToInt32Bits(1.5f);
        values.Globals[2].Value = 1;
        values.Globals[3].Value = 2;
        values.Globals[4].Value = 0b101;
        values.Seats[(2 * 8) + 0].Value = 3;   // Seats[seat * 8 + Column]
        values.Seats[(2 * 8) + 1].Value = 1;
        values.Seats[(3 * 8) + 1].Value = 2;
        values.Seats[(4 * 8) + 1].Value = 0;
        values.Seats[(5 * 8) + 1].Value = 16;  // past its words: the number
        values.Teams[(6 * 8) + 0].Value = 80;  // Teams[team * 8 + Column]
        game.View.WriteModeValues(values);
        board.Look(S(2));

        var now = Assert.IsType<ModeBoardNow>(board.Mode);
        Assert.Equal(1260u, now.Frame);
        Assert.Equal("Board", now.Head.Name);
        Assert.Equal("01020304050607ab", now.Head.Hash);
        var rows = ModeBoardRows.Of(now, [2, 3, 4, 5], [6, 6, 6, 6, 6, 6], seat => $"Racer {seat}");
        Assert.Equal([new("Next cut", "7"), new("Pace", "1.5"), new("Armed", "Yes"), new("Leader", "Racer 2"), new("Alive", "Racer 0, Racer 2")],
            (IEnumerable<ModeFigure>)rows.Globals);
        Assert.Equal(["Lives", "State"], rows.SeatHeads);
        Assert.Equal(["3", "Sûr"], rows.SeatCells[2]);
        Assert.Equal(["0", "Out"], rows.SeatCells[3]);
        Assert.Equal(["0", ""], rows.SeatCells[4]);   // Length 0: a blank cell
        Assert.Equal(["0", "16"], rows.SeatCells[5]);
        var team = Assert.Single(rows.Teams);
        Assert.Equal("Team 7", team.Team);
        Assert.Equal(new ModeFigure("Tank", "80"), Assert.Single(team.Lines));

        values.Globals[0].Value = 8;
        game.View.WriteModeValues(values);
        board.Look(S(3));
        Assert.Same(now, board.Mode);   // the same serial: no new values
        values.Serial = 10;
        game.View.WriteModeValues(values);
        board.Look(S(4));
        Assert.Equal(8, board.Mode!.Global(now.Head.Lines[0]));
    }

    [Fact]
    public void AModesLinesReadByTheirType()
    {
        static string Text(BoardModeBoardType type, int value, params string[] words) =>
            new ShownModeLine(BoardModeBoardScope.Global, type, 0, "L", words).Text(value, seat => $"S{seat}");
        Assert.Equal("-3", Text(BoardModeBoardType.Int, -3));
        Assert.Equal("-3", Text(BoardModeBoardType.Int, -3, "a"));          // a negative is no word
        Assert.Equal("a", Text(BoardModeBoardType.Int, 0, "a", "b"));
        Assert.Equal("2", Text(BoardModeBoardType.Int, 2, "a", "b"));       // WordCount is past the last word
        Assert.Equal("", Text(BoardModeBoardType.Int, 1, "a", ""));
        Assert.Equal("0", Text(BoardModeBoardType.Real, 0));
        Assert.Equal("0", Text(BoardModeBoardType.Real, BitConverter.SingleToInt32Bits(-0.001f)));
        Assert.Equal("-", Text(BoardModeBoardType.Real, BitConverter.SingleToInt32Bits(float.NaN)));
        Assert.Equal("No", Text(BoardModeBoardType.Bool, 0));
        Assert.Equal("-", Text(BoardModeBoardType.Seat, -1));
        Assert.Equal("S29", Text(BoardModeBoardType.Seat, 29));
        Assert.Equal("30", Text(BoardModeBoardType.Seat, 30));
        Assert.Equal("-", Text(BoardModeBoardType.Seats, 0));
    }

    [Fact]
    public void AModeOfNoLinesOrAHeadWrittenBadlyShowsNothingItCannotRead()
    {
        Assert.Null(ModeBoardHead.Of(new BoardModeHead()));   // serial 0: no mode
        var head = ModeHead(
            (BoardModeBoardScope.Seat, BoardModeBoardType.Int, 8, "Column past the eighth", []),
            ((BoardModeBoardScope)9, BoardModeBoardType.Int, 0, "No scope", []),
            (BoardModeBoardScope.Seat, (BoardModeBoardType)9, 0, "No type", []),
            (BoardModeBoardScope.Seat, BoardModeBoardType.Int, 1, "Good", ["a"]));
        head.NameLength = uint.MaxValue;
        head.Lines[3].LabelLength = 400;
        head.Lines[3].WordCount = 4000;
        head.Words[(3 * 16) + 0].Length = 400;
        head.Count = 4000;
        var read = Assert.IsType<ModeBoardHead>(ModeBoardHead.Of(head));
        Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(read.Name));
        Assert.Equal(21, read.Lines.Count);   // of the head's 24, the three that read as nothing left out
        var line = read.Lines[0];
        Assert.Equal(16, line.Words.Count);
        Assert.Equal(32, System.Text.Encoding.UTF8.GetByteCount(line.Label));
        Assert.False(ModeBoardRows.Of(null, [0], null, s => "").Any);
        Assert.False(ModeBoardRows.Of(new ModeBoardNow(new ModeBoardHead("M", "", []), 0, new int[8], new int[240], new int[240]), [0], null, s => "").Any);
    }

    private static BoardSnapshot Race(uint serial, params BoardRacer[] racers)
    {
        var s = new BoardSnapshot { Serial = serial, Frame = 1260, LocalSeat = 1, Count = (uint)racers.Length };
        racers.CopyTo(s.Racers, 0);
        return s;
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void TheGamesRaceArrivesAsTheRacePageTookItFromTheFeedBefore()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new FakeGame(board.Path);
        var phases = new List<BoardPhase>();
        var races = new List<RaceSnapshot>();
        board.PhaseChanged += phases.Add;
        board.Received += races.Add;
        game.Status(BoardPhase.Racing);
        game.View.WriteSnapshot(Race(1,
            new BoardRacer { Seat = 0, Rank = 2, LapMs = 20950, TotalMs = 20950, Speed = 1032.4f, Energy = 88.5f, MaxEnergy = 100, TopSpeed = 1101f, X = -1201.3f, Y = 40f, Z = 388.9f, Distance = 8890.5f },
            new BoardRacer { Seat = 1, Rank = 1, Lap = 1, LapMs = 3010, TotalMs = 21007, BestLapMs = 17997, Flags = BoardRacerFlags.HasBestLap, Speed = 1110f, Energy = 100f, MaxEnergy = 100, Kos = 2, TopSpeed = 1180.2f, Y = 41.5f, Distance = 9100f },
            new BoardRacer { Seat = 2, State = BoardRacerState.Out },
            new BoardRacer { Seat = 3, State = BoardRacerState.Finished }));
        board.Look(S(0));
        board.Look(S(0.016));   // (the same snapshot again: passed on once)
        Assert.True(board.Taken);
        Assert.Equal([BoardPhase.Racing], phases);
        var s = Assert.Single(races);
        Assert.Equal((1260, 1, 4), (s.Frame, s.LocalSeat, s.Racers.Count));
        Assert.Null(s.Racers[0].BestLapMs);
        Assert.Equal(17997, s.Racers[1].BestLapMs);
        Assert.Equal((2, 1110.0f, 41.5f, 100f), (s.Racers[1].Kos, s.Racers[1].Speed, s.Racers[1].Y, s.Racers[1].MaxEnergy));
        Assert.Equal((8890.5f, 9100.0f), (s.Racers[0].Distance, s.Racers[1].Distance));
        Assert.Equal([RacerLive.Racing, RacerLive.Racing, RacerLive.Out, RacerLive.Finished], s.Racers.Select(r => r.State));
        Assert.Same(s, board.Latest);
    }

    /// <summary>The board is a file in the launcher's own folder that only this PC's programs can open; still, what
    /// cannot be shown is not passed on (the check the coordinator's reports pass).</summary>
    [Fact]
    public void ARaceWithASeatTwiceOrAPlaceThatIsNoNumberIsNotPassedOn()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new FakeGame(board.Path);
        var races = new List<RaceSnapshot>();
        board.Received += races.Add;
        game.View.WriteSnapshot(Race(1, new BoardRacer { Seat = 4 }, new BoardRacer { Seat = 4 }));
        board.Look(S(0));
        game.View.WriteSnapshot(Race(2, new BoardRacer { Seat = 4, X = float.PositiveInfinity }));
        board.Look(S(0.1));
        game.View.WriteSnapshot(Race(3, new BoardRacer { Seat = 31 }));
        board.Look(S(0.2));
        game.View.WriteSnapshot(Race(4, new BoardRacer { Seat = 6 }));
        board.Look(S(0.3));
        Assert.Equal(6, Assert.Single(Assert.Single(races).Racers).Seat);
    }

    [Fact]
    public void AHeartbeatThatStandsIsJudgedWhereItShouldBeat()
    {
        using var board = GameBoard.Create(_folder, frozenAfter: S(10));
        using var game = new FakeGame(board.Path);
        var said = new List<bool>();
        board.FrozenChanged += said.Add;
        // before the first beat, at the gate, while the core waits for the relay: never
        game.Status(BoardPhase.Starting);
        board.Look(S(0));
        board.Look(S(60));
        game.View.PulseHeartbeat = 5;
        game.Status(BoardPhase.Gate);
        board.Look(S(61));
        board.Look(S(200));
        game.Status(BoardPhase.Racing, BoardStatusFlags.WaitsForRelay);
        board.Look(S(201));
        board.Look(S(214));
        Assert.Empty(said);
        // racing: 10 s with no beat, while the game's own watching thread goes on
        game.Status(BoardPhase.Racing);
        board.Look(S(220));
        game.View.PulseWatchBeat = 40;
        board.Look(S(229.9));
        Assert.Empty(said);
        board.Look(S(230));
        Assert.Equal([true], said);
        Assert.True(board.Frozen);
        Assert.False(board.WholeProcess);
        Assert.Equal(S(10), board.Stood);
        // it moves again
        game.View.PulseHeartbeat = 6;
        board.Look(S(231));
        Assert.Equal([true, false], said);
        // the whole process stands: the watching thread too
        board.Look(S(241.5));
        Assert.True(board.Frozen && board.WholeProcess);
        // a game that stops must be gone soon: one still there stands too
        game.View.PulseHeartbeat = 7;
        game.Status(BoardPhase.Stopping);
        board.Look(S(242));
        board.Look(S(252));
        Assert.Equal([true, false, true, false, true], said);
    }

    [Fact]
    public void TheMailboxCarriesTheStopAndANotice()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new FakeGame(board.Path);
        var mail = new BoardMailbox();
        Assert.True(game.View.TryReadMailbox(mail) && mail.StopSerial == 0 && mail.NoticeSerial == 0);
        board.Notice("Ready in 10 s");
        board.AskStop();
        Assert.True(game.View.TryReadMailbox(mail));
        Assert.Equal((1u, 1u, 13u), (mail.StopSerial, mail.NoticeSerial, mail.NoticeLength));
        Assert.Equal("Ready in 10 s", System.Text.Encoding.UTF8.GetString(mail.NoticeText, 0, 13));
        board.Notice(new string('é', 100));   // (cut to the box's 128 bytes)
        Assert.True(game.View.TryReadMailbox(mail) && mail.NoticeSerial == 2 && mail.NoticeLength == 128);
    }

    [Fact]
    public void TheGamesEventsAreReadOnceInOrderAndALossIsCounted()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new FakeGame(board.Path);
        var read = new List<(BoardEventKind, uint, ulong)>();
        board.EventRead += (e, lost) => read.Add((e.Kind, e.A, lost));
        game.Event(BoardEventKind.Stands, 2000);
        game.Event(BoardEventKind.Moves, 2600);
        board.Look(S(0));
        board.Look(S(1));
        Assert.Equal([(BoardEventKind.Stands, 2000u, 0ul), (BoardEventKind.Moves, 2600u, 0ul)], read);
        read.Clear();
        for (var i = 0u; i < BoardLayout.EventsSlots + 3; i++)
        {
            game.Event(BoardEventKind.Desync, i);
        }
        board.Look(S(2));
        Assert.Equal(BoardLayout.EventsSlots, read.Count);
        Assert.Equal((BoardEventKind.Desync, 3u, 3ul), read[0]);
        game.Event(BoardEventKind.Stop, (uint)BoardStopReason.Launcher);
        game.View.PulseStopReason = BoardStopReason.Launcher;
        board.Look(S(3));
        Assert.Equal((BoardEventKind.Stop, 7u, 0ul), read[^1]);
        Assert.Equal(BoardStopReason.Launcher, board.StopReason);
    }

    [Fact]
    public void AGameOfAnotherVersionRefusesAndTheLauncherGoesWithout()
    {
        using var board = GameBoard.Create(_folder);
        Assert.Null(board.Taken);
        using var game = new FakeGame(board.Path, BoardLayout.Version + 1);
        var taken = new List<bool>();
        var races = new List<RaceSnapshot>();
        board.TakenChanged += taken.Add;
        board.Received += races.Add;
        game.View.WriteSnapshot(Race(1, new BoardRacer { Seat = 1 }));
        game.View.PulseHeartbeat = 1;
        game.Status(BoardPhase.Racing);
        board.Look(S(0));
        board.Look(S(100));
        Assert.Equal([false], taken);
        Assert.Empty(races);
        Assert.False(board.Frozen);
        Assert.Equal(BoardPhase.None, board.Phase);
    }

    [Fact]
    public void TheBoardIsAFileOfTheLaunchersFolderThatGoesWithIt()
    {
        Directory.CreateDirectory(_folder);
        var left = Path.Combine(_folder, "game-left.board");
        File.WriteAllBytes(left, new byte[16]);                     // (no launcher's process id: gone)
        using var other = GameBoard.Create(_folder);                // a launcher that runs: this process
        var board = GameBoard.Create(_folder);
        Assert.False(File.Exists(left));   // a board of a launcher that ended without removing it
        Assert.True(File.Exists(other.Path));
        Assert.Equal(_folder, Path.GetDirectoryName(board.Path));
        var bytes = File.ReadAllBytes(board.Path);
        Assert.Equal(BoardLayout.Size, bytes.Length);
        Assert.Equal("FZSB"u8.ToArray(), bytes[..4]);
        board.Dispose();
        board.Dispose();
        board.Look(S(0));
        board.AskStop();
        Assert.False(File.Exists(board.Path));
    }

    [Fact]
    public void TheLaunchFileNamesTheBoardAndNoLiveFeed()
    {
        var window = new GameWindow("F-Zero GX", Board: @"C:\data\boards\game-1.board");
        var race = JsonNode.Parse(GameLaunchFile.Race("b", """{"match":{"matchId":"m7"}}""", record: "r.fzrp", window: window))!;
        Assert.Equal(@"C:\data\boards\game-1.board", (string?)race["board"]);
        Assert.Null(race["match"]!["live"]);
        var offline = JsonNode.Parse(GameLaunchFile.Offline("b", window: window))!;
        Assert.Equal(@"C:\data\boards\game-1.board", (string?)offline["board"]);
        Assert.Null(JsonNode.Parse(GameLaunchFile.Offline("b", window: new GameWindow("F-Zero GX")))!["board"]);
    }

    [Fact]
    public void TheBoardsLinesSayWhatItHeld()
    {
        using var board = GameBoard.Create(_folder, frozenAfter: S(10));
        using var game = new FakeGame(board.Path);
        var lines = new List<string>();
        BoardLines.Follow(board, lines.Add);
        game.View.PulseHeartbeat = 1;
        game.Status(BoardPhase.Racing);
        game.View.WriteSnapshot(Race(1, new BoardRacer { Seat = 1 }));
        game.Event(BoardEventKind.Stands, 2000);
        board.Look(S(0));
        board.Look(S(10));
        Assert.Contains(lines, l => l.Contains("\"taken\":true", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"phase\":\"Racing\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"snapshot\":1260", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"kind\":\"Stands\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("\"frozen\":true", StringComparison.Ordinal));
        Assert.Contains("\"ended\":true", BoardLines.Last(board), StringComparison.Ordinal);
    }
}
