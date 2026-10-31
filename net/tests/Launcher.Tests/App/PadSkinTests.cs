using Launcher.App.Controls;
using SkiaSharp;

namespace Launcher.Tests;

/// <summary>The numbers of m-overlay's skin as the Controller page's pad draws from them (<see cref="PadSkin"/>).</summary>
public sealed class PadSkinTests
{
    [Fact]
    public void TheSdksClampIsWhatTheSkinsRangesAreMadeFor()
    {
        Assert.Equal((0, 0), PadSkin.ClampedStick(128, 128));
        Assert.Equal((0, 0), PadSkin.ClampedStick(128 + 15, 128 - 15));
        Assert.Equal((72, 0), PadSkin.ClampedStick(255, 128));
        Assert.Equal((0, -59), PadSkin.ClampedCStick(128, 0));
        Assert.Equal((0, 150), (PadSkin.ClampedTrigger(30), PadSkin.ClampedTrigger(255)));
        Assert.Equal(1f, PadSkin.Trigger(PadSkin.ClampedTrigger(255)));
    }

    [Fact]
    public void AStickAtRestStandsInItsGateAndAPushedOneLeansAway()
    {
        var rest = PadSkin.Quad(PadSkin.Stick, 0, 0);
        Assert.Equal(new SKPoint(22, 52), rest[0]);
        Assert.Equal(new SKPoint(150, 180), rest[2]);
        var (x, y) = PadSkin.Translate(0, 72, 72);
        var up = PadSkin.Quad(PadSkin.Stick, x, y);
        Assert.True(up[2].Y < rest[2].Y);
        Assert.True(up[1].X - up[0].X < up[2].X - up[3].X);
    }

    [Fact]
    public void ThePictureLandsOnTheQuadsCorners()
    {
        var (x, y) = PadSkin.Translate(40, -30, 59);
        var quad = PadSkin.Quad(PadSkin.CStick, x, y);
        var onto = PadSkin.PictureOnto(quad, 128, 128);
        SKPoint[] corners = [new(0, 0), new(128, 0), new(128, 128), new(0, 128)];
        for (var i = 0; i < 4; i++)
        {
            var at = onto.MapPoint(corners[i]);
            Assert.Equal(quad[i].X, at.X, 2);
            Assert.Equal(quad[i].Y, at.Y, 2);
        }
    }
}
