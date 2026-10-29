using System.Globalization;

namespace Launcher.Core;

/// <summary>
/// The Replays page's groups by date, newest first: Today, Yesterday, Earlier this week, Last week, Earlier this month,
/// then a month a group ("September"; with its year once it is not this year's: "December 2025"). A week starts on
/// Monday. Every time is the player's own (local) time.
/// </summary>
public static class ReplayDates
{
    /// <summary>A replay's group: its order (smaller is newer: groups are shown in this order) and its title.</summary>
    public static (long Order, string Title) Group(DateTimeOffset at, DateTimeOffset now)
    {
        var day = at.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // this week's Monday
        if (day >= today)
        {
            return (0, "Today");
        }
        if (day == today.AddDays(-1))
        {
            return (1, "Yesterday");
        }
        if (day >= week)
        {
            return (2, "Earlier this week");
        }
        if (day >= week.AddDays(-7))
        {
            return (3, "Last week");
        }
        if (day.Year == today.Year && day.Month == today.Month)
        {
            return (4, "Earlier this month");
        }
        // older: by month, the newest first
        var months = (today.Year - day.Year) * 12 + today.Month - day.Month;
        var title = day.ToString(day.Year == today.Year ? "MMMM" : "MMMM yyyy", CultureInfo.InvariantCulture);
        return (4 + months, title);
    }

    /// <summary>When, as a row of a group says it: the time on Today and Yesterday ("3:12 PM"), the day and time this
    /// week and last ("Mon 3:12 PM"), the date and time before ("Oct 3, 3:12 PM"; the year is the group's title).</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var (order, _) = Group(at, now);
        var time = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return order switch
        {
            <= 1 => time,
            <= 3 => local.ToString("ddd ", CultureInfo.InvariantCulture) + time,
            _ => local.ToString("MMM d, ", CultureInfo.InvariantCulture) + time,
        };
    }
}
