namespace Launcher.Core;

/// <summary>An output of the system that a <see cref="SoundLine"/> streams to, mono and 16 bit: Windows' waveOut in
/// the launcher (<c>Launcher.App.WaveOutStream</c>), a stand-in in the tests. Only the line's own thread calls it.</summary>
public interface ISoundDevice
{
    /// <summary>Opens the output at <paramref name="rate"/> samples a second, for blocks of
    /// <paramref name="blockLength"/> samples; false: there is none now (no device).</summary>
    bool Open(int rate, int blockLength);

    /// <summary>Queues the block to play right after the ones queued before it, first waiting until the output has
    /// room for it (it keeps a few queued, never stopping between them); false: the output failed (its device gone,
    /// or no room in time), and the line closes it.</summary>
    bool Write(short[] block);

    /// <summary>Ends what plays and closes the output.</summary>
    void Close();
}

/// <summary>
/// The launcher's sounds on one stream of the system (<see cref="ISoundDevice"/>), opened with the first sound and
/// kept running while sounds come: block after block of what plays (<see cref="SoundMix"/>), silence between two
/// sounds, so that nothing on the system's side is opened, closed, stopped, reset or started again from one sound
/// to the next. A new sound only changes what the next block holds: it cuts the one before it short, however many
/// come in a row. (Windows' PlaySound, which the launcher played its sounds with, opened and closed a device for every
/// sound; more than five or so quick sounds in a row, the pad's cursor down a page, ended in silence, and nothing
/// sounded again for some two seconds.) The stream is worked on the line's own thread, so that the window never
/// waits on the device, and only the newest sound asked for is started: one asked for before the thread got to the
/// one before it takes its place. After <see cref="Idle"/> of silence the stream is closed (no stream is kept running,
/// and the computer kept awake, for nothing), and the next sound opens it again; a stream that fails is closed and
/// opened again for the same sound, once (a headset unplugged: the system's choice of device is asked again).
/// </summary>
public sealed class SoundLine : IDisposable
{
    /// <summary>Samples a second of the stream: the draw's own (<see cref="DrawSounds.Rate"/>); every other sound is
    /// brought to it (<see cref="WaveFile.Resample"/>).</summary>
    public const int Rate = DrawSounds.Rate;

    /// <summary>Samples a block: 10 ms (the system's own period), so that a new sound waits little for its turn.</summary>
    public const int BlockLength = Rate / 100;

    /// <summary>How long the stream runs on in silence before it is closed, unless a line is given another.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    private const int Tries = 2;   // a sound's tries on a stream that fails: opened again once

    private readonly ISoundDevice _device;
    private readonly int _idleBlocks;
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly SoundMix _mix = new();   // the thread's own
    private byte[]? _next;    // the newest sound asked for that the thread has not started
    private Thread? _thread;  // started with the first sound
    private bool _ended;

    /// <summary>A line on the device; <paramref name="idle"/>: how long the stream runs on in silence
    /// (<see cref="Idle"/> when none is given).</summary>
    public SoundLine(ISoundDevice device, TimeSpan? idle = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _idleBlocks = Math.Max(1, (int)Math.Ceiling((idle ?? Idle).TotalSeconds * Rate / BlockLength));
    }

    /// <summary>The wave file (as <see cref="WaveFile.Make"/> writes it) to play now, cutting short what plays; false
    /// when the line has ended. It returns at once: the line's thread starts it (one that is not a wave file is not
    /// played).</summary>
    public bool Play(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);
        lock (_gate)
        {
            if (_ended)
            {
                return false;
            }
            _next = wave;
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Launcher sounds", Priority = ThreadPriority.AboveNormal };
                _thread.Start();
            }
        }
        _ = _wake.Set();
        return true;
    }

    /// <summary>Ends the line: what plays ends and the stream is closed; a sound asked for after it is not played.</summary>
    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            thread = _thread;
        }
        _ = _wake.Set();
        thread?.Join();
        _wake.Dispose();
    }

    private void Run()
    {
        var block = new short[BlockLength];
        var open = false;
        var quiet = 0;   // blocks of silence alone written since the last sound ended
        var tries = 0;   // the stream's failures for the sound that plays
        while (true)
        {
            if (!open && !_mix.Playing)
            {
                _ = _wake.WaitOne();   // no stream and nothing to play: until a sound is asked for
            }
            byte[]? next;
            lock (_gate)
            {
                if (_ended)
                {
                    break;
                }
                next = _next;
                _next = null;
            }
            if (next is not null && WaveFile.Read(next) is { } read)
            {
                _mix.Start(WaveFile.Resample(read.Samples, read.Rate, Rate));
                tries = 0;
            }
            if (!open)
            {
                if (!_mix.Playing)
                {
                    continue;
                }
                if (!_device.Open(Rate, BlockLength))
                {
                    Failed(ref tries);
                    continue;
                }
                open = true;
                quiet = 0;
            }
            var sounding = _mix.Playing;
            _mix.Fill(block);
            quiet = sounding ? 0 : quiet + 1;
            if (!_device.Write(block))
            {
                _device.Close();
                open = false;
                Failed(ref tries);
            }
            else if (quiet >= _idleBlocks)
            {
                _device.Close();
                open = false;
            }
        }
        if (open)
        {
            _device.Close();
        }
    }

    /// <summary>The stream failed the sound that plays: it is tried again on a stream opened anew, else given up.</summary>
    private void Failed(ref int tries)
    {
        if (++tries >= Tries)
        {
            _mix.Stop();
            tries = 0;
        }
    }
}
