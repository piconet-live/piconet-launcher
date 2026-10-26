using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The garage's colour wheel: hue around the wheel (0° red at the right, turning counter-clockwise on screen through
/// yellow and green, as colour wheels usually go), saturation from the centre (white) to the rim, and brightness on a
/// slider beside it; plus the "#RRGGBB" text the player can type. Plain maths, so the wheel and the text agree.
/// </summary>
public static class ColorWheel
{
    /// <summary>Hue (0-360), saturation and value (0-1) of a colour; a grey has hue 0.</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(Rgb color)
    {
        var (r, g, b) = (color.R / 255.0, color.G / 255.0, color.B / 255.0);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var hue = d == 0 ? 0
            : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * (((b - r) / d) + 2)
            : 60 * (((r - g) / d) + 4);
        return (hue < 0 ? hue + 360 : hue, max == 0 ? 0 : d / max, max);
    }

    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        var h = ((hue % 360) + 360) % 360 / 60;
        var s = Math.Clamp(saturation, 0, 1);
        var v = Math.Clamp(value, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs((h % 2) - 1));
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = v - c;
        return new Rgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Hue and saturation at a point on the wheel (x right, y down, both from the centre, in wheel radii);
    /// points beyond the rim give the rim's colour.</summary>
    public static (double Hue, double Saturation) At(double x, double y)
    {
        var hue = Math.Atan2(-y, x) * 180 / Math.PI;
        return (hue < 0 ? hue + 360 : hue, Math.Min(1, Math.Sqrt((x * x) + (y * y))));
    }

    /// <summary>Where a hue and saturation sit on the wheel (as <see cref="At"/> takes them).</summary>
    public static (double X, double Y) Where(double hue, double saturation)
    {
        var a = hue * Math.PI / 180;
        return (Math.Cos(a) * saturation, -Math.Sin(a) * saturation);
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
