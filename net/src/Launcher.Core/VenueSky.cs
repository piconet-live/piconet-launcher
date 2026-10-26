using System.Numerics;

namespace Launcher.Core;

/// <summary>
/// The colours a course's model is drawn in on the Practice page, each 0 to 1 red, green, blue: a dusk sky from
/// <see cref="Top"/> down to <see cref="Horizon"/>, a glowing band (<see cref="Glow"/>) where it meets the
/// <see cref="Ground"/>, and the model in flat shades from <see cref="Dark"/> edge-on to <see cref="Lit"/> facing the
/// light. Taken from the venue's own picture (<see cref="From"/>): its hues, at night and dusk lightness.
/// </summary>
public readonly record struct VenueSky(Vector3 Top, Vector3 Horizon, Vector3 Glow, Vector3 Ground, Vector3 Dark, Vector3 Lit)
{
    /// <summary>Where the glowing band lies, down the picture (0 the top, 1 the foot).</summary>
    public const float BandAt = 0.62f;

    /// <summary>The launcher's slate blues lit towards cyan (the race page's map), under a night of the same blue: a
    /// course whose venue has no picture.</summary>
    public static VenueSky Plain { get; } = Tones(215, 0.45f, 210, 0.45f, 205, 0.40f, 205, 0.45f);

    /// <summary>The sky of a venue's picture: the hue and colourfulness of its top third (the sky), its middle (the
    /// horizon's glow) and its foot (the ground and the model), each at a fixed night or dusk lightness, so that every
    /// venue reads as the same kind of picture in its own colours. A grey picture gives grey tones.</summary>
    public static VenueSky From(Picture? picture)
    {
        if (picture is not { Width: > 0, Height: > 2 } p || p.Rgba.Length < p.Width * p.Height * 4)
        {
            return Plain;
        }
        var third = p.Height / 3;
        var (skyHue, skySat) = Hue(p, 0, third);
        var (midHue, midSat) = Hue(p, third, 2 * third);
        var (footHue, footSat) = Hue(p, 2 * third, p.Height);
        return Tones(skyHue, skySat, midHue, midSat, footHue, footSat, midHue, (midSat + footSat) / 2);
    }

    private static VenueSky Tones(float skyHue, float skySat, float midHue, float midSat, float footHue, float footSat, float modelHue, float modelSat)
    {
        // a picture's mean colourfulness is low (its colours average out): raised, within a ceiling per role
        // (a grey picture stays grey)
        static float S(float sat, float most) => sat < 0.01f ? 0 : Math.Clamp(0.12f + (sat * 3), 0, most);
        return new VenueSky(
            Top: Hsl(skyHue, S(skySat, 0.55f), 0.07f),
            Horizon: Hsl(midHue, S(midSat, 0.60f), 0.19f),
            Glow: Hsl(midHue, S(midSat, 0.90f), 0.55f),
            Ground: Hsl(footHue, S(footSat, 0.45f), 0.08f),
            Dark: Hsl(modelHue, S(modelSat, 0.45f), 0.30f),
            Lit: Hsl(modelHue, S(modelSat, 0.70f), 0.74f));
    }

    /// <summary>The mean hue (degrees) and colourfulness (0 to 1) of rows <paramref name="from"/> to
    /// <paramref name="to"/>: each pixel's hue as a direction, as long as its chroma, so that grey pixels count for
    /// nothing and a hue near 0 and one near 360 meet.</summary>
    private static (float Hue, float Saturation) Hue(Picture p, int from, int to)
    {
        var sum = Vector2.Zero;
        var count = 0;
        for (var y = from; y < to; y++)
        {
            for (var x = 0; x < p.Width; x++)
            {
                var i = ((y * p.Width) + x) * 4;
                var (r, g, b) = (p.Rgba[i] / 255f, p.Rgba[i + 1] / 255f, p.Rgba[i + 2] / 255f);
                var max = MathF.Max(r, MathF.Max(g, b));
                var chroma = max - MathF.Min(r, MathF.Min(g, b));
                count++;
                if (chroma <= 0)
                {
                    continue;
                }
                var h = max == r ? (g - b) / chroma % 6 : max == g ? ((b - r) / chroma) + 2 : ((r - g) / chroma) + 4;
                var angle = h * MathF.PI / 3;
                sum += new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * chroma;
            }
        }
        if (count == 0)
        {
            return (0, 0);
        }
        var mean = sum / count;
        var hue = MathF.Atan2(mean.Y, mean.X) * 180 / MathF.PI;
        return (hue < 0 ? hue + 360 : hue, mean.Length());
    }

    /// <summary>Hue in degrees, saturation and lightness 0 to 1, to red, green, blue.</summary>
    public static Vector3 Hsl(float hue, float saturation, float lightness)
    {
        var c = (1 - MathF.Abs((2 * lightness) - 1)) * saturation;
        var h = ((hue % 360) + 360) % 360 / 60;
        var x = c * (1 - MathF.Abs((h % 2) - 1));
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0f),
            < 2 => (x, c, 0f),
            < 3 => (0f, c, x),
            < 4 => (0f, x, c),
            < 5 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        var m = lightness - (c / 2);
        return new Vector3(r + m, g + m, b + m);
    }
}
