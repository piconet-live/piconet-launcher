using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// What comes from a coordinator, checked before the launcher keeps or shows any of it. A coordinator passes on what
/// players send it (a lobby's players, chat, a racer's live race) and is itself any computer on the network when it
/// is a LAN server, so nothing in a message is taken as given: the message types say "never null" of parts that JSON
/// can leave out, and the screens read them on the window's thread, where one exception closes the launcher.
/// A message is made whole where that loses nothing (a player with no colour
/// is shown in grey, a name with no text is empty), and dropped where it cannot be (a race with no set-up).
/// </summary>
public static class ServerMessages
{
    /// <summary>The longest message a coordinator may send (a lobby of 30 with its race is about 8 KiB).</summary>
    public const int MaxBytes = 1 << 20;

    /// <summary>Relays measured for one match at most (five pings each, half a second a ping that is not answered).</summary>
    public const int MaxRelays = 16;

    public const int MaxChatLength = 500;

    /// <summary>The longest chat mute a launcher counts down (a week; the server's operator mutes for a week at most).</summary>
    public const int MostMuteSeconds = 7 * 24 * 3600;

    /// <summary>More players than any service of this game has: a count above it is not a count.</summary>
    public const int MaxPlayers = 1_000_000;

    /// <summary>The lobbies and the races of a server's public status that the Play page lists (the official service
    /// has a handful; a list without end would be laid out row by row on the window's thread).</summary>
    public const int MaxListed = 100;

    /// <summary>The options of a course vote at most (the game's servers offer six).</summary>
    public const int MaxVoteOptions = 16;

    private const int MaxNameLength = 40;
    private const int MaxCourseNameLength = 80;
    private const int MaxSeats = 30;

    /// <summary>The longest text of a stat pack's values: every word of the car data, 6 bytes each, as base64.</summary>
    private const int MaxStatPackText = ((StatPackRules.MostEntries * 6) + 2) / 3 * 4;

    /// <summary>The members of a lobby at most, and so its seats: the page draws one row a seat.</summary>
    private const int MaxMembers = 2 * MaxSeats;
    private static readonly Rgb NoColor = new(140, 150, 170);

    /// <summary>The message of this JSON text, checked; null when it is no message, or one that cannot be used.</summary>
    public static ServerMessage? Checked(string text)
    {
        ServerMessage? message;
        try
        {
            message = ControlProtocol.DecodeServer(text);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // not JSON, not a message, or a message of a kind this launcher does not know
            return null;
        }
        return Checked(message);
    }

    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",

        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    public static ServerMessage? Checked(ServerMessage? message) => message switch
    {
        Welcome w => w.PlayerId is null ? null
            : w with { ServerName = Text(w.ServerName, MaxNameLength), Rules = Rules(w.Rules), Tag = Tag(w.Tag) },
        // the rules again, changed while connected: checked as the welcome's
        RulesChanged r => r.Rules is null ? null : r with { Rules = Rules(r.Rules)! },
        ErrorMessage e => e with { Code = e.Code ?? "", Message = Text(e.Message, MaxChatLength), Seconds = e.Seconds is { } s ? Math.Clamp(s, 0, MostMuteSeconds) : null },
        LobbyState l => Lobby(l),
        LeftLobby l => l with { LobbyId = l.LobbyId ?? "" },
        // a notice's tier is one of the four (one this launcher does not know is the least, info), its id short
        ChatMessage c => c with
        {
            Channel = c.Channel ?? "", FromId = c.FromId ?? "", FromName = Text(c.FromName, MaxNameLength), Text = Text(c.Text, MaxChatLength),
            FromTag = Tag(c.FromTag),
            Level = c.Level is null ? null : Notices.Known(c.Level) ? c.Level : Notices.Info,
            NoticeId = c.NoticeId is { Length: >= 1 and <= MaxNameLength } id ? id : null,
        },
        // the last hour's notices: a list no longer than the launcher keeps, each with an id, a text and a time
        RecentNotices r => r.Notices is null ? null : r with
        {
            Notices = [.. r.Notices.Where(n => n is { Id.Length: >= 1 and <= MaxNameLength, Text: not null }).TakeLast(NoticeLog.Most)
                .Select(n => n with { Level = Notices.Known(n.Level) ? n.Level : Notices.Info, Text = Text(n.Text, MaxChatLength) })],
        },
        MeasureRelays m => m.MatchId is null || m.Relays is null ? null
            : m with { Relays = [.. m.Relays.Where(Sound).Take(MaxRelays).Select(r => r with { Id = r.Id ?? "" })] },
        MatchStart s => Start(s),
        // a spectator's race: as a match start's, the seat the one watched first
        WatchStart w => Start(new MatchStart(w.MatchId, w.Relay, w.Token, w.Seat, w.Setup)) is { } s
            ? new WatchStart(s.MatchId, s.Relay, s.Token, s.Seat, s.Setup) : null,
        // the server's pack for the Garage: three texts, the values no longer than a whole car data's; what they
        // hold is the session's to judge (StatPack.FromWire), as a match start's
        StatPackValues v => v.Pack is { Name: not null, Fingerprint: not null, Values: { Length: <= MaxStatPackText } } ? v : null,
        Requeued r => r with { MatchId = r.MatchId ?? "", Reason = Text(r.Reason, MaxNameLength) },
        QuickPlayEnded r => r with
        {
            MatchId = r.MatchId ?? "", Reason = Text(r.Reason, MaxNameLength), Message = r.Message is null ? null : Text(r.Message, MaxChatLength),
        },
        CourseVote v => v.MatchId is null || v.Options is not { Count: >= 1 and <= MaxVoteOptions } ? null : v,
        // the counts are added up and compared for the vote's bars and shares: a count no vote can have (less than
        // none, more than MaxPlayers) is none
        CourseVotes v => v.MatchId is null || v.Votes is not { Count: <= MaxVoteOptions } || v.Voted is null ? null
            : v with { Votes = Counts(v.Votes), Voted = [.. v.Voted.Where(id => id is not null).Take(MaxMembers)] },
        CourseChosen c => c.MatchId is null || c.Options is null || c.Votes is not { Count: <= MaxVoteOptions } || c.Winner < 0 || c.Winner >= c.Votes.Count
            ? null : c with { Votes = Counts(c.Votes) },
        ChooseMachine c => c.MatchId is null || c.Machines is null ? null
            : c with { Machines = [.. c.Machines.Where(Machines.Has).Distinct()] },
        LiveRace l => l.MatchId is not null && Sound(l.Snapshot) ? l : null,
        RaceDesynced d => d with { MatchId = d.MatchId ?? "" },
        // the lobby's pings: a list no longer than a lobby, each a player's id and a number of three digits at most
        LobbyPings p => p.LobbyId is null || p.Pings is null ? null : p with
        {
            Pings = [.. p.Pings.Where(x => x?.Id is not null).Take(MaxMembers).Select(x => x with { Ms = Math.Clamp(x.Ms, 0, PlayerPing.MostMs) })],
        },
        // the player's tag for this connection: only one a tag can be
        TagChanged t => PlayerTags.IsValid(t.Tag) ? t : null,
        // the players' list: this launcher keeps none (its hello does not ask for it)
        PlayersList or PlayersChanged => null,
        _ => message,
    };

    /// <summary>A coordinator's public status (GET /api/status, which anybody's server answers), checked as its
    /// messages are, before the Play page lists it on the window's thread: every list there, and no longer than the
    /// page can show; a lobby's seats as the lobby's own message has them; names as text is shown; a paint that is a
    /// colour's text or none; a race's live report only when it can be shown (<see cref="Sound(RaceSnapshot?)"/>).
    /// Null when there is no status.</summary>
    public static ServiceStatus? Checked(ServiceStatus? status) => status is null ? null : status with
    {
        Name = Text(status.Name, MaxNameLength),
        PlayersOnline = Math.Clamp(status.PlayersOnline, 0, MaxPlayers),
        Lobbies = [.. (status.Lobbies ?? []).Where(l => l is { Id: not null }).Take(MaxListed).Select(l => l with
        {
            Players = Racers(l.Players),
            Capacity = Math.Clamp(l.Capacity, 0, MaxMembers),
            StartVotes = Math.Clamp(l.StartVotes, 0, MaxMembers),
            Stage = l.Stage ?? LobbyState.Forming,
        })],
        Starting = [.. (status.Starting ?? []).Where(m => m is not null).Take(MaxListed)],
        Races = [.. (status.Races ?? []).Where(r => r is { Id: not null }).Take(MaxListed).Select(r => r with
        {
            Relay = r.Relay ?? "",
            Region = r.Region ?? "",
            CourseName = Text(r.CourseName, MaxCourseNameLength),
            Players = Racers(r.Players),
            Live = Sound(r.Live) ? r.Live : null,
        })],
        Relays = [.. (status.Relays ?? []).Where(r => r is not null).Take(MaxListed)],
    };

    private static IReadOnlyList<RacerView> Racers(IReadOnlyList<RacerView>? racers) =>
        [.. (racers ?? []).Where(r => r is not null).Take(MaxMembers)
            .Select(r => r with
            {
                Name = Text(r.Name, MaxNameLength), Color = r.Color is { Length: <= 9 } ? r.Color : null, Tag = Tag(r.Tag),
            })];

    private static IReadOnlyList<int> Counts(IReadOnlyList<int> votes) => [.. votes.Select(n => Math.Clamp(n, 0, MaxPlayers))];

    /// <summary>Whether a live race can be shown: at most 30 racers, each with a seat of the race (0 to 29) that no
    /// other racer has, a state, and a place, speed and energy that are numbers (JSON carries a number too large
    /// for the type as infinity). From a racer's launcher through the coordinator, or from the player's own game.
    /// The map keeps its dots by seat and aims its camera by their places, on the window's thread.</summary>
    public static bool Sound(RaceSnapshot? snapshot)
    {
        if (snapshot is not { Racers: { Count: <= MaxSeats } racers })
        {
            return false;
        }
        var taken = 0u;                                                        // one bit a seat: 30 seats
        foreach (var r in racers)
        {
            if (r is not { Seat: >= 0 and < MaxSeats, State: not null } || (taken & (1u << r.Seat)) != 0 ||
                !(float.IsFinite(r.X) && float.IsFinite(r.Y) && float.IsFinite(r.Z) && float.IsFinite(r.Speed) && float.IsFinite(r.TopSpeed) &&
                  float.IsFinite(r.Energy) && float.IsFinite(r.MaxEnergy) && float.IsFinite(r.Distance)))
            {
                return false;
            }
            taken |= 1u << r.Seat;
        }
        return true;
    }

    /// <summary>Text a player or a server wrote, as the launcher shows it: no control characters and none of the
    /// marks that turn the reading direction around (a name could show as another's), cut to a length.</summary>
    public static string Text(string? text, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var shown = text.Where(c => !char.IsControl(c) && c is not ((>= '‪' and <= '‮') or (>= '⁦' and <= '⁩') or '‎' or '‏'))
            .Take(length).ToArray();
        return shown.Length == text.Length ? text : new string(shown);
    }

    /// <summary>A player's tag as the page shows it after the name: only one a tag can be (<see cref="PlayerTags.IsValid"/>:
    /// four characters of its alphabet), else none, as names are made sound before a page shows them.</summary>
    public static string? Tag(string? tag) => PlayerTags.IsValid(tag) ? tag : null;

    private static bool Sound(RelayEndpoint? relay) => relay is { Host.Length: <= 255, Port: >= 1 and <= 65535 };

    private static ServerRules? Rules(ServerRules? rules) => rules is null ? null : rules with
    {
        AllowedMachines = [.. (rules.AllowedMachines ?? []).Where(Machines.Has).Distinct()],
        // a range that is none (missing, outside 0 to 100, low over high): each player's own, which the server clamps
        MachineSetting = rules.MachineSetting is { IsSound: true } setting ? setting : SettingRange.Free,
        Race = rules.Race ?? new RaceRules(),
        // the lobby's latency limit, inside the rule's range (shown only: the coordinator applies it)
        LatencyLimitMs = HostRules.LatencyLimit.Kept(rules.LatencyLimitMs),
    };

    private static LobbyState Lobby(LobbyState l) => l with
    {
        LobbyId = l.LobbyId ?? "",
        Stage = l.Stage ?? LobbyState.Forming,
        // the page draws a row a seat, the empty ones too
        Capacity = Math.Clamp(l.Capacity, 0, MaxMembers),
        Players = [.. (l.Players ?? []).Where(p => p is not null).Take(MaxMembers)
            .Select(p => p with
            {
                Id = p.Id ?? "", Name = Text(p.Name, MaxNameLength), Color = p.Color ?? NoColor, Tag = Tag(p.Tag),
                Pack = p.Pack is null ? null : Text(p.Pack, Online.Protocol.StatPack.MostName),
            })],
        Race = l.Race is { MatchId: not null, Seats: not null } race ? race with { Seats = Seats(race.Seats) } : null,
        // the counts are added up for the lobby's notice: one no service has (none, less than none, more than
        // MaxPlayers) is left out
        OtherBuilds = l.OtherBuilds is null ? null : [.. l.OtherBuilds.Where(o => o is { Players: >= 1 and <= MaxPlayers }).Take(MaxSeats)],
        // a label shows the name: only one a pack can have, with a fingerprint that is one; and the author only
        // when it is a text a pack's author can be
        StatPack = l.StatPack is { } pack && Online.Protocol.StatPack.IsName(pack.Name)
            && pack.Fingerprint is { Length: 40 } print && print.All(char.IsAsciiHexDigitLower)
            ? pack with { Author = Online.Protocol.StatPack.IsName(pack.Author) ? pack.Author : null } : null,
        // the packs a racer may pick (the machine stage's list, by name): as the lobby's own pack, a few at most
        SeatPacks = l.SeatPacks is null ? null
            : [.. l.SeatPacks.Where(PackName).Take(RaceSetup.MostSeatPacks).Select(p => p with { Author = null })],
    };

    /// <summary>A pack's name as a label can show it: a name a pack can have, and a fingerprint that is one.</summary>
    private static bool PackName(StatPackName? pack) =>
        pack is not null && Online.Protocol.StatPack.IsName(pack.Name) && pack.Fingerprint is { Length: 40 } print && print.All(char.IsAsciiHexDigitLower);

    private static StatPackWire? Whole(StatPackWire? pack) =>
        pack is null ? null : pack with { Name = pack.Name ?? "", Fingerprint = pack.Fingerprint ?? "", Values = pack.Values ?? "" };

    private static IReadOnlyList<SeatSetup> Seats(IReadOnlyList<SeatSetup> seats) =>
        [.. seats.Where(s => s is not null).Take(MaxSeats).Select(s => s with { Name = Text(s.Name, MaxNameLength) })];

    /// <summary>A match start the game can be given: a match, its relay and token, this player's seat, and the race's
    /// seats, each a seat of the race. A stat pack it carries is the session's to judge (it refuses the start with
    /// a sentence for the player: <c>QuickPlaySession</c>).</summary>
    private static MatchStart? Start(MatchStart s)
    {
        if (string.IsNullOrEmpty(s.MatchId) || s.MatchId.Length > 64 || !Sound(s.Relay) || s.Token is null || s.Seat is < 0 or >= MaxSeats ||
            s.Setup?.Seats is not { Count: >= 1 and <= MaxSeats } seats || seats.Any(x => x is not { Seat: >= 0 and < MaxSeats }) ||
            s.Setup.Packs is { } packs && (packs.Count > RaceSetup.MostSeatPacks || packs.Any(p => p is null)))
        {
            return null;
        }
        return s with
        {
            Relay = s.Relay with { Id = s.Relay.Id ?? "" },
            Setup = s.Setup with
            {
                Seats = Seats(seats),
                // a pack's parts left out are empty: the session (the race's pack) and the game (a seat's) refuse it
                Stats = Whole(s.Setup.Stats),
                Packs = s.Setup.Packs is null ? null : [.. s.Setup.Packs.Select(p => Whole(p)!)],
            },
        };
    }
}
