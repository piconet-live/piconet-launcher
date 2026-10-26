using System.Buffers.Binary;
using System.Text;

namespace Launcher.Tests;

/// <summary>A made-up GCAX sound bank in the game's layout (<see cref="Launcher.Core.GcaxBank"/>): no game data.</summary>
internal static class MadeUpSoundBank
{
    /// <summary>A wave: its rate, its DSP ADPCM frames and how many samples they hold.</summary>
    public sealed record Wave(int Rate, byte[] Frames, int Samples, short[]? Coefficients = null);

    /// <summary>Frames that decode to <paramref name="nibbles"/> × 2^<paramref name="scale"/> each, with the
    /// predictor's coefficients all 0 (header: predictor 0, the scale's power).</summary>
    public static Wave Plain(int rate, int scale, params int[] nibbles)
    {
        var frames = new byte[((nibbles.Length + 13) / 14) * 8];
        for (var i = 0; i < nibbles.Length; i++)
        {
            var (frame, k) = (i / 14, i % 14);
            frames[frame * 8] = (byte)scale;
            var at = (frame * 8) + 1 + (k >> 1);
            frames[at] |= (byte)((k & 1) == 0 ? (nibbles[i] & 15) << 4 : nibbles[i] & 15);
        }
        return new Wave(rate, frames, nibbles.Length);
    }

    /// <summary>The bank: each group's records (program, volume), each program's voice (wave, key, fine), the waves.</summary>
    public static byte[] Make(IReadOnlyDictionary<ushort, (int Program, int Volume)[]> groups,
        IReadOnlyList<(int Program, int Wave, int Key, int Fine)> voices, IReadOnlyList<Wave> waves)
    {
        var bank = new List<byte>(new byte[0x10]);
        var table = bank.Count;
        bank.AddRange(Encoding.ASCII.GetBytes("gcaxTBLD"));
        bank.AddRange(new byte[0x58]);   // to +0x60: the sub-tables' places at +0x38
        var subs = new int[9];

        // [3] the groups: count - 1, the groups' ids and places, then each group's records
        subs[3] = bank.Count;
        var g = bank.Count;
        bank.AddRange(new byte[4 + (4 * groups.Count)]);
        bank[g] = (byte)(groups.Count - 1);
        var gi = 0;
        foreach (var (id, records) in groups)
        {
            Put16(bank, g + 4 + (4 * gi), id);
            Put16(bank, g + 6 + (4 * gi), bank.Count - g);
            var at = bank.Count;
            bank.AddRange(new byte[4 + (4 * records.Length)]);
            bank[at] = (byte)(records.Length - 1);
            for (var r = 0; r < records.Length; r++)
            {
                Put32(bank, at + 4 + (4 * r), bank.Count - g);
                bank.AddRange([0xC0, 0xDE, (byte)records[r].Program, (byte)records[r].Volume, 0x80, 0xFF]);
            }
            gi++;
        }

        // [4] the voices: a header, the records, and one that is no voice
        subs[4] = bank.Count;
        bank.AddRange(new byte[0x30]);
        foreach (var (program, wave, key, fine) in voices)
        {
            var v = new byte[0x30];
            (v[0], v[1], v[2], v[3], v[10], v[11]) = ((byte)program, 0x80, 1, (byte)wave, (byte)(sbyte)key, (byte)fine);
            bank.AddRange(v);
        }
        bank.AddRange(new byte[0x30]);

        // [8] the waves: count - 1, then 0x44 each
        subs[8] = bank.Count;
        var w = bank.Count;
        bank.AddRange(new byte[4 + (0x44 * waves.Count)]);
        Put32(bank, w, waves.Count - 1);
        var data = 0;
        for (var i = 0; i < waves.Count; i++)
        {
            var at = w + 4 + (0x44 * i);
            var wave = waves[i];
            var frames = (wave.Samples + 13) / 14;
            Put32(bank, at, data);
            Put32(bank, at + 4, 2);
            Put32(bank, at + 8, wave.Samples + (2 * frames) - 1);
            for (var k = 0; k < 16; k++)
            {
                Put16(bank, at + 12 + (2 * k), wave.Coefficients is { } c && k < c.Length ? (ushort)c[k] : 0);
            }
            Put16(bank, at + 0x3C, wave.Rate);
            Put32(bank, at + 0x40, wave.Frames.Length);
            data += wave.Frames.Length;
        }
        for (var i = 0; i < 9; i++)
        {
            Put32(bank, table + 0x38 + (4 * i), subs[i] == 0 ? 0 : subs[i] - table - 0x20);
        }

        bank.AddRange(Encoding.ASCII.GetBytes("gcaxPCMD"));
        bank.AddRange(new byte[0x18]);
        foreach (var wave in waves)
        {
            bank.AddRange(wave.Frames);
        }
        return [.. bank];
    }

    private static void Put16(List<byte> bank, int at, int value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value);
        (bank[at], bank[at + 1]) = (b[0], b[1]);
    }

    private static void Put32(List<byte> bank, int at, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        for (var k = 0; k < 4; k++)
        {
            bank[at + k] = b[k];
        }
    }

    /// <summary>A bank holding every sound the launcher chooses (<see cref="Launcher.Core.GameSounds.Ids"/>), each its
    /// own wave: 44.1 kHz with the key and fine of the game's (+5, 35/64), the confirm's (the machine stage's) 16 kHz
    /// (-12, 0); the group A900 is the old machine stage's, which no longer is chosen. Without
    /// <paramref name="riser"/>, the menu group ends before the riser's record (A9011700): the bank lacks it.</summary>
    public static byte[] Chosen(bool riser = true)
    {
        (int, int)[] Records(int count, Dictionary<int, int> programs) =>
            [.. Enumerable.Range(0, count).Select(r => (programs.GetValueOrDefault(r), 100))];
        var groups = new Dictionary<ushort, (int, int)[]>
        {
            [0xA900] = Records(7, new() { [6] = 1 }),
            [0xA901] = Records(riser ? 0x18 : 0x17, new() { [0x00] = 2, [0x01] = 1, [0x02] = 3, [0x0B] = 5, [0x17] = 7 }),
        };
        var voices = Enumerable.Range(0, 8).Select(p => (p, p, p == 1 ? -12 : 5, p == 1 ? 0 : 35)).ToList();
        var waves = Enumerable.Range(0, 8).Select(p => Plain(p == 1 ? 16000 : 44100, 8, [.. Enumerable.Range(0, 20 + p).Select(i => (i % 15) - 7)])).ToList();
        return Make(groups, voices, waves);
    }
}
