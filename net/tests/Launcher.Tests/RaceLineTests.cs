using Launcher.App.ViewModels;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The line under the lobby's steps once the race is known: the mode's title as the results page heads it,
/// the course and the laps; pointed at, what the mode changes.</summary>
public sealed class RaceLineTests
{
    [Fact]
    public void TheTitleIsTheBadgeElseTheModesNameElseQuickPlay()
    {
        Assert.Equal("KNOCKOUT", RaceLine.TitleOf("KNOCKOUT", "Knockout"));
        Assert.Equal("QUICK PLAY", RaceLine.TitleOf(null, "Quick Play"));    // Quick Play's normal mode
        Assert.Equal("MY CUP", RaceLine.TitleOf("", "My cup"));              // a host's own mode without a badge
        Assert.Equal("QUICK PLAY", RaceLine.TitleOf(null, null));
    }

    [Fact]
    public void ADrawnCardSaysItsModeCourseAndLaps()
    {
        var spicy = RaceLine.OfCard(new VoteCard("Knockout", 3, "KNOCKOUT", "Last place is out.", ["CPU racers Off"]), 7);
        Assert.Equal("KNOCKOUT · " + Launcher.Core.Courses.Name(7) + " · 3 laps", spicy.Text);
        Assert.Equal("Last place is out.\nCPU racers Off", spicy.Tip);
        Assert.True(spicy.Badged);

        var normal = RaceLine.OfCard(new VoteCard("Quick Play", 1), 0);
        Assert.Equal("QUICK PLAY · " + Launcher.Core.Courses.Name(0) + " · 1 lap", normal.Text);
        Assert.Null(normal.Tip);   // Quick Play's own rules: nothing to list
        Assert.False(normal.Badged);

        // unlimited laps say nothing
        Assert.Equal("OVERDRIVE · " + Launcher.Core.Courses.Name(8), RaceLine.OfCard(new VoteCard("Overdrive", 0, "OVERDRIVE"), 8).Text);
    }

    [Fact]
    public void ACustomLobbyNamesItsModeAndTheRulesItChanges()
    {
        var mode = new LobbyMode("h", "code", "Death Race", 1, "DEATH RACE", "Three lives each.");
        var rules = new LobbyRules(new Dictionary<string, int> { ["racers"] = 10 }, Mode: mode);
        var line = RaceLine.OfCustom(rules, null, 16, 5);
        Assert.Equal("DEATH RACE · " + Launcher.Core.Courses.Name(16) + " · 5 laps", line.Text);
        Assert.Equal("Three lives each.\n10 racers", line.Tip);
        Assert.True(line.Badged);
    }
}
