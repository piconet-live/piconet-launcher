using System.Buffers.Binary;

namespace Launcher.Core;

/// <summary>
/// The machines' stats as the game's disc holds them, read from bytes that
/// are the player's and may be anything: what is not the game's table is no table, and nothing here raises or makes
/// room for a size a file names.
/// <para>The numbers are in the car data file, <c>game/cardata.lz</c>: unpacked, 41 blocks of 0xB4 bytes, one a
/// machine by id, low byte first; a stat's place in a block is <see cref="MachineStatInfo.At"/>. The letters of the
/// machine select (body, boost, grip) are not in that file: they are a table in the game's main module, which the
/// disc holds encrypted and packed (<c>enemy_line/line__.bin</c>).</para>
/// </summary>
public static class MachineStatFile
{
    /// <summary>The car data file's path on the disc, without the ".lz" it is packed under.</summary>
    public const string CarData = "game/cardata";

    /// <summary>The main module's path on the disc.</summary>
    public const string MainModule = "enemy_line/line__.bin";

    /// <summary>A machine's block in the car data file.</summary>
    public const int BlockSize = 0xB4;

    /// <summary>Where the letters stand in the main module of the pinned disc (<see cref="DiscPins.Game"/>), once
    /// decrypted and unpacked: 0x416B8 into its data section. 41 machines, three words each (body, boost, grip), high
    /// byte first, 1 (A) to 5 (E). The place is that one file's: it is read only from a file with the pin's hash.</summary>
    public const int RatingsAt = 0x1AECB8;

    private const int RatingWords = 3;

    /// <summary>The heaviest a machine can be said to be: far above any of the game's, and a number still.</summary>
    private const float MostWeight = 100_000;

    // The main module's cipher, as the game's loader undoes it (port/setup/mainrel.py names its source): every word,
    // high byte first, is XORed with the next state of state = state * Multiplier + Addend.
    private const uint Start = 0xAF8910BE;
    private const uint Multiplier = 0x9B36BB94;
    private const uint Addend = 0x000CD8F3;

    /// <summary>Each machine's numbers from the unpacked car data, by <see cref="MachineStat"/>; null when the bytes
    /// are not the table: shorter than 41 blocks, or with a value that is no number, a weight that is none, or a
    /// number beyond any the game computes with.</summary>
    public static float?[][]? Numbers(ReadOnlySpan<byte> carData)
    {
        var machines = Machines.Names.Count;
        if (carData.Length < machines * BlockSize)
        {
            return null;
        }
        var table = new float?[machines][];
        for (var machine = 0; machine < machines; machine++)
        {
            var block = carData.Slice(machine * BlockSize, BlockSize);
            var row = new float?[MachineStats.All.Count];
            foreach (var stat in MachineStats.All)
            {
                if (stat.At < 0)
                {
                    continue;
                }
                var value = BinaryPrimitives.ReadSingleLittleEndian(block[stat.At..]);
                if (!float.IsFinite(value) || MathF.Abs(value) > MostWeight || (stat.Stat == MachineStat.Weight && value <= 0))
                {
                    return null;
                }
                row[(int)stat.Stat] = value;
            }
            table[machine] = row;
        }
        return table;
    }

    /// <summary>Each machine's three letters (1 to 5: body, boost, grip) from the unpacked main module; null when it
    /// is too short or a word is no letter.</summary>
    public static int[][]? Ratings(ReadOnlySpan<byte> mainModule)
    {
        var machines = Machines.Names.Count;
        if (mainModule.Length < RatingsAt + (machines * RatingWords * 4))
        {
            return null;
        }
        var table = new int[machines][];
        for (var machine = 0; machine < machines; machine++)
        {
            table[machine] = new int[RatingWords];
            for (var k = 0; k < RatingWords; k++)
            {
                var word = BinaryPrimitives.ReadUInt32BigEndian(mainModule[(RatingsAt + (((machine * RatingWords) + k) * 4))..]);
                if (word is < 1 or > 5)
                {
                    return null;
                }
                table[machine][k] = (int)word;
            }
        }
        return table;
    }

    /// <summary>The main module's file with its cipher undone: a packed file (two words, low byte first: the packed
    /// length and the unpacked one, then the data). The cipher is its own inverse.</summary>
    public static byte[] Decrypted(ReadOnlySpan<byte> file)
    {
        var plain = file.ToArray();
        var state = Start;
        Span<byte> last = stackalloc byte[4];
        for (var at = 0; at < plain.Length; at += 4)
        {
            state = unchecked((state * Multiplier) + Addend);
            if (at + 4 <= plain.Length)
            {
                BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(at), BinaryPrimitives.ReadUInt32BigEndian(plain.AsSpan(at)) ^ state);
                continue;
            }
            // a file that does not end on a word: its last bytes against the state's high ones
            BinaryPrimitives.WriteUInt32BigEndian(last, state);
            for (var k = 0; at + k < plain.Length; k++)
            {
                plain[at + k] ^= last[k];
            }
        }
        return plain;
    }

    /// <summary>The table of the numbers and, when there are any, the letters.</summary>
    public static MachineStatTable Table(float?[][] numbers, int[][]? ratings)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        return MachineStatTable.Of((machine, stat) => stat switch
        {
            MachineStat.BodyRating => ratings?[machine][0],
            MachineStat.BoostRating => ratings?[machine][1],
            MachineStat.GripRating => ratings?[machine][2],
            _ => numbers[machine][(int)stat],
        });
    }
}
