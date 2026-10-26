using Launcher.App;

namespace Launcher.Tests;

/// <summary>When the open launcher looks for an update again (players leave it open).</summary>
public sealed class UpdateTimesTests
{
    private static readonly DateTimeOffset Last = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheNextLookIsTwoHoursAndUpToFiveMinutesAfterTheLast()
    {
        Assert.Equal(Last.AddHours(2), UpdateTimes.Next(Last, 0));
        Assert.Equal(Last.AddHours(2).AddMinutes(2.5), UpdateTimes.Next(Last, 0.5));
        Assert.Equal(Last.AddHours(2).AddMinutes(5), UpdateTimes.Next(Last, 1));
        Assert.Equal(Last.AddHours(2).AddMinutes(5), UpdateTimes.Next(Last, 7));   // (the spread is at most five minutes)
    }

    [Fact]
    public void AGamesEndLooksAgainOnlyWhenTheLastLookIsOverHalfAnHourOld()
    {
        Assert.True(UpdateTimes.AfterPlayDue(Last, null));
        Assert.False(UpdateTimes.AfterPlayDue(Last.AddMinutes(10), Last));
        Assert.False(UpdateTimes.AfterPlayDue(Last.AddMinutes(30), Last));
        Assert.True(UpdateTimes.AfterPlayDue(Last.AddMinutes(31), Last));
    }
}
