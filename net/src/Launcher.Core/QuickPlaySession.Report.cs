using Online.Protocol;

namespace Launcher.Core;

// The race as this player's game shows it: passed on for the lobby's waiting players, and the last of it sent with the
// race's end (RaceOver.Final), from which the coordinator takes the lobby's last finish for the next start grid.
public sealed partial class QuickPlaySession
{
    /// <summary>The race as this player's game sees it, passed on to the coordinator when it asked
    /// (<see cref="MatchStart.ReportLive"/>) for the lobby's waiting players, and kept as the race's last seen.</summary>
    public Task ReportAsync(RaceSnapshot snapshot)
    {
        if (Race is { Spectate: false } racing)
        {
            _lastSeen = (racing.Start.MatchId, snapshot);
        }
        return Race is { Start.ReportLive: true } race && _client is not null
            ? _client.ReportRaceAsync(race.Start.MatchId, snapshot)
            : Task.CompletedTask;
    }

    /// <summary>The race as this player's game last showed it (<see cref="ReportAsync"/>), and of which match: sent
    /// with the race's end (<see cref="RaceOver.Final"/>), for the start grid of the lobby's next race.</summary>
    private (string MatchId, RaceSnapshot Snapshot)? _lastSeen;

    private RaceSnapshot? LastSeenOf(string matchId) => _lastSeen is { } seen && seen.MatchId == matchId ? seen.Snapshot : null;
}
