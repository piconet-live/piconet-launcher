using System.Numerics;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The Practice page's pictures of a course's model (<see cref="CourseShot"/>): the whole model inside the
/// picture with its margin, the tile's still from its one angle and the hero at every angle of its turn; and where the
/// stills are kept (<see cref="CourseStillCache"/>).</summary>
public sealed class CourseShotTests
{
    /// <summary>A made-up course: a long, flat loop with a tall tower at one end, off the world's middle.</summary>
    private static CourseModel Course()
    {
        var positions = new List<Vector3>();
        for (var i = 0; i < 64; i++)
        {
            var a = i * 2 * MathF.PI / 64;
            positions.Add(new Vector3(3000 + (2400 * MathF.Cos(a)), 40 * MathF.Sin(3 * a), -800 + (900 * MathF.Sin(a))));
        }
        positions.Add(new Vector3(5400, 700, -800));
        var indices = Enumerable.Range(0, 63).SelectMany(i => new[] { i, i + 1, 64 }).ToArray();
        return new CourseModel([.. positions], [.. positions.Select(_ => Vector3.UnitY)], indices);
    }

    /// <summary>Every vertex's place on a picture of the size, and its depth in front of the camera.</summary>
    private static IEnumerable<(Vector2 At, float Depth)> Projected(CourseModel model, Matrix4x4 viewProjection, int width, int height) =>
        model.Positions.Select(p => StudioCamera.Project(p, viewProjection, width, height));

    private static void InsideTheMargin(CourseModel model, Matrix4x4 viewProjection, int width, int height, string what)
    {
        // the margin is a share of each half of the picture: kept on every side
        var (mx, my) = (width / 2f * CourseShot.Margin, height / 2f * CourseShot.Margin);
        var shorter = Math.Min(width, height) / 2f * CourseShot.Margin;
        foreach (var (at, depth) in Projected(model, viewProjection, width, height))
        {
            Assert.True(depth > 0, $"{what}: a vertex is behind the camera");
            Assert.InRange(at.X, -0.5f, width + 0.5f);
            Assert.InRange(at.Y, -0.5f, height + 0.5f);
        }
        var all = Projected(model, viewProjection, width, height).Select(p => p.At).ToList();
        var (low, high) = (all.Aggregate(Vector2.Min), all.Aggregate(Vector2.Max));
        // inside the margin, and framed: filling the picture across or up to the margin (not small in its middle)
        Assert.True(low.X >= mx - 0.5f && low.Y >= my - 0.5f && high.X <= width - mx + 0.5f && high.Y <= height - my + 0.5f,
            $"{what}: the model reaches {low} to {high} on {width}x{height}");
        Assert.True(low.X <= mx + 1 || low.Y <= my + 1 || high.X >= width - mx - 1 || high.Y >= height - my - 1,
            $"{what}: the model reaches {low} to {high} on {width}x{height}, short of the margin {shorter} all round");
    }

    [Theory]
    [InlineData(320, 144)]
    [InlineData(144, 320)]
    [InlineData(200, 200)]
    public void ATilesStillHoldsTheWholeModelCentred(int width, int height)
    {
        var model = Course();
        var frame = CourseShot.Still(model, width, height);
        var viewProjection = CourseShot.StillViewProjection(model, frame, width, height);
        InsideTheMargin(model, viewProjection, width, height, "the still");

        // centred: as much room on the left as on the right, and above as below (within a pixel or two)
        var all = Projected(model, viewProjection, width, height).Select(p => p.At).ToList();
        var (low, high) = (all.Aggregate(Vector2.Min), all.Aggregate(Vector2.Max));
        var tight = Math.Abs(low.X - (width - high.X)) < 2 || Math.Abs(low.Y - (height - high.Y)) < 2;
        Assert.True(tight, $"the still is not centred on the side it fills: {low} to {high} on {width}x{height}");
    }

    [Theory]
    [InlineData(900, 232)]
    [InlineData(1800, 464)]
    [InlineData(400, 300)]
    public void TheHeroHoldsTheWholeModelAtEveryAngleOfItsTurnAtOneDistance(int width, int height)
    {
        var model = Course();
        var frame = CourseShot.Hero(model, width, height);
        Assert.Equal(model.Center, frame.Target);
        var filled = false;
        for (var s = 0.0; s < CourseShot.TurnSeconds; s += 0.37)
        {
            var viewProjection = CourseShot.HeroViewProjection(model, frame, s, width, height);
            foreach (var (at, depth) in Projected(model, viewProjection, width, height))
            {
                Assert.True(depth > 0);
                Assert.InRange(at.X, width / 2f * CourseShot.Margin * 0.98f, width - (width / 2f * CourseShot.Margin * 0.98f));
                Assert.InRange(at.Y, height / 2f * CourseShot.Margin * 0.98f, height - (height / 2f * CourseShot.Margin * 0.98f));
            }
            // framed: at some angle of the turn the model reaches the margin (no farther back than it must be)
            var all = Projected(model, viewProjection, width, height).Select(p => p.At).ToList();
            var (low, high) = (all.Aggregate(Vector2.Min), all.Aggregate(Vector2.Max));
            var (mx, my) = (width / 2f * CourseShot.Margin, height / 2f * CourseShot.Margin);
            filled |= low.X < mx + 3 || high.X > width - mx - 3 || low.Y < my + 3 || high.Y > height - my - 3;
        }
        Assert.True(filled, "the hero never reaches the margin: it stands farther back than it must");
    }

    /// <summary>The hero frames the model right of the title plate (at a larger interface
    /// scale the plate hid it): at every angle of the turn, nothing of the model lands on the plate, whatever the
    /// hero's width, the plate at its most share of the width or a little more.</summary>
    [Theory]
    [InlineData(600, 0.40)]
    [InlineData(600, 0.47)]
    [InlineData(900, 0.40)]
    [InlineData(1300, 0.40)]
    [InlineData(1300, 0.25)]
    public void TheHerosModelStaysClearOfTheTitlePlate(int width, double plate)
    {
        const int height = 232;
        var model = Course();
        var covered = (int)Math.Round(width * plate);
        var (x, areaWidth) = CourseShot.HeroArea(width, covered);
        Assert.True(x > covered, "the model's area starts right of the plate");
        Assert.Equal(width, x + areaWidth);
        var frame = CourseShot.Hero(model, areaWidth, height);
        for (var s = 0.0; s < CourseShot.TurnSeconds; s += 0.37)
        {
            var viewProjection = CourseShot.HeroViewProjection(model, frame, s, areaWidth, height);
            foreach (var (at, depth) in Projected(model, viewProjection, areaWidth, height))
            {
                Assert.True(depth > 0);
                Assert.InRange(x + at.X, covered + 1, width);
                Assert.InRange(at.Y, 0, height);
            }
        }
    }

    [Fact]
    public void ANarrowHerosPlateKeepsItsNameToItsShare()
    {
        // the name's room: 40% of the hero less what stands round the name, and never too small to read
        Assert.Equal(600 * CourseShot.PlateShare - 96, CourseShot.NameRoom(600, 96), 6);
        Assert.Equal(1300 * CourseShot.PlateShare - 128, CourseShot.NameRoom(1300, 128), 6);
        Assert.Equal(96, CourseShot.NameRoom(200, 128));
        // a plate taking almost all of a hero still leaves the model a quarter of it
        Assert.Equal((450, 150), CourseShot.HeroArea(600, 590));
    }

    [Fact]
    public void TheHeroTurnsOnceInFortySeconds()
    {
        Assert.Equal(0, CourseShot.HeroYaw(0), 5);
        Assert.Equal(MathF.PI, CourseShot.HeroYaw(20), 4);
        Assert.Equal(CourseShot.HeroYaw(3), CourseShot.HeroYaw(43), 4);
    }

    [Fact]
    public void AStillIsKeptUnderTheDataFolderByDiscAndCourse()
    {
        var files = new[] { ("stage/selst01.gma.lz", new byte[] { 1, 2, 3 }), ("stage/selst03.gma.lz", new byte[] { 4, 5 }) };
        var key = CourseStillCache.DiscKey(files, 320, 144);
        Assert.Matches("^[0-9a-f]{16}$", key);
        var folder = CourseStillCache.Folder(Path.Combine("data"), key);
        Assert.Equal(Path.Combine("data", "cache", "courses", key), folder);
        Assert.Equal(Path.Combine("data", "cache", "courses", key, "05.png"), CourseStillCache.File(folder, 5));
        Assert.Equal(Path.Combine("data", "cache", "courses", key, "36.png"), CourseStillCache.File(folder, 36));

        // the same files in any order: the same key
        Assert.Equal(key, CourseStillCache.DiscKey(files.Reverse(), 320, 144));
        // another file's byte, another file, another size: another key (another look of the stills, too: Recipe)
        Assert.NotEqual(key, CourseStillCache.DiscKey([files[0], ("stage/selst03.gma.lz", new byte[] { 4, 6 })], 320, 144));
        Assert.NotEqual(key, CourseStillCache.DiscKey([files[0]], 320, 144));
        Assert.NotEqual(key, CourseStillCache.DiscKey(files, 640, 288));
        // a name and its bytes cannot be shifted into the next one's
        Assert.NotEqual(CourseStillCache.DiscKey([("a", new byte[] { 0x62 })], 1, 1), CourseStillCache.DiscKey([("ab", Array.Empty<byte>())], 1, 1));
    }

    [Fact]
    public void AKeptStillIsReadBackWholeAndAMissingOneIsNone()
    {
        var data = Directory.CreateTempSubdirectory("launcher-tests").FullName;
        try
        {
            var file = CourseStillCache.File(CourseStillCache.Folder(data, "0123456789abcdef"), 7);
            Assert.Null(CourseStillCache.Read(file));
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
            CourseStillCache.Keep(file, png);
            Assert.Equal(png, CourseStillCache.Read(file));
            Assert.False(File.Exists(file + Online.Protocol.AtomicFile.Suffix));
            // an empty file is no still: drawn again
            File.WriteAllBytes(file, []);
            Assert.Null(CourseStillCache.Read(file));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void AVenuesSkyIsNightAboveAndTheModelLitInItsHue()
    {
        // a picture: orange sky, red middle, blue foot
        var (w, h) = (6, 9);
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var (r, g, b) = y < 3 ? (240, 140, 20) : y < 6 ? (220, 30, 30) : (20, 40, 200);
            for (var x = 0; x < w; x++)
            {
                (rgba[((y * w) + x) * 4], rgba[(((y * w) + x) * 4) + 1], rgba[(((y * w) + x) * 4) + 2], rgba[(((y * w) + x) * 4) + 3]) = ((byte)r, (byte)g, (byte)b, 255);
            }
        }
        var sky = VenueSky.From(new Picture(w, h, rgba));
        Assert.True(sky.Top.X > sky.Top.Z, "the sky's top is the picture's orange, darkened");
        Assert.True(sky.Top.Length() < sky.Horizon.Length() && sky.Horizon.Length() < sky.Glow.Length());
        Assert.True(sky.Glow.X > sky.Glow.Y && sky.Glow.X > sky.Glow.Z, "the glow is the middle's red");
        Assert.True(sky.Ground.Z > sky.Ground.X, "the ground is the foot's blue");
        Assert.True(sky.Lit.Length() > sky.Dark.Length());
        Assert.Equal(VenueSky.Plain, VenueSky.From(null));

        // grey: grey tones
        var grey = VenueSky.From(new Picture(1, 3, [.. Enumerable.Repeat((byte)128, 12)]));
        Assert.Equal(grey.Lit.X, grey.Lit.Z, 4);
    }
}
