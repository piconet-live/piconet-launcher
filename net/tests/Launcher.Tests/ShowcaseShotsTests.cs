using System.Numerics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The redesigned hub's showcase (<see cref="ShowcaseShots"/>): its shots one after another, round and round,
/// each a move of its own, the picture dissolving only between two.</summary>
public sealed class ShowcaseShotsTests
{
    [Fact]
    public void TheShotsFollowOneAnotherAndBeginAgain()
    {
        Assert.True(ShowcaseShots.Shots.Count >= 5);
        Assert.Equal(ShowcaseShots.Shots.Sum(s => s.Seconds), ShowcaseShots.Length);
        // the middle of each shot: the whole picture, between its own two ends
        var start = 0f;
        foreach (var shot in ShowcaseShots.Shots)
        {
            var middle = ShowcaseShots.At(start + (shot.Seconds / 2));
            Assert.Equal(1, middle.Shown, 3);
            Assert.InRange(middle.Zoom, Math.Min(shot.From.Zoom, shot.To.Zoom), Math.Max(shot.From.Zoom, shot.To.Zoom));
            // at a cut the picture is gone, and the next shot begins where it says
            Assert.Equal(0, ShowcaseShots.At(start).Shown, 3);
            Assert.Equal(shot.From.Yaw, ShowcaseShots.At(start).Yaw, 3);
            start += shot.Seconds;
        }
        Assert.Equal(ShowcaseShots.At(1.5f), ShowcaseShots.At(1.5f + ShowcaseShots.Length));
        Assert.Equal(ShowcaseShots.At(ShowcaseShots.Length - 1), ShowcaseShots.At(-1));
    }

    [Fact]
    public void EachShotMovesAndTheCameraLooksAtTheMachineFromOutsideIt()
    {
        foreach (var shot in ShowcaseShots.Shots)
        {
            Assert.NotEqual(shot.From, shot.To);
            foreach (var camera in new[] { shot.From, shot.To })
            {
                Assert.InRange(camera.Zoom, 1.5f, 4);   // close in, never so close that the camera stands in the machine
                Assert.InRange(camera.Pitch, 0, 1);         // never from under it
                Assert.True(Vector3.Abs(camera.Focus).X <= 1 && Vector3.Abs(camera.Focus).Y <= 1 && Vector3.Abs(camera.Focus).Z <= 1);
            }
        }
        // in close on its parts, and drawing back
        Assert.Contains(ShowcaseShots.Shots, s => s.To.Zoom >= 3 && s.To.Focus.Z < -0.5f);   // the nose
        Assert.Contains(ShowcaseShots.Shots, s => s.To.Zoom >= 3 && s.To.Focus.Z > 0.5f);    // the boosters
        Assert.Contains(ShowcaseShots.Shots, s => s.From.Pitch > 0.7f);                         // from above
        Assert.Contains(ShowcaseShots.Shots, s => s.To.Zoom < s.From.Zoom - 1);
        // a move goes on through the dissolve: a little way into a shot the camera has already moved
        var first = ShowcaseShots.Shots[0];
        Assert.NotEqual(first.From.Yaw, ShowcaseShots.At(0.3f).Yaw, 3);
        // Reduced motion: the first shot's middle, whole
        Assert.Equal(1, ShowcaseShots.Still.Shown, 3);
        // the engines boost in the shot from behind, from a third of the way in; at rest everywhere else
        var start = 0f;
        foreach (var shot in ShowcaseShots.Shots)
        {
            Assert.Equal(0, ShowcaseShots.At(start + (shot.Seconds * 0.2f)).Boost, 3);
            Assert.Equal(shot.Boost, ShowcaseShots.At(start + (shot.Seconds * 0.7f)).Boost, 3);
            start += shot.Seconds;
        }
        Assert.Contains(ShowcaseShots.Shots, s => s.Boost > 0 && s.To.Focus.Z > 0.5f);
    }

    /// <summary>Slow motion: no shot turns the camera faster than <see cref="ShowcaseShots.SlowestTurn"/> on average,
    /// none runs along the machine (a tracking shot alongside it looked as if it were dragged, not driven), and a
    /// second of a shot moves the camera only a little anywhere in it.</summary>
    [Fact]
    public void TheShotsMoveInSlowMotionAndNoneRunsAlongTheMachine()
    {
        foreach (var shot in ShowcaseShots.Shots)
        {
            Assert.InRange(MathF.Abs(shot.To.Yaw - shot.From.Yaw) / shot.Seconds, 0.001f, ShowcaseShots.SlowestTurn);
            Assert.InRange(Vector3.Distance(shot.From.Focus, shot.To.Focus), 0, 0.5f);
            Assert.InRange(MathF.Abs(shot.To.Focus.Z - shot.From.Focus.Z), 0, 0.4f);
            Assert.InRange(shot.Seconds, 12, 20);
        }
        Assert.DoesNotContain(ShowcaseShots.Shots, s => s.Name.Contains("alongside", StringComparison.Ordinal));
        var start = 0f;
        foreach (var shot in ShowcaseShots.Shots)
        {
            for (var t = 0f; t + 0.5f < shot.Seconds; t += 0.5f)
            {
                Assert.InRange(MathF.Abs(ShowcaseShots.At(start + t + 0.5f).Yaw - ShowcaseShots.At(start + t).Yaw), 0, 0.02f);
            }
            start += shot.Seconds;
        }
    }
}
