using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>What a session hands the game, without a server: the build's name and the match file. A session against
/// a real server: the service's <c>net/tests/Integration.Tests/SessionTests.cs</c>.</summary>
public sealed class SessionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TheGameBuildNamesTheImageAndTheModule()
    {
        var recomp = Path.Combine(_dir, "recomp");
        Directory.CreateDirectory(Path.Combine(recomp, "fzgx"));
        var paths = RecompLaunch.Resolve(null, _dir, Path.Combine(_dir, "data"));
        Assert.Null(RecompLaunch.Build(paths));                        // nothing installed: no build to name
        File.WriteAllText(Path.Combine(recomp, "fzgx", "mod.bin"), "image");
        File.WriteAllText(paths.Module, "module");
        Assert.Null(RecompLaunch.Build(paths));                        // no game program yet
        File.WriteAllText(paths.Runner, "runner");
        var build = RecompLaunch.Build(paths)!;
        Assert.Equal(3, build.Split('/').Length);                      // image/module/runner
        Assert.All(build.Split('/'), h => Assert.Equal(40, h.Length));
        Assert.StartsWith("0e76292794888d4f1fa75fb3aff4ca27c58f56a6/", build, StringComparison.Ordinal);   // sha1("image")
        File.WriteAllText(paths.Module, "module2");
        var other = RecompLaunch.Build(paths)!;
        Assert.NotEqual(build, other);                                 // another module is another build
        File.WriteAllText(paths.Runner, "runner2");
        Assert.NotEqual(other, RecompLaunch.Build(paths));             // and so is another game program
        Assert.True(build.Length <= Hello.MaxBuildLength);
    }

    /// <summary>A player's side of a race: no test field is in a match file, whoever writes it (the game refuses a
    /// match that names one; the test settings are the game's launch file's "debug" part).</summary>
    [Fact]
    public void AMatchFileHasNoTestFields()
    {
        var start = new MatchStart("m1", new RelayEndpoint("r", "", 47811), "1f", 0,
            new RaceSetup(16, 1, [new SeatSetup(0, "p1", "ANA", 1, null)], 1, 2));
        var plain = MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Quieter));
        Assert.DoesNotContain("autopilot", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("testLagMs", plain, StringComparison.Ordinal);
        Assert.Contains("\"delay\": 2", plain, StringComparison.Ordinal);
        // the player's setting: this game's own delay, 0, 1 or 2 and never more
        Assert.Contains("\"delay\": 0", MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Quieter,
            new LauncherSettings { InputDelay = 0 }.RaceInputDelay)), StringComparison.Ordinal);
        Assert.Contains("\"delay\": 2", MatchFile.Build(start, new RaceOptions("127.0.0.1", OtherSounds.Quieter,
            new LauncherSettings { InputDelay = 3 }.RaceInputDelay)), StringComparison.Ordinal);
    }
}
