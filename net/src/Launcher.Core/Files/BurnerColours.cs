using System.Buffers.Binary;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A machine's own trail colours, from its attachment file (<c>.fmi</c>: a stock machine's in <c>vehicle/NAME_p.arc</c>,
/// a custom machine's in its booster's <c>vehicle_parts/parts_bstNN.fmi</c>). The game copies them into each burner's
/// record (main.rel <c>fn_1_983CC</c>): byte 3 the burner count, the burners 0x38 bytes each from 0x40, each with
/// its colour at rest at +0x20 and boosting at +0x2C, three floats 0 to 255 (high byte first).
/// </summary>
public static class BurnerColours
{
    private const int First = 0x40, Size = 0x38, Rest = 0x20, Boosting = 0x2C;

    /// <summary>The trail's colours as the file has them, each once: every burner's at rest, then every burner's
    /// boosting. Empty for a file with no burners, a burner past the file's end, or a colour that is no number.</summary>
    public static IReadOnlyList<Rgb> Read(byte[]? fmi)
    {
        if (fmi is null || fmi.Length < First)
        {
            return [];
        }
        var count = fmi[3];
        if (First + (count * Size) > fmi.Length)
        {
            return [];
        }
        var colours = new List<Rgb>();
        foreach (var at in (int[])[Rest, Boosting])
        {
            for (var i = 0; i < count; i++)
            {
                if (Colour(fmi.AsSpan(First + (i * Size) + at)) is not { } colour)
                {
                    return [];
                }
                if (!colours.Contains(colour))
                {
                    colours.Add(colour);
                }
            }
        }
        return colours;
    }

    private static Rgb? Colour(ReadOnlySpan<byte> at)
    {
        Span<byte> rgb = stackalloc byte[3];
        for (var k = 0; k < 3; k++)
        {
            var v = BinaryPrimitives.ReadSingleBigEndian(at[(k * 4)..]);
            if (!float.IsFinite(v))
            {
                return null;
            }
            rgb[k] = (byte)Math.Clamp(MathF.Round(v), 0, 255);
        }
        return new Rgb(rgb[0], rgb[1], rgb[2]);
    }
}
