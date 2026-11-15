using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.Tests;

/// <summary>
/// The pad turned into the window's intents (<see cref="PadIntents"/>), on a clock the test sets: a direction moves
/// once when pressed and repeats while held, slowly first and then faster on an eased curve, down to a floor; a button
/// acts on its press only; L and R also by their analog press (a pad's triggers), with a gap too; the main stick has a
/// gap between engaging and letting go; the C-stick scrolls by the time it is held, up and down and to the side; a
/// controller unplugged holds nothing.
/// </summary>
public sealed class PadIntentsTests
{
    private static HelperPad Pad(string[]? buttons = null, int x = 128, int y = 128, int cy = 128, bool connected = true,
        int cx = 128, int l = 0, int r = 0) =>
        new(0, 6, connected, new HashSet<string>(buttons ?? []), x, y, cx, cy, l, r);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void APressActsOnceAndAHeldButtonDoesNotRepeat()
    {
        var pad = new PadIntents();
        Assert.Equal([new PadPressed(PadButton.A)], pad.Read(Pad(["A"]), Ms(0)));
        Assert.Empty(pad.Read(Pad(["A"], x: 130), Ms(50)));          // still held, another reading
        Assert.Empty(pad.Tick(Ms(2000)));
        Assert.Empty(pad.Read(Pad(), Ms(2100)));                       // let go
        Assert.Equal([new PadPressed(PadButton.B), new PadPressed(PadButton.Start)], pad.Read(Pad(["B", "Start"]), Ms(2200)));
        Assert.Equal([new PadPressed(PadButton.L), new PadPressed(PadButton.R), new PadPressed(PadButton.Z)],
            pad.Read(Pad(["L", "R", "Z"]), Ms(2300)));
        // (X and Y are the redesigned interface's: the chat and a page's second action; the classic one does nothing with them)
        Assert.Equal([new PadPressed(PadButton.X), new PadPressed(PadButton.Y)], pad.Read(Pad(["X", "Y"]), Ms(2400)));
    }

    [Fact]
    public void TheRepeatWaitsThenEasesFasterToAFloor()
    {
        Assert.Equal(Ms(400), PadIntents.RepeatAfter(0));
        Assert.Equal(Ms(200), PadIntents.RepeatAfter(1));
        var waits = Enumerable.Range(1, 20).Select(PadIntents.RepeatAfter).ToList();
        for (var i = 1; i < waits.Count; i++)
        {
            Assert.True(waits[i] <= waits[i - 1], "a repeat never comes later than the one before");
        }
        Assert.True(waits[1] < waits[0] && waits[2] < waits[1], "it speeds up step by step, not at once");
        Assert.Equal(Ms(55), waits[^1]);
        Assert.Contains(waits, w => w > Ms(55) && w < Ms(200));      // the steps between: an ease, not a jump
    }

    [Fact]
    public void AHeldDirectionRepeatsOnTheCurve()
    {
        var pad = new PadIntents();
        Assert.Equal([new PadMoved(PadMove.Down, Repeat: false)], pad.Read(Pad(["Down"]), Ms(0)));
        Assert.True(pad.Holding);
        Assert.Empty(pad.Tick(Ms(399)));
        Assert.Equal([new PadMoved(PadMove.Down, Repeat: true)], pad.Tick(Ms(400)));
        Assert.Empty(pad.Tick(Ms(599)));
        Assert.Single(pad.Tick(Ms(600)));                              // 400 + 200
        Assert.Single(pad.Tick(Ms(764)));                              // + 164
        // then 134.5, 110.3, 90.4, 74.2, 60.8 and 55 ms from there on: due by 1.6 s, eleven more
        var all = pad.Tick(Ms(1600));
        Assert.All(all, i => Assert.Equal(new PadMoved(PadMove.Down, true), i));
        Assert.Equal(11, all.Count);
        Assert.Empty(pad.Read(Pad(), Ms(1610)));
        Assert.False(pad.Holding);
        Assert.Empty(pad.Tick(Ms(5000)));
    }

    [Fact]
    public void TheStickEngagesFarAndLetsGoNear()
    {
        Assert.Null(PadIntents.DirectionOf(Pad(x: 128 + 40)));
        Assert.Equal(PadMove.Right, PadIntents.DirectionOf(Pad(x: 128 + 48)));
        Assert.Equal(PadMove.Right, PadIntents.DirectionOf(Pad(x: 128 + 40), held: PadMove.Right));
        Assert.Null(PadIntents.DirectionOf(Pad(x: 128 + 30), held: PadMove.Right));
        Assert.Equal(PadMove.Up, PadIntents.DirectionOf(Pad(y: 255)));       // the GameCube's stick reads higher up
        Assert.Equal(PadMove.Down, PadIntents.DirectionOf(Pad(y: 0)));
        Assert.Equal(PadMove.Left, PadIntents.DirectionOf(Pad(x: 0, y: 100)));  // the larger axis
        Assert.Equal(PadMove.Up, PadIntents.DirectionOf(Pad(["Up"], x: 255)));  // the D-pad first

        var pad = new PadIntents();
        Assert.Single(pad.Read(Pad(x: 200), Ms(0)));
        Assert.Empty(pad.Read(Pad(x: 170), Ms(10)));                  // within the gap: still held, no new press
        Assert.Empty(pad.Read(Pad(x: 150), Ms(20)));                  // let go
        Assert.Single(pad.Read(Pad(x: 200), Ms(30)));                 // a new press
    }

    [Fact]
    public void TheCStickScrollsByTheTimeHeld()
    {
        var pad = new PadIntents();
        pad.Read(Pad(cy: 128 - 100), Ms(0));                           // down, at its gate
        Assert.True(pad.Holding);
        var scrolled = Assert.IsType<PadScrolled>(Assert.Single(pad.Tick(Ms(100))));
        Assert.Equal(PadIntents.ScrollSpeed * 0.1, scrolled.By, 3);
        pad.Read(Pad(cy: 228), Ms(100));                               // up
        Assert.True(Assert.IsType<PadScrolled>(Assert.Single(pad.Tick(Ms(150)))).By < 0);
        pad.Read(Pad(cy: 140), Ms(150));                               // within its dead zone
        Assert.False(pad.Holding);
        Assert.Empty(pad.Tick(Ms(400)));
    }

    [Fact]
    public void ATriggerPressedDeepIsLOrROnce()
    {
        var pad = new PadIntents();
        Assert.Equal([new PadPressed(PadButton.R)], pad.Read(Pad(r: 200), Ms(0)));
        Assert.Empty(pad.Read(Pad(r: 140), Ms(10)));                  // within the gap: still held
        Assert.Empty(pad.Read(Pad(r: 60), Ms(20)));                   // let go
        Assert.Empty(pad.Read(Pad(r: 150), Ms(30)));                  // not deep enough for a new press
        Assert.Equal([new PadPressed(PadButton.L)], pad.Read(Pad(["L"], l: 255), Ms(40)));   // the click and the press: one
    }

    [Fact]
    public void TheCStickScrollsToTheSide()
    {
        var pad = new PadIntents();
        pad.Read(Pad(cx: 228), Ms(0));                                 // right, at its gate
        Assert.True(pad.Holding);
        var scrolled = Assert.IsType<PadScrolled>(Assert.Single(pad.Tick(Ms(100))));
        Assert.Equal(0, scrolled.By);
        Assert.Equal(PadIntents.ScrollSpeed * 0.1, scrolled.Across, 3);
    }

    [Fact]
    public void AControllerUnpluggedHoldsNothing()
    {
        var pad = new PadIntents();
        pad.Read(Pad(["Left"]), Ms(0));
        Assert.Empty(pad.Read(Pad(["Left"], connected: false), Ms(10)));
        Assert.False(pad.Holding);
        Assert.Empty(pad.Tick(Ms(1000)));
        Assert.Single(pad.Read(Pad(["Left"]), Ms(1100)));              // plugged in again: a new press
        pad.Reset();
        Assert.False(pad.Holding);
    }
}
