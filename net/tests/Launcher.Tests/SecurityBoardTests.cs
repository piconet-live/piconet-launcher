using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Launcher.Core;
using Launcher.Core.Board;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The status board as the launcher reads it when what is in it is not what a sound game writes: the
/// board is a file of the player's own data folder, which the game (or any program of the same
/// user) maps for writing. Whatever it holds, a look at it is short, raises nothing, and passes on only a race the
/// pages can show: the window's thread takes the board's lock to stop the game or put it away.</summary>
public sealed unsafe class SecurityBoardTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("board-security").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>The other side's own mapping of the board's file, as a game (or another program) has it.</summary>
    private sealed class Writer : IDisposable
    {
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _accessor;

        public Writer(string path)
        {
            _map = MemoryMappedFile.CreateFromFile(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete),
                null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            _accessor = _map.CreateViewAccessor();
            byte* at = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
            At = at + _accessor.PointerOffset;
            View = new BoardView(At);
            View.HeaderRunnerSize = BoardLayout.Size;
            View.HeaderRunnerPid = 4242;
            View.HeaderRunnerVersion = BoardLayout.Version;
        }

        public readonly byte* At;

        public readonly BoardView View;

        public void Dispose()
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _map.Dispose();
        }
    }

    /// <summary>The events' count is the game's word, read as it stands: a count of 2^64 - 1 with every slot's mark
    /// set so that each event reads as overwritten kept the board's thread in its loop for about a billion turns
    /// (seconds a look, and again at the next look), holding the lock that the window's thread takes to ask the game
    /// to stop or to put the board away: the launcher's window stood still with it. A look now reads the ring's 64
    /// slots at most, and counts the rest as lost.</summary>
    [Fact]
    public void AnEventCountWithoutEndIsReadAsTheRingsSlotsAtMost()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new Writer(board.Path);
        for (var slot = 0; slot < BoardLayout.EventsSlots; slot++)
        {
            *(uint*)(game.At + BoardLayout.EventsFirst + (slot * BoardLayout.EventsSlot)) = 0x80000001;
        }
        *(ulong*)(game.At + BoardLayout.Events) = ulong.MaxValue;
        var read = 0;
        ulong lost = 0;
        board.EventRead += (_, l) => (read, lost) = (read + 1, lost + l);
        var clock = Stopwatch.StartNew();
        board.Look(TimeSpan.Zero);
        board.Look(TimeSpan.FromMilliseconds(15));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"two looks took {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.InRange(read, 0, BoardLayout.EventsSlots);
    }

    /// <summary>A sound game's events still arrive each once, in order, when it wrote more than the ring holds
    /// between two looks: the ones overwritten are counted as lost before the first one read.</summary>
    [Fact]
    public void MoreEventsThanTheRingHoldsAreCountedAsLost()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new Writer(board.Path);
        for (ulong n = 0; n < 200; n++)
        {
            game.View.WriteEvents(n, new BoardEvent { Kind = BoardEventKind.Stands, A = (uint)n });
        }
        var read = new List<(uint A, ulong Lost)>();
        board.EventRead += (e, lost) => read.Add((e.A, lost));
        board.Look(TimeSpan.Zero);
        Assert.Equal(BoardLayout.EventsSlots, read.Count);
        Assert.Equal((136u, 136ul), read[0]);
        Assert.Equal(Enumerable.Range(136, 64).Select(i => (uint)i), read.Select(r => r.A));
        Assert.All(read.Skip(1), r => Assert.Equal(0ul, r.Lost));
    }

    /// <summary>A board of any bytes (the header aside, which says the game took it): every look is short and
    /// raises nothing, and a race passed on is one the pages can show.</summary>
    [Fact]
    public void ABoardOfAnyBytesIsLookedAtWithoutHarm()
    {
        using var board = GameBoard.Create(_folder);
        using var game = new Writer(board.Path);
        var random = new Random(20261019);
        var races = 0;
        board.Received += race =>
        {
            Assert.True(ServerMessages.Sound(race));
            Assert.InRange(race.LocalSeat, 0, 29);
            races++;
        };
        var events = 0;
        board.EventRead += (_, _) => events++;
        var bytes = new byte[BoardLayout.Size - BoardLayout.Pulse];
        ulong written = 0;
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 3000; i++)
        {
            random.NextBytes(bytes);
            if (i < 1500)
            {
                // the first half: the events' ring as the game writes it (below), the rest any bytes
                Array.Clear(bytes, BoardLayout.Events - BoardLayout.Pulse, BoardLayout.Mailbox - BoardLayout.Events);
                new Span<byte>(game.At + BoardLayout.Events, BoardLayout.Mailbox - BoardLayout.Events).CopyTo(
                    bytes.AsSpan(BoardLayout.Events - BoardLayout.Pulse));
            }
            new Span<byte>(game.At + BoardLayout.Pulse, bytes.Length).Clear();
            bytes.CopyTo(new Span<byte>(game.At + BoardLayout.Pulse, bytes.Length));
            if (i % 2 == 0)
            {
                // a race as the game writes one, with hostile numbers: seats of no race or twice, numbers that are none
                var snapshot = new BoardSnapshot
                {
                    Serial = (uint)i + 1, Frame = random.Next(4) == 0 ? uint.MaxValue - (uint)random.Next(9) : (uint)random.Next(100000),
                    LocalSeat = (uint)random.Next(-2, 33), Count = (uint)random.Next(32),
                };
                for (var r = 0; r < snapshot.Racers.Length; r++)
                {
                    snapshot.Racers[r] = new BoardRacer
                    {
                        Seat = (byte)(random.Next(3) == 0 ? random.Next(256) : r), State = (BoardRacerState)random.Next(5),
                        Rank = (byte)random.Next(256), Flags = (byte)random.Next(256), Lap = (ushort)random.Next(65536),
                        Kos = (ushort)random.Next(65536), MaxEnergy = (ushort)random.Next(65536),
                        LapMs = (uint)random.NextInt64(1L << 32), TotalMs = (uint)random.NextInt64(1L << 32), BestLapMs = (uint)random.NextInt64(1L << 32),
                        Speed = Hostile(random), Energy = Hostile(random), TopSpeed = Hostile(random),
                        X = Hostile(random), Y = Hostile(random), Z = Hostile(random), Distance = Hostile(random),
                    };
                }
                game.View.WriteSnapshot(snapshot);
                for (var e = random.Next(80); i < 1500 && e > 0; e--)
                {
                    game.View.WriteEvents(written++, new BoardEvent { Kind = (BoardEventKind)random.Next(9), A = (uint)random.Next(), B = (uint)random.Next() });
                }
            }
            var before = events;
            board.Look(TimeSpan.FromMilliseconds(15 * i));
            Assert.InRange(events - before, 0, BoardLayout.EventsSlots);
        }
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"3000 looks took {clock.Elapsed.TotalSeconds:0.0} s");
        TestContext.Current.SendDiagnosticMessage($"board: 3000 boards, {races} races passed on, {events} events");
    }

    private static readonly float[] Odd = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, -float.MaxValue, float.Epsilon, -0f];

    private static float Hostile(Random random) => random.Next(40) == 0 ? Odd[random.Next(Odd.Length)] : (float)((random.NextDouble() - 0.5) * 1e5);
}
