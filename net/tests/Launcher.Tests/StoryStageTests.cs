using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The story's stages as courses of a race: every one, labelled in the
/// vote by its story mode chapter.</summary>
public sealed class StoryStageTests
{
    /// <summary>A stage of the story is named by its chapter, as the game numbers them, wherever the launcher names
    /// a course: stages 37 to 45 are chapters 1 to 9, with "Story" where a cup course has its venue. A vote that
    /// holds one shows it so, among cup courses, and so does a recording made on one.</summary>
    [Fact]
    public void AStoryStageIsNamedByItsChapterInAVoteAndOnARecording()
    {
        Assert.Equal(["Story: Chapter 1", "Story: Chapter 2", "Story: High-Stakes Jumps", "Story: Chapter 4", "Story: Chapter 5",
            "Story: Chapter 6", "Story: Championship Circuit", "Story: Emperor's Trial", "Story: Dimension's Edge"],
            Enumerable.Range(37, 9).Select(Courses.Name));
        Assert.Equal(Enumerable.Range(1, 9).Cast<int?>(), Enumerable.Range(37, 9).Select(CourseNames.StoryChapter));
        Assert.Equal((null, null, "Mute City: Sonic Oval", "Course 46", "Course 49"),
            (CourseNames.StoryChapter(36), CourseNames.StoryChapter(46), Courses.Name(36), Courses.Name(46), Courses.Name(49)));

        // a vote's options as the lobby's cards show them: the venue's line, then the course's
        int[] offered = [1, 43, 39, CourseVote.RandomCourse];
        var cards = offered.Select((course, i) => new Launcher.App.ViewModels.CourseOption(i,
            course == CourseVote.RandomCourse ? "Random course" : Courses.Name(course), course)).ToList();
        Assert.Equal([("Mute City", "Twist Road"), ("Story · Mute City", "Championship Circuit"), ("Story · Casino Palace", "High-Stakes Jumps"), ("Any venue", "Random course")],
            cards.Select(c => (c.Place, c.Track)));
        Assert.Equal("STORY", cards[1].Venue);

        // a recording made on one: the Replays page's row
        var replay = new ReplayInfo("race.fzrp", new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero), 45, 3, ["ANA"], "ANA",
            TimeSpan.FromSeconds(125), null, null);
        Assert.Equal("Story: Dimension's Edge", replay.CourseName);
        Assert.Equal(("Story", "Dimension's Edge"), ReplayWords.Course(replay.CourseName));
        var row = new Launcher.App.ViewModels.ReplayItem(replay);
        Assert.Equal(("Dimension's Edge", "Story · 3 laps"), (row.Track, row.Under));
    }
}
