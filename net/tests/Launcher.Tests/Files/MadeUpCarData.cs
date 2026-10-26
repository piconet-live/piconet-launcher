using System.Buffers.Binary;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>A made-up car data file in the shape of the game's, for the tests of a
/// stat pack's import: 41 machine blocks, twelve zero bytes, names, 75 part blocks, the parts' names; every block's
/// first word 0, every other word a number worked out here from the block and the offset (never the game's), low
/// byte first, and the four bytes at 0x48 as they stand.</summary>
internal static class MadeUpCarData
{
    public const int Length = 0x549A;

    /// <summary>A block's made-up word as a pack holds it. Every one is inside its range (a weight of a thousand and
    /// more, a boost strength of ten and more, every other number between 0.02 and 0.08), so a file made of them and
    /// changed in one place gives a pack of that one value.</summary>
    public static uint Word(int block, int at) => at switch
    {
        0x48 => ((uint)(block % 200) << 24) | ((uint)(block % 4) << 16),
        0x04 => BitConverter.SingleToUInt32Bits(1000 + block),
        0x34 => BitConverter.SingleToUInt32Bits(10 + (block / 8f)),
        _ => BitConverter.SingleToUInt32Bits(0.02f + (((block * 44) + (at / 4)) / 100000f)),
    };

    /// <summary>The unpacked file, with <paramref name="change"/>'s word where it gives one.</summary>
    public static byte[] File(Func<int, int, uint?>? change = null, string names = "RAINBOW\0MADE UP\0")
    {
        var data = new byte[Length];
        for (var block = 0; block < StatPackRules.Blocks; block++)
        {
            for (var at = 4; at < StatPackRules.BlockSize; at += 4)
            {
                var word = change?.Invoke(block, at) ?? Word(block, at);
                var place = data.AsSpan(StatPackRules.BlockAt(block) + at);
                if (at == 0x48)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(place, word);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(place, word);
                }
            }
        }
        System.Text.Encoding.ASCII.GetBytes(names).CopyTo(data, 0x1CE0);
        System.Text.Encoding.ASCII.GetBytes("PART A\0PART B\0").CopyTo(data, 0x53C0);
        return data;
    }

    /// <summary>The file with these words changed: (block, offset) to the word.</summary>
    public static byte[] With(params (int Block, int At, uint Bits)[] changes) =>
        File((block, at) => changes.Where(c => c.Block == block && c.At == at).Select(c => (uint?)c.Bits).FirstOrDefault());

    public static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}
