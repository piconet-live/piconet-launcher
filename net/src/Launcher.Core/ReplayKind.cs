namespace Launcher.Core;

/// <summary>The Replays page's tabs: every replay, the races, the practice runs.</summary>
public enum ReplayKind
{
    All,
    Races,
    Practice,
}

/// <summary>Which replays a tab lists, and how many.</summary>
public static class ReplayKinds
{
    public static bool Shows(this ReplayKind kind, ReplayInfo replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        return kind switch
        {
            ReplayKind.Races => !replay.Practice,
            ReplayKind.Practice => replay.Practice,
            _ => true,
        };
    }

    public static int Count(this ReplayKind kind, IEnumerable<ReplayInfo> replays) => replays.Count(r => kind.Shows(r));

    /// <summary>The tab shows a listed replay (<see cref="ReplayListing"/>), as it would its file.</summary>
    public static bool Shows(this ReplayKind kind, ReplayListing replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        return kind switch
        {
            ReplayKind.Races => !replay.Practice,
            ReplayKind.Practice => replay.Practice,
            _ => true,
        };
    }

    public static int Count(this ReplayKind kind, IEnumerable<ReplayListing> replays) => replays.Count(r => kind.Shows(r));
}
