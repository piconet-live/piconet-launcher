namespace Launcher.Core;

/// <summary>
/// The course draw's two sounds, made here in code (a listening set's
/// recommended mix takes the game's own sounds for the vote, the machines and the start, and B2-draw, ours, for the
/// draw, as the game's tick is very high and thin): a bright FM blip on every hop of the frame
/// (<see cref="LauncherSound.DrawHop"/>), at the hops' own pace, and a two-tone FM chime, B5 then E6, on the course
/// drawn (<see cref="LauncherSound.CourseDrawn"/>). Ported from the set's <c>tools/build_b.py</c> (<c>b2_hop</c>,
/// <c>B2_draw</c>). Nothing of the game's: they sound with or without a disc. Both are scaled by one factor, the
/// chime's loudest sample at <see cref="LobbyTones.Peak"/> times the volume, so a blip stays as much quieter than
/// the chime as in B2-draw.
/// </summary>
public static class DrawSounds
{
    /// <summary>Samples a second: mono, 16 bit (the blip's FM sidebands reach past 11 kHz).</summary>
    public const int Rate = 44100;

    /// <summary>A blip's level against the chime's notes (B2-draw's hops rise from 0.30 to 0.54; this is between):
    /// its loudest sample about a quarter of the chime's, as B2-draw's hops are 0.22 to 0.33.</summary>
    public const double HopLevel = 0.42;

    /// <summary>The chime's longest, in seconds: as long as it sounds in B2-draw.</summary>
    public const double ChimeLength = 0.715;

    private static readonly Lazy<double[]> HopSamples = new(MakeHop);
    private static readonly Lazy<double[]> ChimeSamples = new(MakeChime);

    /// <summary>Makes both sounds now (some tens of milliseconds, once a run), so that the draw's first hop does not
    /// wait for them: the launcher calls it on a worker as it starts.</summary>
    public static void Warm() => _ = (HopSamples.Value, ChimeSamples.Value);

    /// <summary>The draw's sound as a wave file at a volume of 0 to 100; null for any other sound.</summary>
    public static byte[]? Wave(LauncherSound sound, int volume) =>
        Samples(sound, volume) is { } samples ? WaveFile.Make(samples, Rate) : null;

    /// <summary>The draw's sound's samples at a volume of 0 to 100; null for any other sound.</summary>
    public static short[]? Samples(LauncherSound sound, int volume)
    {
        var raw = sound switch
        {
            LauncherSound.DrawHop => HopSamples.Value,
            LauncherSound.CourseDrawn => ChimeSamples.Value,
            _ => null,
        };
        if (raw is null)
        {
            return null;
        }
        var top = ChimeSamples.Value.Max(Math.Abs);
        var scale = short.MaxValue * LobbyTones.Peak * (Math.Clamp(volume, 0, 100) / 100.0) / top;
        return [.. raw.Select(v => (short)Math.Round(v * scale))];
    }

    /// <summary>B2's hop: a 45 ms FM blip at 1760 Hz (modulator 1.41 times, its index falling fast) and a 4 ms click
    /// of noise.</summary>
    private static double[] MakeHop()
    {
        var random = new Random(22);
        var s = Fm(1760, 1.41, 3.0, 0.045, HopLevel, decay: 70, indexDecay: 90);
        s = Mix(s, Noise(random, 0.004, 0.15 * HopLevel, hp: 0.6, lp: 0.9, decay: 1200), 0);
        return s;
    }

    /// <summary>B2's chime: B5, then E6 0.12 s later, each a bright FM tone and a slightly detuned one at a third of
    /// its level, in a light room (<see cref="Reverb"/>), cut where it falls below -66 dB or at <see cref="ChimeLength"/>
    /// (where B2-draw's 1.18 s ends after its hops), faded out over 80 ms (6 ms when it ends sooner).</summary>
    private static double[] MakeChime()
    {
        var s = new double[(int)(0.1 * Rate)];
        foreach (var (hz, at, level) in new[] { (Note(2), 0.0, 0.7), (Note(7), 0.12, 0.9) })
        {
            s = Mix(s, Fm(hz, 2.0, 2.8, 0.55, level, decay: 6, indexDecay: 10), at);
            s = Mix(s, Fm(hz * 1.004, 3.01, 1.2, 0.55, level * 0.35, decay: 8, indexDecay: 16), at);
        }
        s = Reverb(s, wet: 0.16);
        var floor = Math.Pow(10, -66 / 20.0) * s.Max(Math.Abs);
        var end = s.Length;
        while (end > 1 && Math.Abs(s[end - 1]) < floor)
        {
            end--;
        }
        var longest = (int)(ChimeLength * Rate);
        var cut = end > longest;
        s = s[..Math.Min(end, longest)];
        var fade = Math.Min(s.Length, (int)((cut ? 0.080 : 0.006) * Rate));
        for (var i = 0; i < fade; i++)
        {
            s[^(i + 1)] *= i / (double)fade;
        }
        return s;
    }

    /// <summary>A note's pitch by semitones above A5 (880 Hz): 2 is B5, 7 is E6.</summary>
    private static double Note(int semitones) => 880 * Math.Pow(2, semitones / 12.0);

    /// <summary>An FM tone: carrier <paramref name="hz"/>, modulator <paramref name="ratio"/> times it, the index
    /// decaying fast (a bright attack, a mellow tail), a 1 ms rise and a 20 ms release to silence.</summary>
    private static double[] Fm(double hz, double ratio, double index, double seconds, double level, double decay, double indexDecay)
    {
        var n = (int)(seconds * Rate);
        var attack = Math.Max(1, (int)(0.001 * Rate));
        var s = new double[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Rate;
            var m = index * Math.Exp(-indexDecay * t) * Math.Sin(2 * Math.PI * hz * ratio * t);
            s[i] = Math.Sin((2 * Math.PI * hz * t) + m) * Math.Exp(-decay * t) * Math.Min(1, i / (double)attack) * level;
        }
        var release = Math.Min(n, (int)(0.020 * Rate));
        for (var i = 0; i < release; i++)
        {
            s[n - 1 - i] *= i / (double)release;
        }
        return s;
    }

    /// <summary>A short burst of noise: a one-pole low-pass (<paramref name="lp"/>), less a slower copy of itself
    /// (<paramref name="hp"/>), decaying.</summary>
    private static double[] Noise(Random random, double seconds, double level, double hp, double lp, double decay)
    {
        var n = (int)(seconds * Rate);
        var s = new double[n];
        double y = 0, slow = 0;
        for (var i = 0; i < n; i++)
        {
            y += lp * (((random.NextDouble() * 2) - 1) - y);
            slow += hp * (y - slow);
            s[i] = (y - slow) * Math.Exp(-decay * i / Rate) * level;
        }
        return s;
    }

    /// <summary>Adds <paramref name="part"/> into <paramref name="into"/> from <paramref name="at"/> seconds, longer
    /// when it must be; the sum.</summary>
    private static double[] Mix(double[] into, double[] part, double at)
    {
        var first = (int)(at * Rate);
        if (into.Length < first + part.Length)
        {
            Array.Resize(ref into, first + part.Length);
        }
        for (var i = 0; i < part.Length; i++)
        {
            into[first + i] += part[i];
        }
        return into;
    }

    /// <summary>A light room (Schroeder, as Freeverb): four damped combs side by side, two all-passes after them,
    /// a 0.5 s tail; mono.</summary>
    private static double[] Reverb(double[] dry, double wet)
    {
        const double room = 0.80, damp = 0.35;
        var x = new double[dry.Length + (int)(0.5 * Rate)];
        dry.CopyTo(x, 0);
        var sum = new double[x.Length];
        foreach (var delay in new[] { 1116, 1188, 1277, 1356 })
        {
            var line = new double[delay];
            var at = 0;
            var store = 0.0;
            for (var i = 0; i < x.Length; i++)
            {
                var o = line[at];
                store = (o * (1 - damp)) + (store * damp);
                line[at] = x[i] + (store * room);
                at = (at + 1) % delay;
                sum[i] += o;
            }
        }
        foreach (var delay in new[] { 556, 225 })
        {
            var line = new double[delay];
            var at = 0;
            for (var i = 0; i < sum.Length; i++)
            {
                var b = line[at];
                var o = -sum[i] + b;
                line[at] = sum[i] + (b * 0.5);
                at = (at + 1) % delay;
                sum[i] = o;
            }
        }
        for (var i = 0; i < x.Length; i++)
        {
            x[i] += wet * 0.25 * sum[i];
        }
        return x;
    }
}
