using System.Numerics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The track map's camera: following a racer, and the eased change from and to the whole course.</summary>
public sealed class MapCameraTests
{
    /// <summary>The map keeps where each dot glides from by seat, on the window's thread: racers that share a seat
    /// (refused before they reach the map, <see cref="ServerMessages.Sound(Online.Protocol.RaceSnapshot?)"/>) are
    /// one dot's place there, not an error.</summary>
    [Fact]
    public void TheMapsDotsBySeatTakeASeatGivenTwice()
    {
        var places = MapGlide.BySeat([(4, new Vector3(1, 2, 3)), (9, new Vector3(7, 8, 9)), (4, new Vector3(4, 5, 6))]);
        Assert.Equal(2, places.Count);
        Assert.Equal(new Vector3(4, 5, 6), places[4]);
        Assert.Equal(new Vector3(7, 8, 9), places[9]);
        Assert.Empty(MapGlide.BySeat([]));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0.6, -0.8)]
    public void AFollowedRacersRoadAheadIsUpTheScreen(float x, float z)
    {
        var at = new Vector3(-1201, 40, 388);
        var heading = new Vector3(x, 0, z);
        var view = MapCamera.Following(at, MapCamera.YawAlong(heading)!.Value);

        var here = Vector3.Transform(at, view.Matrix);
        var ahead = Vector3.Transform(at + heading * 100, view.Matrix);
        // straight up the screen: no sideways, higher on screen and farther from the camera
        Assert.Equal(here.X, ahead.X, 0.01f);
        Assert.True(ahead.Y > here.Y);
        Assert.True(ahead.Z < here.Z);
        // the machine below the middle, so that more road ahead shows
        Assert.True(here.Y < 0);
    }

    [Fact]
    public void ANearlyVerticalMoveHasNoHeading()
    {
        Assert.Null(MapCamera.YawAlong(new Vector3(0.1f, 10, 0)));
        Assert.Null(MapCamera.YawAlong(Vector3.Zero));
        Assert.NotNull(MapCamera.YawAlong(new Vector3(5, 3, 0)));
    }

    [Fact]
    public void TheChangeStartsAndEndsOnEachViewAndZoomsEvenly()
    {
        var whole = new MapView(new Vector3(0, 0, 0), 2400, 0.2f, 0.7f);
        var close = new MapView(new Vector3(500, 20, -300), 240, -0.3f, 0.9f);

        Assert.Equal(whole, MapCamera.Blend(whole, close, 0));
        Assert.Equal(close, MapCamera.Blend(whole, close, 1));
        Assert.Equal(close, MapCamera.Blend(whole, close, 3));
        // half way through, half the zoom by ratio: 2400 to 240 passes 759, not 1320
        Assert.Equal(MathF.Sqrt(2400 * 240), MapCamera.Blend(whole, close, 0.5).Radius, 1f);
        // eased: slow at both ends, never past either view
        Assert.True(MapCamera.Ease(0.1) < 0.1 && MapCamera.Ease(0.9) > 0.9);
        double last = 0;
        for (var t = 0.0; t <= 1; t += 0.05)
        {
            var e = MapCamera.Ease(t);
            Assert.InRange(e, last, 1);
            last = e;
        }
    }

    [Fact]
    public void TheViewTurnsTheShortWayRound()
    {
        var a = new MapView(Vector3.Zero, 100, 3.0f, 0.7f);
        var b = new MapView(Vector3.Zero, 100, -3.0f, 0.7f);
        var mid = MapCamera.Blend(a, b, 0.5).Yaw;
        // across pi (0.28 radians), not back through zero (6 radians)
        Assert.Equal(MathF.PI, MapCamera.Wrap(mid) < 0 ? mid + 2 * MathF.PI : mid, 0.01f);

        // the whole course's yaw keeps growing as it turns: many turns on, still the short way
        var turned = MapCamera.Approach(0.1f + 20 * MathF.PI, 0, 1e6, 300);
        Assert.Equal(0, MapCamera.Wrap(turned), 0.001f);
        var step = MapCamera.Approach(3.0f, -3.0f, 16, 300);
        Assert.True(step > 3.0f);
    }

    [Fact]
    public void TheHeadingFollowsTheMovesAndIgnoresJumps()
    {
        var heading = new HeadingTracker();
        heading.Add(new Vector3(0, 0, 0));
        Assert.Null(heading.Yaw);
        heading.Add(new Vector3(3, 0, 0));   // too small a move yet
        Assert.Null(heading.Yaw);
        heading.Add(new Vector3(10, 0, 0));
        Assert.Equal(MapCamera.YawAlong(Vector3.UnitX), heading.Yaw);

        // respawned far away: no turn from the jump itself
        heading.Add(new Vector3(10, 0, 900));
        Assert.Equal(MapCamera.YawAlong(Vector3.UnitX), heading.Yaw);
        heading.Add(new Vector3(10, 0, 880));
        Assert.Equal(MapCamera.YawAlong(-Vector3.UnitZ), heading.Yaw);

        heading.Reset();
        Assert.Null(heading.Yaw);
    }

    private const double FrameMs = 1000.0 / 60;

    /// <summary>A machine at about 1200 km/h: its place each frame.</summary>
    private const float Pace = 5.5f;

    /// <summary>The view turning after a followed racer, as the map does: the most it turned in one frame once settled,
    /// the most its heading strayed from the way the path really goes.</summary>
    private static (float MostPerFrame, float MostOff) Turning(IEnumerable<Vector3> path, Func<int, float> way)
    {
        const int settled = 200;
        var heading = new HeadingTracker();
        float? yaw = null;
        float perFrame = 0, off = 0;
        var i = 0;
        foreach (var at in path)
        {
            heading.Add(at);
            if (heading.Yaw is { } target)
            {
                var next = MapCamera.Approach(yaw ?? target, target, FrameMs, MapCamera.TurnMs);
                if (i > settled)
                {
                    perFrame = MathF.Max(perFrame, MathF.Abs(MapCamera.Wrap(next - (yaw ?? next))));
                    off = MathF.Max(off, MathF.Abs(MapCamera.Wrap(way(i) - next)));
                }
                yaw = next;
            }
            i++;
        }
        return (perFrame * 180 / MathF.PI, off * 180 / MathF.PI);
    }

    private static readonly float Straight = MapCamera.YawAlong(-Vector3.UnitZ)!.Value;

    /// <summary>Along a straight road with a burst of 15 units across it each way every few steps: the view stands
    /// nearly still. (With a heading from each move of 8 units and the old 350 ms, this turned 3.4 degrees a frame.)</summary>
    [Fact]
    public void AWeaveOnAStraightBarelyTurnsTheView()
    {
        var weave = Enumerable.Range(0, 600).Select(i => new Vector3((i / 4 % 2 == 0 ? -15 : 15), 0, -Pace * i));
        var (perFrame, off) = Turning(weave, _ => Straight);
        Assert.True(perFrame < 1.2f, $"{perFrame} degrees in a frame");
        Assert.True(off < 8f, $"{off} degrees off the road");
    }

    /// <summary>Places that step back and forth along the road (two reports of one race, one older): the view does not
    /// swing round. (A heading from each move turned 7.3 degrees a frame at the old 350 ms.)</summary>
    [Fact]
    public void PlacesSteppingBackAndForthDoNotTurnTheView()
    {
        var path = Enumerable.Range(0, 600).Select(i => new Vector3(0, 0, -Pace * i + (i % 12 >= 6 ? 30 : 0)));
        var (perFrame, off) = Turning(path, _ => Straight);
        Assert.True(perFrame < 0.1f, $"{perFrame} degrees in a frame");
        Assert.True(off < 0.1f, $"{off} degrees off the road");
    }

    /// <summary>A real bend is followed, within a set lag: half the chord's angle and the view's own turn.</summary>
    [Theory]
    [InlineData(150, 60)]
    [InlineData(300, 30)]
    [InlineData(600, 15)]
    public void ABendIsFollowedWithinALag(float radius, float most)
    {
        float Angle(int i) => Pace * i / radius;
        var bend = Enumerable.Range(0, 600).Select(i => new Vector3(radius * MathF.Sin(Angle(i)), 0, radius - (radius * MathF.Cos(Angle(i)))));
        var (_, off) = Turning(bend, i => MapCamera.YawAlong(new Vector3(MathF.Cos(Angle(i)), 0, MathF.Sin(Angle(i))))!.Value);
        Assert.True(off < most, $"{off} degrees behind the bend of radius {radius}");
    }

    /// <summary>A respawn: the heading holds through the jump and the places before it no longer count.</summary>
    [Fact]
    public void AJumpForgetsThePlacesBeforeIt()
    {
        var heading = new HeadingTracker();
        for (var i = 0; i < 40; i++)
        {
            heading.Add(new Vector3(10 * i, 0, 0));
        }
        var before = heading.Yaw;
        Assert.Equal(MapCamera.YawAlong(Vector3.UnitX), before);
        heading.Add(new Vector3(0, 0, 2000));
        Assert.Equal(before, heading.Yaw);
        heading.Add(new Vector3(0, 0, 1990));
        Assert.Equal(MapCamera.YawAlong(-Vector3.UnitZ), heading.Yaw);   // not from before the jump
    }

    /// <summary>Straight up a wall: the last heading stands.</summary>
    [Fact]
    public void AClimbTooSteepKeepsTheHeading()
    {
        var heading = new HeadingTracker();
        for (var i = 0; i < 40; i++)
        {
            heading.Add(new Vector3(10 * i, 0, 0));
        }
        var before = heading.Yaw;
        for (var i = 1; i < 80; i++)
        {
            heading.Add(new Vector3(390, 10 * i, 0));
        }
        Assert.Equal(before, heading.Yaw);
    }
}
