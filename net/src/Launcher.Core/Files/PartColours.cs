using System.Buffers.Binary;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The custom parts' colours as the game holds them. A part's colour
/// is one word, high byte first: red, green, blue, then the paint slot, which names the meshes of the part's model
/// it paints and is always <see cref="GameFacts.Files.PartPaintSlot"/>; a word of 0 is the part's stock colour 0.
/// <para>Each of the 75 parts (bodies 0-24, cockpits 25-49, boosters 50-74) has four stock colours, stock colour 0
/// its own: a table in the game's main module (<c>enemy_line/line__.bin</c>, decrypted and unpacked as for
/// <see cref="MachineStatFile.Ratings"/>), read from the player's disc by <see cref="DiscArt.PartColours()"/>. The
/// bytes may be anything: what is not the game's table is no table, and nothing here raises.</para>
/// </summary>
public static class PartColours
{
    /// <summary>Where the table stands in the main module of the pinned disc (<see cref="DiscPins.Game"/>), once
    /// decrypted and unpacked: 0xD048 into main.rel's read-only data (.rodata, 0x15F710 into the module). 75 parts,
    /// four colour words each, stock colour 0 first. The place is that one file's.</summary>
    public const int TableAt = 0x16C758;

    /// <summary>Stock colours of a part.</summary>
    public const int PerPart = 4;

    /// <summary>The four stock colours of every part (<c>[part][n]</c>, part 0-74, n 0-3) from the unpacked main
    /// module; null when it is too short or a word is not a colour of the paint slot.</summary>
    public static uint[][]? Read(ReadOnlySpan<byte> mainModule)
    {
        const int parts = CustomMachine.Parts;
        if (mainModule.Length < TableAt + (parts * PerPart * 4))
        {
            return null;
        }
        var table = new uint[parts][];
        for (var part = 0; part < parts; part++)
        {
            table[part] = new uint[PerPart];
            for (var n = 0; n < PerPart; n++)
            {
                var word = BinaryPrimitives.ReadUInt32BigEndian(mainModule[(TableAt + (((part * PerPart) + n) * 4))..]);
                if ((word & 0xFF) != GameFacts.Files.PartPaintSlot)
                {
                    return null;
                }
                table[part][n] = word;
            }
        }
        return table;
    }

    /// <summary>The colour word of a paint: red, green and blue (any 0-255, as the game's Garage allows) and the paint
    /// slot. Never 0, even for black.</summary>
    public static uint Word(byte red, byte green, byte blue) =>
        ((uint)red << 24) | ((uint)green << 16) | ((uint)blue << 8) | GameFacts.Files.PartPaintSlot;

    /// <summary>The red, green and blue of a colour word (0, the part's own colour, gives black: look up the part's
    /// stock colour 0 for it).</summary>
    public static (byte Red, byte Green, byte Blue) Rgb(uint word) => ((byte)(word >> 24), (byte)(word >> 16), (byte)(word >> 8));
}
