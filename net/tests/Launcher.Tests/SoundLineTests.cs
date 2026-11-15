using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The launcher's one stream for its sounds (<see cref="SoundLine"/>), on a stand-in device that keeps what was done
/// to it: many sounds in a row, the pad's cursor down a page, all play on one stream opened once, which runs on in
/// silence between them and is never closed, reset or started again from one sound to the next (PlaySound, before
/// it, opened and closed a device for each, and more than five or so in a row went silent for two seconds); a burst
/// plays its newest; a stream silent so long is closed and the next sound opens it again; a failing stream is opened
/// again for the same sound; no device at all plays nothing and raises nothing. What plays block by block
/// (<see cref="SoundMix"/>): a sound cut short fades out under the next; every sound is brought to the stream's rate
/// (<see cref="WaveFile.Resample"/>).
/// </summary>
public sealed class SoundLineTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public void ManySoundsInARowPlayOnOneStreamOpenedOnce()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        for (var n = 1; n <= 30; n++)
        {
            Assert.True(line.Play(Wave(n)));
            Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(n)), Long), $"sound {n} was not played");
        }
        Assert.Equal(1, device.Opens);
        Assert.Equal(0, device.Closes);
        Assert.Equal($"open {SoundLine.Rate} {SoundLine.BlockLength}", device.Calls[0]);
        Assert.All(device.Calls.Skip(1), call => Assert.Equal("write", call));
    }

    [Fact]
    public void TheStreamRunsOnInSilenceBetweenSounds()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        Assert.True(line.Play(Wave(1, length: 100)));
        Assert.True(SpinWait.SpinUntil(() => device.Written.Count > 20, Long));
        Assert.Equal(Level(1), device.Written[0][0]);
        Assert.All(device.Written.Skip(1).Take(19), block => Assert.All(block, sample => Assert.Equal(0, sample)));

        Assert.True(line.Play(Wave(2)));
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(2)), Long));
        Assert.Equal(1, device.Opens);
        Assert.Equal(0, device.Closes);
    }

    [Fact]
    public void ABurstPlaysItsNewestSound()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        for (var n = 1; n <= 50; n++)
        {
            Assert.True(line.Play(Wave(n)));
        }
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(50)), Long));
        Assert.Equal(1, device.Opens);
    }

    [Fact]
    public void AStreamSilentSoLongIsClosedAndTheNextSoundOpensItAgain()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMilliseconds(50));
        Assert.True(line.Play(Wave(1, length: SoundLine.BlockLength)));
        Assert.True(SpinWait.SpinUntil(() => device.Closes == 1, Long));
        Assert.Equal(1 + 5, device.Written.Count);   // the sound's block, then 50 ms of silence

        Assert.True(line.Play(Wave(2)));
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(2)), Long));
        Assert.Equal(2, device.Opens);
    }

    [Fact]
    public void AFailingStreamIsClosedAndOpenedAgainForTheSameSound()
    {
        var device = new Device { Failures = 1 };
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        Assert.True(line.Play(Wave(7)));
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(7)), Long));
        Assert.Equal(["open", "write failed", "close", "open", "write"], device.Calls.Take(5).Select(c => c.Split(' ')[0] == "open" ? "open" : c));
        Assert.Equal(2, device.Opens);
    }

    [Fact]
    public void NoDeviceIsTriedTwiceAndTheNextSoundTriesAgain()
    {
        var device = new Device { Opening = false };
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        Assert.True(line.Play(Wave(1)));
        Assert.True(SpinWait.SpinUntil(() => device.Opens == 2, Long));
        Thread.Sleep(100);
        Assert.Equal(2, device.Opens);   // given up: no stream tried over and over

        device.Opening = true;
        Assert.True(line.Play(Wave(2)));
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(2)), Long));
        Assert.Equal(3, device.Opens);
        Assert.DoesNotContain(device.Written, block => block.Contains(Level(1)));
    }

    [Fact]
    public void AnEndedLineClosesItsStreamAndPlaysNothingMore()
    {
        var device = new Device();
        var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        Assert.True(line.Play(Wave(1)));
        Assert.True(SpinWait.SpinUntil(() => device.Heard(Level(1)), Long));
        line.Dispose();
        Assert.Equal(1, device.Closes);
        Assert.False(line.Play(Wave(2)));
        line.Dispose();
        Assert.Equal(1, device.Closes);
    }

    [Fact]
    public void WhatIsNotAWaveFileIsNotPlayed()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        Assert.True(line.Play([1, 2, 3]));
        Thread.Sleep(100);
        Assert.Equal(0, device.Opens);
    }

    [Fact]
    public void EveryRateIsBroughtToTheStreams()
    {
        var device = new Device();
        using var line = new SoundLine(device, idle: TimeSpan.FromMinutes(1));
        var samples = Enumerable.Repeat((short)1000, 2205).ToArray();   // 100 ms at 22050 Hz
        Assert.True(line.Play(WaveFile.Make(samples, 22050)));
        Assert.True(SpinWait.SpinUntil(() => device.Written.Count > 12, Long));
        var played = device.Written.SelectMany(b => b).Count(s => s == 1000);
        Assert.Equal(4410, played);   // 100 ms at the stream's 44100 Hz
    }

    [Fact]
    public void ASoundCutShortFadesOutUnderTheNext()
    {
        var mix = new SoundMix();
        mix.Start(Enumerable.Repeat((short)1000, 1000).ToArray());
        var block = new short[10];
        mix.Fill(block);
        Assert.All(block, s => Assert.Equal(1000, s));

        mix.Start(Enumerable.Repeat((short)100, 1000).ToArray());
        var after = new short[SoundMix.Fade + 10];
        mix.Fill(after);
        Assert.Equal(1100, after[0]);                                    // the one cut short at full, under the new one's start
        Assert.True(after.Take(SoundMix.Fade).Zip(after.Skip(1)).All(p => p.First >= p.Second), "the fade only falls");
        Assert.All(after.Skip(SoundMix.Fade), s => Assert.Equal(100, s)); // then the new one alone
    }

    [Fact]
    public void AMixPlaysSilenceOnceItsSoundHasEnded()
    {
        var mix = new SoundMix();
        Assert.False(mix.Playing);
        mix.Start([5, 6, 7]);
        Assert.True(mix.Playing);
        var block = new short[6];
        mix.Fill(block);
        Assert.Equal([5, 6, 7, 0, 0, 0], block);
        Assert.False(mix.Playing);

        mix.Start([short.MaxValue, short.MaxValue]);
        mix.Start([short.MaxValue]);
        mix.Fill(block);
        Assert.Equal(short.MaxValue, block[0]);   // two at full: held at the top, not wrapped round
    }

    [Fact]
    public void ResamplingKeepsTheLengthInTimeAndDrawsTheLineBetweenSamples()
    {
        short[] samples = [0, 100, 200, 300];
        Assert.Equal(samples, WaveFile.Resample(samples, 44100, 44100));
        Assert.NotSame(samples, WaveFile.Resample(samples, 44100, 44100));
        Assert.Equal([0, 50, 100, 150, 200, 250, 300, 300], WaveFile.Resample(samples, 22050, 44100));
        Assert.Equal([0, 200], WaveFile.Resample(samples, 44100, 22050));
        Assert.Empty(WaveFile.Resample([], 22050, 44100));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveFile.Resample(samples, 0, 44100));
    }

    /// <summary>The level sound <paramref name="n"/> holds throughout: what tells it apart in the blocks.</summary>
    private static short Level(int n) => (short)(n * 100);

    private static byte[] Wave(int n, int length = 2000) =>
        WaveFile.Make(Enumerable.Repeat(Level(n), length).ToArray(), SoundLine.Rate);

    /// <summary>A device that keeps what was done to it, a block taking a millisecond (a real one's ten, faster).</summary>
    private sealed class Device : ISoundDevice
    {
        private readonly Lock _gate = new();
        private readonly List<string> _calls = [];
        private readonly List<short[]> _written = [];
        private int _opens;
        private int _closes;
        private volatile bool _opening = true;

        public bool Opening
        {
            get => _opening;
            set => _opening = value;
        }

        public int Failures { get; set; }

        public int Opens
        {
            get
            {
                lock (_gate)
                {
                    return _opens;
                }
            }
        }

        public int Closes
        {
            get
            {
                lock (_gate)
                {
                    return _closes;
                }
            }
        }

        public List<string> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public List<short[]> Written
        {
            get
            {
                lock (_gate)
                {
                    return [.. _written];
                }
            }
        }

        /// <summary>Whether a block held the level alone (its sound, once the one before has faded out).</summary>
        public bool Heard(short level) => Written.Any(block => block.All(s => s == level));

        public bool Open(int rate, int blockLength)
        {
            lock (_gate)
            {
                _opens++;
                _calls.Add($"open {rate} {blockLength}");
            }
            return _opening;
        }

        public bool Write(short[] block)
        {
            Thread.Sleep(1);
            lock (_gate)
            {
                if (Failures > 0)
                {
                    Failures--;
                    _calls.Add("write failed");
                    return false;
                }
                _calls.Add("write");
                _written.Add([.. block]);
                return true;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closes++;
                _calls.Add("close");
            }
        }
    }
}
