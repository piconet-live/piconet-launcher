using System.Buffers.Binary;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>Made-up stat files for a made-up disc: a car data file and a main module whose every value is worked
/// out here from the machine's id, never taken from the game. The values are not in the order of the ids, so a list
/// sorted by one differs from the list by number.</summary>
internal static class MadeUpStats
{
    /// <summary>A machine's made-up number for a stat: a hundred a stat, and within it the ids shuffled.</summary>
    public static float Value(int machine, MachineStat stat) => ((int)stat * 100) + ((machine * 7) % 41) + 0.5f;

    /// <summary>A machine's made-up letter (1 to 5): 0 body, 1 boost, 2 grip.</summary>
    public static int Rating(int machine, int which) => 1 + (((machine * 3) + which) % 5);

    /// <summary>The unpacked car data: 41 blocks, each stat at its place, low byte first; what is between them is
    /// filled with 0xEE, so that a value read from a wrong place is seen.</summary>
    public static byte[] CarData(Func<int, MachineStat, float>? value = null)
    {
        var data = new byte[(41 * MachineStatFile.BlockSize) + 300];        // the game's file goes on after the blocks
        data.AsSpan().Fill(0xEE);
        for (var machine = 0; machine < 41; machine++)
        {
            foreach (var stat in MachineStats.All.Where(s => s.At >= 0))
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((machine * MachineStatFile.BlockSize) + stat.At),
                    (value ?? Value)(machine, stat.Stat));
            }
        }
        return data;
    }

    /// <summary>The unpacked main module: nothing but the letters' table at its place, high byte first.</summary>
    public static byte[] MainModule(Func<int, int, int>? rating = null)
    {
        var module = new byte[MachineStatFile.RatingsAt + (41 * 3 * 4) + 64];
        for (var machine = 0; machine < 41; machine++)
        {
            for (var which = 0; which < 3; which++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(module.AsSpan(MachineStatFile.RatingsAt + (((machine * 3) + which) * 4)),
                    (uint)(rating ?? Rating)(machine, which));
            }
        }
        return module;
    }

    /// <summary>A file packed the plainest way the format has (every byte as itself, eight to a flag byte of 0xFF)
    /// under its two words: the packed length and the unpacked one.</summary>
    public static byte[] Packed(ReadOnlySpan<byte> data, int? says = null)
    {
        var body = new List<byte>(data.Length + (data.Length / 8) + 1);
        for (var at = 0; at < data.Length; at += 8)
        {
            body.Add(0xFF);
            body.AddRange(data.Slice(at, Math.Min(8, data.Length - at)));
        }
        var file = new byte[8 + body.Count];
        BinaryPrimitives.WriteInt32LittleEndian(file, body.Count);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), says ?? data.Length);
        body.CopyTo(file, 8);
        return file;
    }

    /// <summary>The main module's cipher, written out here on its own (64-bit arithmetic, a word at a time from a
    /// padded copy), as the game's loader has it: each word, high byte first, against the next state of
    /// state = state * 0x9B36BB94 + 0xCD8F3 from 0xAF8910BE.</summary>
    public static byte[] Cipher(ReadOnlySpan<byte> data)
    {
        var padded = new byte[(data.Length + 3) / 4 * 4];
        data.CopyTo(padded);
        ulong state = 0xAF8910BE;
        for (var at = 0; at < padded.Length; at += 4)
        {
            state = ((state * 0x9B36BB94UL) + 0xCD8F3UL) & 0xFFFFFFFFUL;
            var word = (uint)((padded[at] << 24) | (padded[at + 1] << 16) | (padded[at + 2] << 8) | padded[at + 3]) ^ (uint)state;
            (padded[at], padded[at + 1], padded[at + 2], padded[at + 3]) = ((byte)(word >> 24), (byte)(word >> 16), (byte)(word >> 8), (byte)word);
        }
        return padded[..data.Length];
    }

    /// <summary>A made-up disc with both files: the car data packed as <c>game/cardata.lz</c>, the main module
    /// packed and enciphered as <c>enemy_line/line__.bin</c>. A null leaves the disc's own file (or none).</summary>
    public static SyntheticDisc Disc(byte[]? carData, byte[]? mainModule)
    {
        var disc = new SyntheticDisc();
        if (carData is not null)
        {
            disc.Files[MachineStatFile.CarData + ".lz"] = Fzgx.GameCube.Compression.AvLz.Compress(carData);
        }
        if (mainModule is not null)
        {
            disc.Files[MachineStatFile.MainModule] = Cipher(Packed(mainModule));
        }
        return disc;
    }
}
