using System.Numerics;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>One soft spot of light of a flame: where (the model's space), how large, its colour (0 to 1 each), how
/// much of its middle is white-hot (0 to 1) and how bright it is (0 to 1).</summary>
public readonly record struct FlameSprite(Vector3 At, float Radius, Vector3 Colour, float Heat, float Bright);

/// <summary>
/// A machine's engine flames as soft spots of light (drawn added to the picture: <c>Launcher.App/Gpu/Studio.cs</c>,
/// Glow): from each burner (<see cref="Burner"/>) a plume going back towards the tail, white-hot at the burner and of
/// the burner's colour further out, narrowing and fading, flickering as a flame does; a faint halo round the burner.
/// Plain maths, so that the flames are the same at the same moment.
/// </summary>
public static class EngineFlames
{
    /// <summary>The spots along one plume.</summary>
    public const int Along = 20;

    /// <summary>The flames of <paramref name="burners"/> <paramref name="seconds"/> in, at rest (<paramref name="boost"/>
    /// 0) or boosting (1: the boosting colour, a longer plume).</summary>
    public static IReadOnlyList<FlameSprite> Sprites(IReadOnlyList<Burner> burners, float seconds, float boost = 0)
    {
        ArgumentNullException.ThrowIfNull(burners);
        var sprites = new List<FlameSprite>(burners.Count * (Along + 1));
        boost = Math.Clamp(boost, 0, 1);
        for (var b = 0; b < burners.Count; b++)
        {
            var burner = burners[b];
            var radius = Radius(burner.Size);
            if (radius <= 0)
            {
                continue;
            }
            var colour = Vivid(Vector3.Lerp(Of(burner.Rest), Of(burner.Boosting), boost));
            var flicker = 0.86f + (0.14f * Wobble((seconds * 23) + (b * 7.1f)));
            var length = radius * (6.5f + (4.5f * boost)) * (0.9f + (0.1f * Wobble((seconds * 11) + (b * 3.3f))));
            sprites.Add(new FlameSprite(burner.At + new Vector3(0, 0, radius * 0.3f), radius * (2.6f + boost), colour, 0, (0.22f + (0.15f * boost)) * flicker));
            for (var i = 0; i < Along; i++)
            {
                var f = i / (float)(Along - 1);
                var at = burner.At + new Vector3(0, 0, (radius * 0.25f) + (length * f));
                var size = radius * (1.0f - (0.55f * f)) * (0.92f + (0.08f * Wobble((seconds * 31) + i + (b * 1.7f))));
                var bright = MathF.Pow(1 - f, 1.2f) * flicker * (0.55f + (0.2f * boost));
                sprites.Add(new FlameSprite(at, size, colour, MathF.Pow(1 - f, 8), bright));
            }
        }
        return sprites;
    }

    /// <summary>A burner's flame's radius at the burner (the model's units) by the size the game gives it.</summary>
    public static float Radius(float size) => size <= 0 ? 0 : 0.16f + (0.006f * size);

    private static Vector3 Of(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    /// <summary>The colour at full strength and a little deeper: the burner's colours are pale (the game's flame
    /// texture is multiplied by them).</summary>
    private static Vector3 Vivid(Vector3 c)
    {
        var top = Math.Max(c.X, Math.Max(c.Y, c.Z));
        if (top <= 0)
        {
            return Vector3.One;
        }
        var full = c / top;
        return new Vector3(MathF.Pow(full.X, 1.6f), MathF.Pow(full.Y, 1.6f), MathF.Pow(full.Z, 1.6f));
    }

    /// <summary>A flame's restless wave, -1 to 1: three waves of their own.</summary>
    private static float Wobble(float x) => (MathF.Sin(x) + (0.5f * MathF.Sin((2.3f * x) + 1)) + (0.25f * MathF.Sin((5.1f * x) + 2))) / 1.75f;
}
