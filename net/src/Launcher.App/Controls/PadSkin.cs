using SkiaSharp;

namespace Launcher.App.Controls;

/// <summary>
/// The numbers of m-overlay's default skin (bkacjios/m-overlay, MIT: Assets/PadOverlay/LICENSE.txt;
/// source/overlay/skins/default.lua and source/modules/games/core.lua), in its own 512 by 256 units, and the SDK's
/// PADClamp that the game reads a pad through, which those numbers are made for. No drawing here: <see cref="PadOverlay"/>
/// draws from them. The runner draws the same skin (port/moderngekko/src/inputs_skin.h).
/// </summary>
public static class PadSkin
{
    public const float Width = 512, Height = 256, Cell = 128;

    /// <summary>Where the stick's or C-stick's gate stands, and how far its centre travels.</summary>
    public readonly record struct StickPlace(float GateX, float GateY, float CentreX, float CentreY, float Travel);

    public static readonly StickPlace Stick = new(22, 52, 64 + 22, 64 + 8, 44);
    public static readonly StickPlace CStick = new(48 + 128, 52, 64 + 48 + 128, 64 + 18, 32);

    /// <summary>A round button's picture: its name in the art's files, where it stands, its colour.</summary>
    public readonly record struct Round(string Button, string Art, float X, float Y, SKColor Colour);

    public static readonly IReadOnlyList<Round> Buttons =
    [
        new("A", "a", 12 + 64 + 256, 48, new SKColor(0, 225, 150)),
        new("B", "b", 16 + 256, 92, new SKColor(230, 0, 0)),
        new("X", "x", 138 + 256, 32, SKColors.White),
        new("Y", "y", 60 + 256, -16, SKColors.White),
        new("Z", "z", 128 + 256, -32, new SKColor(165, 75, 165)),
        new("Start", "start", 256, 26, SKColors.White),
    ];

    public static readonly SKColor CStickColour = new(255, 235, 0);

    /// <summary>What the game reads of a stick the helper says from 0 to 255 (128 at rest).</summary>
    public static (int X, int Y) ClampedStick(int x, int y) => Clamp(x - 128, y - 128, 72, 40, 15);

    public static (int X, int Y) ClampedCStick(int x, int y) => Clamp(x - 128, y - 128, 59, 31, 15);

    /// <summary>What the game reads of a trigger, 0 to 150.</summary>
    public static int ClampedTrigger(int t) => t <= 30 ? 0 : t >= 180 ? 150 : t - 30;

    private static (int X, int Y) Clamp(int rawX, int rawY, int max, int xy, int min)
    {
        var (signX, signY) = (Math.Sign(rawX), Math.Sign(rawY));
        var x = Math.Max(Math.Abs(rawX) - min, 0);
        var y = Math.Max(Math.Abs(rawY) - min, 0);
        var d = y <= x ? xy * x + (max - xy) * y : xy * y + (max - xy) * x;
        if (xy * max < d)
        {
            (x, y) = (xy * max * x / d, xy * max * y / d);
        }
        return (signX * x, signY * y);
    }

    /// <summary>A clamped stick into the skin's -1 to 1, amplified towards a diagonal (72 for the stick, 59 for the
    /// C-stick: <c>core.translateJoyStick</c>, <c>translateCStick</c>).</summary>
    public static (float X, float Y) Translate(int clampedX, int clampedY, float range)
    {
        var (x, y) = (clampedX / range, clampedY / range);
        var near = 1 - Math.Abs(Math.Abs(x) - Math.Abs(y));
        var angle = MathF.Atan2(x, y);
        var mag = MathF.Sqrt(x * x + y * y);
        return (x + MathF.Sin(angle) * mag * near * 0.25f, y + MathF.Cos(angle) * mag * near * 0.25f);
    }

    /// <summary>A clamped trigger into the skin's 0 to 1 (<c>core.translateTriggers</c>).</summary>
    public static float Trigger(int clamped) => Math.Min(1, clamped / 125f);

    /// <summary>The stick's cell, leaning away the further it is pushed: top left, top right, bottom right, bottom
    /// left, in the skin's units.</summary>
    public static SKPoint[] Quad(StickPlace place, float x, float y)
    {
        var angle = MathF.Atan2(x, y);
        var mag = MathF.Sqrt(x * x + y * y);
        var (far, near) = (mag * 12, mag * 20);
        ReadOnlySpan<SKPoint> corners = [new(far, near), new(Cell - far, near), new(Cell, Cell), new(0, Cell)];
        var (centreX, centreY) = (place.CentreX + place.Travel * x, place.CentreY + place.Travel * (1 - y));
        var (c, s) = (MathF.Cos(angle), MathF.Sin(angle));
        var quad = new SKPoint[4];
        for (var i = 0; i < 4; i++)
        {
            var (dx, dy) = (corners[i].X - 64, corners[i].Y - 64);
            quad[i] = new SKPoint(centreX + dx * c - dy * s, centreY + dx * s + dy * c);
        }
        return quad;
    }

    /// <summary>The perspective that takes a picture of the given size onto the quad, as m-overlay's perspective
    /// shader does (one projective map of the whole picture).</summary>
    public static SKMatrix PictureOnto(ReadOnlySpan<SKPoint> quad, float width, float height)
    {
        var (p0, p1, p2, p3) = (quad[0], quad[1], quad[2], quad[3]);
        var (sx, sy) = (p0.X - p1.X + p2.X - p3.X, p0.Y - p1.Y + p2.Y - p3.Y);
        var (dx1, dx2, dy1, dy2) = (p1.X - p2.X, p3.X - p2.X, p1.Y - p2.Y, p3.Y - p2.Y);
        var den = dx1 * dy2 - dx2 * dy1;
        var (g, h) = den == 0 ? (0f, 0f) : ((sx * dy2 - dx2 * sy) / den, (dx1 * sy - sx * dy1) / den);
        return new SKMatrix
        {
            ScaleX = (p1.X - p0.X + g * p1.X) / width,
            SkewX = (p3.X - p0.X + h * p3.X) / height,
            TransX = p0.X,
            SkewY = (p1.Y - p0.Y + g * p1.Y) / width,
            ScaleY = (p3.Y - p0.Y + h * p3.Y) / height,
            TransY = p0.Y,
            Persp0 = g / width,
            Persp1 = h / height,
            Persp2 = 1,
        };
    }
}
