using System.Diagnostics;
using System.Runtime.InteropServices;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// Windows' waveOut as the stream of the launcher's sounds (<see cref="SoundLine"/>, which <see cref="Attention"/>
/// plays on): the system's choice of device (the wave mapper: the default output), opened once for many sounds, with
/// <see cref="Blocks"/> blocks that go round: each one played is filled again and queued behind the others, so the
/// stream never runs dry, is never reset and never started again while sounds come. The system tells of a block
/// played through an event (CALLBACK_EVENT), which the line's thread waits on; only that thread calls this.
/// Windows only.
/// </summary>
internal sealed partial class WaveOutStream : ISoundDevice, IDisposable
{
    /// <summary>Blocks queued at most: 40 ms of 10 ms blocks, the most a new sound waits behind.</summary>
    private const int Blocks = 4;

    private const uint WaveMapper = uint.MaxValue;
    private const uint CallbackEvent = 0x0005_0000;
    private const int HeaderDone = 0x0001;   // WHDR_DONE: the system has played the block, or been reset

    /// <summary>A block the system has not taken back by then: the device has failed (gone, or stuck).</summary>
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(1);

    private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();
    private static readonly int FlagsAt = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));

    private readonly AutoResetEvent _played = new(false);   // the system sets it as a block has played
    private readonly IntPtr[] _headers = new IntPtr[Blocks];  // each in memory of its own: the system writes its flags
    private readonly IntPtr[] _data = new IntPtr[Blocks];     // the samples the system plays from
    private readonly bool[] _queued = new bool[Blocks];
    private IntPtr _device;
    private int _turn;   // the block filled next: the one queued longest ago

    public bool Open(int rate, int blockLength)
    {
        var format = new WaveFormat
        {
            Tag = 1,   // PCM
            Channels = 1,
            Rate = (uint)rate,
            BytesPerSecond = (uint)rate * 2,
            BlockAlign = 2,
            Bits = 16,
        };
        if (WaveOutOpen(out var device, WaveMapper, ref format, _played.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent) != 0)
        {
            return false;
        }
        _device = device;
        _turn = 0;
        var bytes = blockLength * 2;
        for (var i = 0; i < Blocks; i++)
        {
            _data[i] = Marshal.AllocHGlobal(bytes);
            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(new WaveHeader { Data = _data[i], BufferLength = (uint)bytes }, _headers[i], fDeleteOld: false);
            if (WaveOutPrepareHeader(device, _headers[i], (uint)HeaderSize) != 0)
            {
                Close();
                return false;
            }
        }
        return true;
    }

    public bool Write(short[] block)
    {
        var header = _headers[_turn];
        var waited = Stopwatch.StartNew();
        while (_queued[_turn] && (Marshal.ReadInt32(header, FlagsAt) & HeaderDone) == 0)
        {
            var left = Late - waited.Elapsed;
            if (left <= TimeSpan.Zero || !_played.WaitOne(left))
            {
                return false;
            }
        }
        Marshal.Copy(block, 0, _data[_turn], block.Length);
        if (WaveOutWrite(_device, header, (uint)HeaderSize) != 0)
        {
            return false;
        }
        _queued[_turn] = true;
        _turn = (_turn + 1) % Blocks;
        return true;
    }

    public void Close()
    {
        if (_device == IntPtr.Zero)
        {
            return;
        }
        _ = WaveOutReset(_device);   // every queued block comes back done, so that it can be unprepared and go
        for (var i = 0; i < Blocks; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                _ = WaveOutUnprepareHeader(_device, _headers[i], (uint)HeaderSize);
            }
            Marshal.FreeHGlobal(_headers[i]);   // (nothing for a zero)
            Marshal.FreeHGlobal(_data[i]);
            _headers[i] = IntPtr.Zero;
            _data[i] = IntPtr.Zero;
            _queued[i] = false;
        }
        _ = WaveOutClose(_device);
        _device = IntPtr.Zero;
    }

    /// <summary>The stream closed, and the event the system told of its blocks through gone.</summary>
    public void Dispose()
    {
        Close();
        _played.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag;
        public ushort Channels;
        public uint Rate;
        public uint BytesPerSecond;
        public ushort BlockAlign;
        public ushort Bits;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [LibraryImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static partial uint WaveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static partial uint WaveOutPrepareHeader(IntPtr device, IntPtr header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static partial uint WaveOutWrite(IntPtr device, IntPtr header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static partial uint WaveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static partial uint WaveOutReset(IntPtr device);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static partial uint WaveOutClose(IntPtr device);
}
