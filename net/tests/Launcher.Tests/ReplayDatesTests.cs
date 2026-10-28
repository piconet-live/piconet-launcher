using Launcher.Core;

namespace Launcher.Tests;

/// <summary>The Replays page's groups by date and what a row says of its time, in the player's own time: weeks start
/// on Monday, and every month before this one is a group of its own.</summary>
public sealed class ReplayDatesTests
{
    // Wednesday 28 October 2026, 3 PM local
    private static readonly DateTimeOffset Now = Local(2026, 10, 28, 15, 0);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

    [Fact]
    public void ReplaysAreGroupedByDayThenWeekThenMonth()
    {
        string Title(DateTimeOffset at) => ReplayDates.Group(at, Now).Title;
        Assert.Equal("Today", Title(Local(2026, 10, 28, 0, 1)));
        Assert.Equal("Yesterday", Title(Local(2026, 10, 27, 23, 59)));
        Assert.Equal("Earlier this week", Title(Local(2026, 10, 26, 9, 0)));   // Monday
        Assert.Equal("Last week", Title(Local(2026, 10, 25, 9, 0)));           // Sunday
        Assert.Equal("Last week", Title(Local(2026, 10, 19, 9, 0)));
        Assert.Equal("Earlier this month", Title(Local(2026, 10, 18, 9, 0)));
        Assert.Equal("September", Title(Local(2026, 9, 30, 9, 0)));
        Assert.Equal("January", Title(Local(2026, 1, 1, 9, 0)));
        Assert.Equal("December 2025", Title(Local(2025, 12, 31, 9, 0)));
        Assert.Equal("Today", Title(Local(2026, 10, 29, 9, 0)));   // a clock set ahead: today, not a group of its own
    }

    [Fact]
    public void GroupsAreOrderedNewestFirstAndEachMonthIsItsOwn()
    {
        DateTimeOffset[] newestFirst =
        [
            Local(2026, 10, 28, 9, 0), Local(2026, 10, 27, 9, 0), Local(2026, 10, 26, 9, 0), Local(2026, 10, 20, 9, 0),
            Local(2026, 10, 2, 9, 0), Local(2026, 9, 2, 9, 0), Local(2026, 8, 2, 9, 0), Local(2025, 12, 2, 9, 0), Local(2024, 12, 2, 9, 0),
        ];
        var orders = newestFirst.Select(at => ReplayDates.Group(at, Now).Order).ToList();
        Assert.Equal(orders.Order(), orders);
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void ARowSaysAsMuchOfItsTimeAsItsGroupLeavesOut()
    {
        string When(DateTimeOffset at) => ReplayDates.When(at, Now);
        Assert.Equal("9:05 AM", When(Local(2026, 10, 28, 9, 5)));
        Assert.Equal("11:40 PM", When(Local(2026, 10, 27, 23, 40)));
        Assert.Equal("Mon 9:05 AM", When(Local(2026, 10, 26, 9, 5)));
        Assert.Equal("Tue 9:05 AM", When(Local(2026, 10, 20, 9, 5)));
        Assert.Equal("Oct 2, 9:05 AM", When(Local(2026, 10, 2, 9, 5)));
        Assert.Equal("Nov 10, 5:55 PM", When(Local(2025, 11, 10, 17, 55)));   // the year is the group's title
    }
}
