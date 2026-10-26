using Avalonia;
using Launcher.App.Controls;

namespace Launcher.Tests;

/// <summary>The builder's part labels never stand under the machine's name (in a small window
/// the name ran over the cockpit's label): every label's room starts below the name's band, inside the view, and no
/// two labels share room, at the stage's sizes from the least to a large window.</summary>
public sealed class PartCalloutsTests
{
    [Theory]
    [InlineData(584, 504, 118)]     // 1280 x 720
    [InlineData(584, 584, 118)]     // 1280 x 800
    [InlineData(440, 420, 118)]     // 1280 x 800 at 130%
    [InlineData(1224, 864, 118)]    // 1920 x 1080
    [InlineData(500, 270, 118)]     // the stage at its least
    public void TheLabelsStandUnderTheNameAndApart(double width, double height, double clear)
    {
        var places = PartCallouts.Places(new Size(width, height), clear);
        var rooms = places.Select(p => new Rect(p, PartCallouts.LabelSize)).ToList();
        Assert.All(rooms, r => Assert.True(r.Top >= clear, $"a label at {r} reaches into the name's band (to {clear})"));
        Assert.All(rooms, r => Assert.True(r.Bottom <= height && r.Left >= 0, $"a label at {r} leaves the view {width} x {height}"));
        for (var a = 0; a < rooms.Count; a++)
        {
            for (var b = a + 1; b < rooms.Count; b++)
            {
                Assert.False(rooms[a].Intersects(rooms[b]), $"labels {a} and {b} share room: {rooms[a]}, {rooms[b]}");
            }
        }
    }
}
