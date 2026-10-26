namespace Launcher.App.ViewModels;

/// <summary>What stands in the place of the race map, at the top of the Play page.</summary>
public enum HomeBlock
{
    /// <summary>A few words: nobody online, a lobby gathering, a race the player is not in, no answer.</summary>
    Words,

    /// <summary>The player's lobby votes on the course.</summary>
    Vote,

    /// <summary>The race of the player's lobby: the 3D map beside its standings.</summary>
    Race,
}

/// <summary>A racer in a list: the name the game shows, their tag ("4F2A"; null: none told) and the paint.</summary>
public sealed record PersonRow(string Name, string ColorHex, string? Tag = null);

/// <summary>A line of the standings beside the map; a knocked-out racer's detail is "KO".</summary>
public sealed record HomeStanding(string Place, string Name, string ColorHex, string Detail, bool You, bool Out = false);

/// <summary>An option of the course vote: its votes once the server tells them (as they come; from an older server
/// with the result), a pip a vote (the player's own first) and its chance ("38%"); after the draw, won (framed)
/// or lost (stepped back).</summary>
public sealed record VoteRow(string Name, bool Mine, bool Counted, string Odds, IReadOnlyList<VotePip> Pips, bool Won, bool Lost)
{
    /// <summary>It has votes: its chance in full (none: faint).</summary>
    public bool Voted => Pips.Count > 0;

    public bool Equals(VoteRow? other) =>
        other is not null && (Name, Mine, Counted, Odds, Won, Lost) == (other.Name, other.Mine, other.Counted, other.Odds, other.Won, other.Lost)
        && Pips.SequenceEqual(other.Pips);

    public override int GetHashCode() => HashCode.Combine(Name, Mine, Counted, Odds, Pips.Count, Won, Lost);
}

/// <summary>A lobby in a list (<c>Views/LobbyRowView</c>): its name, its racers as a value ("14/30"), rules, what it
/// is doing ("Gathering"), its tag ("your lobby"), and whether its row is on (the one shown above or
/// the player's own).</summary>
public sealed record LobbyRow(string Name, string Racers, string Rules, string Now, string Tag, bool On)
{
    /// <summary>The player's own lobby.</summary>
    public bool Mine => Tag == YourLobby;

    public const string YourLobby = "your lobby";
}

/// <summary>One of the player's last races, from its replay.</summary>
public sealed record RecentRace(string Course, string Place, string Of, string Time, string Best, string Machine, string When,
    string Path);
