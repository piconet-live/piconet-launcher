using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>Players on another version, as the lobby shows them (labels and values, no
/// sentence): "2 players online on another version", then the part that differs with each side's hash.</summary>
public sealed class OtherBuildsValuesTests
{
    [Fact]
    public void ThePartThatDiffersIsAKeyWithEachSidesHash()
    {
        var (label, values) = QuickPlaySession.OtherBuildsValues("aaaa/8a3a451e99/cccc", [new OtherBuild("aaaa/672749d2ff/cccc", 2)]);
        Assert.Equal("2 players online on another version", label);
        Assert.Equal([("native build · yours", "8a3a451e"), ("theirs", "672749d2")], values);
    }

    /// <summary>The server tells each build whole: only the part whose hash differs is named, by its first eight
    /// characters.</summary>
    [Fact]
    public void OnlyThePartWhoseWholeHashDiffersIsNamed()
    {
        var (_, values) = QuickPlaySession.OtherBuildsValues("d596453b11/dad106db22/cf20003433", [new OtherBuild("aaaa111199/dad106db22/cf20003433", 2)]);
        Assert.Equal([("online part · yours", "d596453b"), ("theirs", "aaaa1111")], values);
    }

    /// <summary>Two hashes that share their first eight characters are shown as far as they differ: the label always
    /// carries values that tell the builds apart.</summary>
    [Fact]
    public void HashesThatShareTheirBeginningAreShownUntilTheyDiffer()
    {
        var (label, values) = QuickPlaySession.OtherBuildsValues("d596453b11/dad106db22/cf20003433", [new OtherBuild("d596453b12/dad106db23/cf20003433", 1)]);
        Assert.Equal("1 player online on another version", label);
        Assert.Equal([("online part · yours", "d596453b11"), ("theirs", "d596453b12"), ("native build · yours", "dad106db22"),
            ("theirs", "dad106db23")], values);
        Assert.Contains("the game's native build differs (yours dad106db22, theirs dad106db23)",
            QuickPlaySession.OtherBuildsText("d596453b11/dad106db22/cf20003433", [new OtherBuild("d596453b12/dad106db23/cf20003433", 1)]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderOrNewerLauncherIsSaidAsTheirs()
    {
        var (label, values) = QuickPlaySession.OtherBuildsValues("a/b/c", [new OtherBuild(null, 1), new OtherBuild(null, 3)]);
        Assert.Equal("4 players online on another version", label);
        Assert.Equal([("theirs", "older launcher")], values);
        Assert.Equal([("theirs", "newer launcher")], QuickPlaySession.OtherBuildsValues(null, [new OtherBuild("a/b/c", 1)]).Values);
        Assert.Equal("1 player online on another version", QuickPlaySession.OtherBuildsValues(null, [new OtherBuild("a/b/c", 1)]).Label);
    }
}

/// <summary>A course card without a picture of its own shows its mark.</summary>
public sealed class CourseCardMarkTests
{
    [Fact]
    public void AStoryStageShowsItsNameAndTheRandomCourseAQuestionMark()
    {
        var story = new Launcher.App.ViewModels.CourseOption(0, "Story: Chapter 3", 300);
        Assert.True(story.ShowsMark);
        Assert.Equal("Chapter 3", story.Mark);
        Assert.Equal("?", new Launcher.App.ViewModels.CourseOption(1, "Random course", CourseVote.RandomCourse).Mark);
    }

    /// <summary>Four story chapters show a picture: three a cup venue's, named on the card,
    /// and chapter 8 the story mode's own, which the game names nowhere; the others have none.</summary>
    [Fact]
    public void FourStoryChaptersHaveTheirVenuesPicture()
    {
        var first = CourseNames.FirstStoryStage;
        Assert.Equal([3, 7, 8, 9], Enumerable.Range(1, CourseNames.StoryChapters).Where(c => DiscArt.HasVenue(first + c - 1)));
        Assert.Equal(["Story · Casino Palace", "Story · Mute City", "Story", "Story · Phantom Road", "Story"],
            Enumerable.Range(1, CourseNames.StoryChapters).Where(c => c is 3 or 7 or 8 or 9).Append(1).Select(c => new Launcher.App.ViewModels.CourseOption(0, $"Story: Chapter {c}", first + c - 1).Place));
    }
}
