using System.Buffers.Binary;

namespace Launcher.Core;

/// <summary>A sound of the game's bank, decoded: 16-bit mono samples, the rate the game plays them at
/// (<see cref="GcaxBank.PlayedRate"/>), the wave's own rate as the bank names it, and the game's volume for the sound
/// (0 to 127).</summary>
public sealed record BankSound(int Rate, int OwnRate, short[] Samples, int Volume);

/// <summary>
/// The game's menu sound bank (<c>snd/normal_se/PACK0.bin</c>): a Sega
/// GCAX bank of GameCube DSP ADPCM waves, read by our own code from the layout measured in the file (as the web
/// editor's <c>gcax.ts</c> and a Python decoder, <c>gcax.py</c>). The <c>gcaxTBLD</c> chunk points (at +0x38, each relative
/// to +0x20) to its sub-tables: [3] the sound groups, [4] the voices, [8] the waves; the <c>gcaxPCMD</c> chunk holds
/// the waves' data from +0x20. A sound's id is its group, its record in the group and 00 (<c>A9010700</c>: group
/// A901, record 7). A record (<c>C0 xx PP VV 80 FF</c>) names a program PP and its volume VV; the program's first
/// voice (0x30 bytes after a header of 0x30) names its wave (+3), a key in semitones (+10, signed) and a fine tune in
/// 64ths of a semitone (+11), both against the console's 32 kHz: the game plays the wave at
/// 32000 × 2^((key + fine/64)/12) samples a second (every program of the bank but one names its own wave's
/// rate this way, to within a hundredth of a semitone; A9010600 plays three semitones up). A wave (0x44 bytes) holds
/// its data's start in PCMD (+0), its last nibble (+8), the predictors' 16 coefficients (+12), its rate (+0x3C) and
/// its data's length (+0x40). Two waves loop in the game (A9000000, A9000600); a sound here is one pass.
/// <para>The bank is the player's file: every place it names is checked against its length, and nothing here raises
/// on its bytes (a bank that does not read is no sound: the launcher's own tones then sound).</para>
/// </summary>
public static class GcaxBank
{
    private const int VoiceSize = 0x30;
    private const int WaveSize = 0x44;

    /// <summary>The most samples a sound is decoded to: 30 s at 44.1 kHz (the bank's longest is 7.5 s).</summary>
    public const int MostSamples = 30 * 44100;

    /// <summary>The rate the game plays a voice's wave at: its key and fine tune against the console's 32 kHz.</summary>
    public static int PlayedRate(int key, int fine) => (int)Math.Round(32000 * Math.Pow(2, (key + (fine / 64.0)) / 12));

    /// <summary>The sound <paramref name="id"/> (<c>0xA9010700</c>) decoded from the bank; null when the bank has no
    /// such sound or does not read as a GCAX bank.</summary>
    public static BankSound? Sound(ReadOnlySpan<byte> bank, uint id)
    {
        try
        {
            return Find(bank, id);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static BankSound? Find(ReadOnlySpan<byte> b, uint id)
    {
        var table = b.IndexOf("gcaxTBLD"u8);
        var pcm = b.IndexOf("gcaxPCMD"u8);
        if (table < 0 || pcm < 0)
        {
            return null;
        }
        int Sub(int i, ReadOnlySpan<byte> b) => At(table + 0x20 + U32(b, table + 0x38 + (4 * i)), b);
        var (groups, voices, waves) = (Sub(3, b), Sub(4, b), Sub(8, b));

        // the group's record: its program and volume
        var (group, record) = ((int)(id >> 16), (int)((id >> 8) & 0xFF));
        int? program = null;
        var volume = 0;
        for (var g = 0; g <= U8(b, groups); g++)
        {
            if (U16(b, groups + 4 + (4 * g)) != group)
            {
                continue;
            }
            var at = At(groups + U16(b, groups + 6 + (4 * g)), b);
            if (record > U8(b, at))
            {
                return null;
            }
            var r = At(groups + U32(b, at + 4 + (4 * record)), b);
            (program, volume) = (U8(b, r + 2), U8(b, r + 3));
            break;
        }
        if (program is null)
        {
            return null;
        }

        // the program's first voice: records after the header, until one does not read as a voice
        int? wave = null;
        var (key, fine) = (0, 0);
        for (var o = voices + VoiceSize; o + VoiceSize <= b.Length; o += VoiceSize)
        {
            if (U8(b, o + 1) is not (0x80 or 0x00) || U8(b, o + 2) != 1)
            {
                break;
            }
            if (U8(b, o) == program)
            {
                (wave, key, fine) = (U8(b, o + 3), (sbyte)U8(b, o + 10), U8(b, o + 11));
                break;
            }
        }
        if (wave is not { } w || w > U32(b, waves))
        {
            return null;
        }

        var entry = waves + 4 + (WaveSize * w);
        var (start, endNibble, rate, size) = (U32(b, entry), U32(b, entry + 8), U16(b, entry + 0x3C), U32(b, entry + 0x40));
        Span<short> coefficients = stackalloc short[16];
        for (var k = 0; k < 16; k++)
        {
            coefficients[k] = (short)U16(b, entry + 12 + (2 * k));
        }
        var from = (long)pcm + 0x20 + start;
        if (rate == 0 || from + size > b.Length)
        {
            return null;
        }
        var samples = DspAdpcm.Decode(b.Slice((int)from, (int)size), coefficients, endNibble, MostSamples);
        return new BankSound(PlayedRate(key, fine), rate, samples, volume);
    }

    /// <summary>A place inside the bank, or not a bank.</summary>
    private static int At(long at, ReadOnlySpan<byte> b) =>
        at >= 0 && at < b.Length ? (int)at : throw new InvalidDataException("the bank names a place outside itself");

    private static int U8(ReadOnlySpan<byte> b, int at) => b[At(at, b)];

    private static int U16(ReadOnlySpan<byte> b, int at) =>
        at >= 0 && at + 2 <= b.Length ? BinaryPrimitives.ReadUInt16BigEndian(b[at..]) : throw new InvalidDataException("the bank ends early");

    private static long U32(ReadOnlySpan<byte> b, int at) =>
        at >= 0 && at + 4 <= b.Length ? BinaryPrimitives.ReadUInt32BigEndian(b[at..]) : throw new InvalidDataException("the bank ends early");
}

/// <summary>
/// GameCube DSP ADPCM: 8-byte frames of a header (the predictor's index in the high nibble, the scale's power of two
/// in the low one) and 14 four-bit samples, each predicted from the two before by the predictor's pair of
/// coefficients (fixed point, 11 bits of fraction).
/// </summary>
public static class DspAdpcm
{
    /// <summary>The samples of <paramref name="data"/>, <paramref name="endNibble"/> being the last nibble's index with
    /// the frames' header nibbles counted (so the sample count leaves those out), at most <paramref name="most"/>.</summary>
    public static short[] Decode(ReadOnlySpan<byte> data, ReadOnlySpan<short> coefficients, long endNibble, int most)
    {
        var nibbles = endNibble + 1;
        var count = (int)Math.Clamp(nibbles - (2 * ((nibbles + 15) / 16)), 0, Math.Min(most, (data.Length + 7) / 8 * 14));
        var output = new short[count];
        long h1 = 0, h2 = 0;
        var n = 0;
        for (var frame = 0; frame < data.Length && n < count; frame += 8)
        {
            var header = data[frame];
            var scale = 1L << (header & 15);
            var p = 2 * (header >> 4);
            var (c1, c2) = p + 1 < coefficients.Length ? (coefficients[p], coefficients[p + 1]) : ((short)0, (short)0);
            for (var i = 0; i < 14 && n < count; i++)
            {
                var at = frame + 1 + (i >> 1);
                var b = at < data.Length ? data[at] : 0;
                var nibble = (i & 1) == 0 ? b >> 4 : b & 15;
                if (nibble >= 8)
                {
                    nibble -= 16;
                }
                // (in 64 bits: the terms pass 32; the shift rounds down, as gcax.py's and gcax.ts's)
                var value = Math.Clamp(((nibble * scale * 2048) + 1024 + (c1 * h1) + (c2 * h2)) >> 11, short.MinValue, short.MaxValue);
                (h2, h1) = (h1, value);
                output[n++] = (short)value;
            }
        }
        return n == count ? output : output[..n];
    }
}
