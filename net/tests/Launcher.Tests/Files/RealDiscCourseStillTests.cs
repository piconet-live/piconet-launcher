using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The Practice page's course pictures against the game itself, on your own disc image where this computer has
/// it (skipped where it does not): every course the page offers has its course-select model on the disc, and its
/// still and its hero hold the whole model (<see cref="CourseShot"/>). Nothing is taken from the disc into the
/// repository.
/// </summary>
public sealed class RealDiscCourseStillTests
{
    public static TheoryData<int> Offered() => [.. CourseCups.All.Select(c => c.Course)];

    [Theory]
    [MemberData(nameof(Offered))]
    public void EveryCourseOfThePageHasAModelThatItsPicturesHold(int course)
    {
        var path = RealDiscStatsTests.PlayersDisc();
        Assert.SkipWhen(path is null || !File.Exists(path), "no disc image on this computer: set DISC_DEFAULT to your own F-Zero GX (USA) ISO in ~/.config/piconet/pins.tsv");
        using var art = DiscArt.Open(path);
        Assert.NotNull(art);
        var model = art.Course(course);
        Assert.NotNull(model);
        Assert.NotEmpty(model.Indices);
        Assert.Single(art.CourseFiles([course]));

        var still = CourseShot.StillViewProjection(model, CourseShot.Still(model, 320, 144), 320, 144);
        var hero = CourseShot.Hero(model, 900, 232);
        foreach (var p in model.Positions)
        {
            var (at, depth) = StudioCamera.Project(p, still, 320, 144);
            Assert.True(depth > 0 && at.X >= 0 && at.X <= 320 && at.Y >= 0 && at.Y <= 144, $"course {course}: the still cuts {p}");
            for (var s = 0.0; s < CourseShot.TurnSeconds; s += 2.5)
            {
                (at, depth) = StudioCamera.Project(p, CourseShot.HeroViewProjection(model, hero, s, 900, 232), 900, 232);
                Assert.True(depth > 0 && at.X >= 0 && at.X <= 900 && at.Y >= 0 && at.Y <= 232, $"course {course}: the hero cuts {p} at {s} s");
            }
        }
    }
}
