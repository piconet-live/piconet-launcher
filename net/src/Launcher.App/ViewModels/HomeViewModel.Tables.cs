using System.Globalization;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

/// <summary>Under the map's place: the server's lobbies as a table, and the player's last races from their replays.</summary>
public sealed partial class HomeViewModel
{
    /// <summary>The table: every lobby the server lists (its Quick Play lobbies; it has no others today), the player's own
    /// marked; without the server's list, the player's own lobby alone.</summary>
    private void ShowLobbies(QuickPlaySession? session, ServiceStatus? status)
    {
        var mine = session?.Lobby;
        var shown = mine?.LobbyId ?? (status is null ? null : ShownLobby(status)?.Id);
        var rows = new List<LobbyRow>();
        foreach (var l in status?.Lobbies ?? [])
        {
            if (l.Id == mine?.LobbyId)
            {
                // the player's own lobby from the lobby itself: it is told at once, the status every few seconds
                rows.Add(Mine(StatusText.LobbyName(status, l.Id), mine, session!));
                continue;
            }
            // what it is doing and how full, as every lobby list shows it
            rows.Add(new LobbyRow(StatusText.LobbyName(status, l.Id), StatusText.Count(l.Players.Count, l.Capacity), "Quick Play",
                StatusText.Stage(l.Stage), "", l.Id == shown));
        }
        if (mine is not null && rows.All(r => !r.Mine))
        {
            rows.Insert(0, Mine("Your lobby", mine, session!));
            AskSoon(mine.LobbyId);
        }
        Replace(Lobbies, rows);
        var online = status is null ? "" : status.PlayersOnline == 1 ? "1 racer online" : $"{status.PlayersOnline} racers online";
        LobbiesFootWarns = status is not null && _feed.Failed;
        LobbiesFoot = status is null ? ""
            : _feed.Failed ? $"No answer since {_feed.LatestAt?.ToLocalTime().ToString("h:mm:ss tt", CultureInfo.InvariantCulture)}"
            : status.Lobbies.Count == 0 ? "No lobbies"
            : $"{(status.Lobbies.Count == 1 ? "1 lobby" : $"{status.Lobbies.Count} lobbies")} · {online}";
    }

    /// <summary>The player's own lobby, its phase as its own page says it ("Course vote", "Draw" while starting).</summary>
    private static LobbyRow Mine(string name, LobbyState lobby, QuickPlaySession session) =>
        new(name, StatusText.Count(lobby.Players.Count, lobby.Capacity), "Quick Play",
            lobby.Stage == LobbyState.Starting && session.CourseVote is not null ? session.CourseChosen is null ? "Course vote" : "Draw"
            : StatusText.Stage(lobby.Stage), LobbyRow.YourLobby, true);

    /// <summary>The player's lobby is not in the server's list yet (it is asked every few seconds): asks now, once per
    /// lobby.</summary>
    private void AskSoon(string lobbyId)
    {
        if (_askedFor != lobbyId)
        {
            _askedFor = lobbyId;
            _ = _feed.AskAsync();
        }
    }

    /// <summary>The last three races from the replay folder (read again only when the folder's files changed).</summary>
    private void RefreshRecent()
    {
        var folder = _state.Settings.ReplayFolder;
        string stamp;
        try
        {
            stamp = folder is not null && Directory.Exists(folder)
                ? $"{folder}|{Directory.GetLastWriteTimeUtc(folder).Ticks}|{Directory.EnumerateFiles(folder).Count()}" : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stamp = "";
        }
        if (stamp == _replaysOf)
        {
            return;
        }
        _replaysOf = stamp;
        var all = stamp.Length == 0 ? [] : ReplayFile.List(folder);
        Replace(Recent, [.. all.Take(3).Select(Row)]);
        HasRecent = Recent.Count > 0;
        AllReplays = all.Count == 1 ? "The 1 replay" : $"All {all.Count} replays";
    }

    private static RecentRace Row(ReplayInfo info)
    {
        var result = info.RecorderResult;
        var place = result switch
        {
            _ when info.Practice => "Practice",
            null => "",
            { Out: true } => "KO",
            _ => ReplayWords.Ordinal(result.Place),
        };
        var machine = info.Recorder is { } recorder ? Theme.GameSkin.NameOf(recorder.Raced) : "";
        return new RecentRace(info.CourseName, place, info.Results is { Count: > 0 } all && place.Length > 0 && !info.Practice ? $"of {all.Count}" : "",
            ReplayWords.Time(info), result?.BestLap is { } b ? ReplayWords.Clock(b) : "",
            info.Recorder is null ? "" : machine, When(info.Recorded.ToLocalTime(), DateTimeOffset.Now), info.Path, info.Practice);
    }

    /// <summary>"Today 7:42 PM", "Yesterday 7:42 PM", then the date.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var days = (now.Date - at.Date).Days;
        var time = at.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return days switch
        {
            0 => $"Today {time}",
            1 => $"Yesterday {time}",
            _ => at.ToString(at.Year == now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture),
        };
    }
}
