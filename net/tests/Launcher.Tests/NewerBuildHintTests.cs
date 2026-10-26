using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The server answers after which the launcher looks for an update of itself
/// (<see cref="QuickPlaySession.NewerBuildHint"/>): a hello refused for its version, and Quick Play ended by a
/// retired coordinator ("a new build is out"); never a full service, too many joins, or any other refusal.</summary>
public sealed class NewerBuildHintTests
{
    [Fact]
    public void OnlyAnAnswerThatMayMeanANewerLauncherIsOne()
    {
        Assert.True(QuickPlaySession.NewerBuildHint(new ErrorMessage("bad_version", "this server speaks version 4")));
        Assert.True(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("", "a new build is out", "A new build is out.")));
        Assert.True(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("", "0.5.1 is out", "Get 0.5.1 from piconet.live.")));

        Assert.False(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("m1", "at_capacity")));
        Assert.False(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("m1", "at_capacity", "Servers at capacity.")));
        Assert.False(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("", "too many joins", "Too many joins from your address. Try again in a minute.")));
        Assert.False(QuickPlaySession.NewerBuildHint(new QuickPlayEnded("", "retired")));          // no words: a launcher from before them
        Assert.False(QuickPlaySession.NewerBuildHint(new ErrorMessage("bad_build", "the game's build name is too long")));
        Assert.False(QuickPlaySession.NewerBuildHint(new ErrorMessage("bad_name", "taken")));
    }
}
