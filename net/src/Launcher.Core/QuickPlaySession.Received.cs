using System.Diagnostics.CodeAnalysis;
using Online.Protocol;
using Online.Protocol.Chat;

namespace Launcher.Core;

// What each of the server's messages does to the session: one step a message, in the order they are told apart.
public sealed partial class QuickPlaySession
{
    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",
        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    [SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",
        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    private void OnReceived(ServerMessage message)
    {
        _lastWord = message as ErrorMessage;
        if (NewerBuildHint(message))
        {
            NewerBuildHinted?.Invoke();
        }
        switch (message)
        {
            case Welcome w:
                OnWelcome(w);
                break;
            case LobbyState l:
                OnLobbyState(l);
                LeftRaceBehind(l);
                break;
            case LiveRace live when Lobby?.Race?.MatchId == live.MatchId:
                Live = live;
                Changed?.Invoke();
                break;
            case LeftLobby left:
                OnLeftLobby(left);
                break;
            case LobbyList list:
                OnLobbyList(list);
                break;
            case TagChanged t when PlayerTags.IsValid(t.Tag):
                Tag = t.Tag;   // for this connection, as the welcome's
                Changed?.Invoke();
                break;
            case RulesChanged r when PlayerId is not null:
                // the operator changed the server's rules: shown at once (the lobby, home); the queued machine and
                // a race already started keep what was sent, and the next match's choice offers the new machines
                Rules = r.Rules;
                Changed?.Invoke();
                break;
            case StatPackValues v:
                // the server's pack, for the Garage: kept only when it is a pack this launcher takes (the game's
                // own ranges); one it does not take shows the disc's stats, and its match start says why
                _served = StatPack.FromWire(v.Pack).Pack;
                Changed?.Invoke();
                break;
            case LobbyPings p when Lobby?.LobbyId == p.LobbyId:
                Pings = p.Pings.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Last().Ms);
                Changed?.Invoke();
                break;
            case CourseVote v:
                OnCourseVote(v);
                break;
            case CourseVotes v when CourseVote is { } open && open.Vote.MatchId == v.MatchId && v.Votes.Count == open.Vote.Options.Count:
                CourseVote = open with { Standing = v };
                Changed?.Invoke();
                break;
            case CourseChosen c when CourseVote?.Vote.MatchId == c.MatchId:
                CourseChosen = c;
                Set("Draw");
                break;
            case ChooseMachine c:
                OnChooseMachine(c);
                break;
            case ChatMessage c:
                OnChat(c);
                break;
            case RecentNotices r when r.Notices.Count > 0:
                NoticesReplayed?.Invoke([.. r.Notices.Select(ServerNotice.Of)]);   // never chat lines
                break;
            case ErrorMessage { Code: "rename_too_soon" } e:
                OnRenameRefused(e);
                break;
            case ErrorMessage { Code: "bad_name" } e when PlayerId is not null:   // after the welcome: a rename's
                OnRenameRefused(e);
                break;
            case ErrorMessage e when ChatRefusal.Is(e.Code):
                OnChatRefused(e);
                break;
            case ErrorMessage e:
                LastError = e;
                LobbyRefusal = CustomLobbyRefusals.Contains(e.Code) ? e : LobbyRefusal;
                Set(e.Message, news: true);
                break;
            case Requeued r:
                OnRequeued(r);
                break;
            case QuickPlayEnded r:
                OnQuickPlayEnded(r);
                break;
            case RaceDesynced d:
                OnRaceDesynced(d);
                break;
            case MatchStart when Lobby is null || Race is not null:
                OnMatchStartRefused();
                break;
            case MatchStart s:
                OnMatchStart(s);
                break;
            case WatchStart w when Lobby is not null && Race is null && Watching:
                OnWatchStart(w);
                break;
        }
    }

    /// <summary>The server's welcome: its rules, and this player into Quick Play with the machine it allows.</summary>
    private void OnWelcome(Welcome w)
    {
        ServerName = w.ServerName;
        PlayerId = w.PlayerId;
        Tag = PlayerTags.IsValid(w.Tag) ? w.Tag : null;   // for this connection: nothing about tags is saved
        lock (_naming)
        {
            _renames = w.Renames;
        }
        _spectators = w.Spectators;
        Watching = false;   // (the server starts every visit racing)
        Rules = w.Rules;
        if (Custom)
        {
            OnCustomWelcome();
            return;
        }
        // the racer as it is now, custom machine included: the garage can change after the session was made
        var profile = Racer;
        Machine = QueueMachine(profile.Machine, w.Rules);
        Set(Machine == profile.Machine ? "Queueing" : $"Queueing · {Machines.Names[Machine.Value]}");
        var look = profile.LookOf(Machine.Value);
        Sent = new RacedMachine(Machine.Value, CustomMachine.Checked(profile.Custom));
        _ = Client.EnterQuickPlayAsync(Machine.Value, look.Color ?? profile.Color,
            w.Rules?.MachineSetting.Clamp(Math.Clamp(look.Setting, 0, 100)),
            look.StockColor, profile.Custom);
        ServerHasName(settings.Racer.Name.Trim());   // the hello's; a name saved since then goes now
    }

    /// <summary>The lobby as it is now.</summary>
    private void OnLobbyState(LobbyState l)
    {
        if (Lobby?.LobbyId != l.LobbyId)
        {
            Pings = NoPings;   // another lobby's players
        }
        if (Lobby is { } before && (before.LobbyId != l.LobbyId || before.StatPack?.Fingerprint != l.StatPack?.Fingerprint))
        {
            // another lobby, or this one on another pack or on none: the refused pack is no longer what the next
            // race runs on, so the sentence about it goes
            StatPackProblem = null;
        }
        Lobby = l;
        StartsAt = l.StartsInMs is { } ms ? DateTimeOffset.UtcNow.AddMilliseconds(ms) : null;
        if (l.Stage != LobbyState.Racing)
        {
            Live = null;
        }
        if (l.Stage == LobbyState.Forming)
        {
            // the last match's vote and choice are over: the next match starts with none until its own come
            CourseVote = null;
            CourseChosen = null;
            Choice = null;
        }
        Set(LobbyText(l, PlayerId, Build));
        ServerHasName(l.Players.FirstOrDefault(p => p.Id == PlayerId)?.Name);
    }

    /// <summary>Out of the lobby: back in the queue, or (custom lobbies) back to the browser, with why.</summary>
    private void OnLeftLobby(LeftLobby left)
    {
        LeftReason = Custom ? left.Reason : null;
        Lobby = null;
        StartsAt = null;
        StatPackProblem = null;
        Pings = NoPings;
        CourseVote = null;
        CourseChosen = null;
        Choice = null;
        Set(Custom ? CustomLobbiesText : "Queueing");
    }

    /// <summary>A match's course vote opens.</summary>
    private void OnCourseVote(CourseVote v)
    {
        CourseVote = new CourseVoteState(v, DateTimeOffset.UtcNow.AddMilliseconds(v.DurationMs), null);
        CourseChosen = null;
        Choice = null;
        Set("Course vote");
    }

    /// <summary>A match's machine choice opens, on the machine <see cref="DefaultChoice"/> names.</summary>
    private void OnChooseMachine(ChooseMachine c)
    {
        var profile = Racer;
        var start = DefaultChoice(profile.Machine, Machine, c.Machines);
        Choice = new MachineChoiceState(c, DateTimeOffset.UtcNow.AddMilliseconds(c.DurationMs), profile.LookOf(start), null);
        Set("Machines");
        // Only while nothing has been chosen: Set tells Changed's handlers at once, on this thread, and one of them may
        // have chosen already (a scripted visit presses there); the start sent after that would undo its pick.
        if (Choice is { Mine: null } open && open.Choose.MatchId == c.MatchId &&
            !new RacedMachine(start, CustomMachine.Checked(profile.Custom)).Same(Sent))
        {
            // the raced machine changed since the server was told (a Garage visit in the lobby): told now, unasked,
            // or the race would start on the old one (a choice sent marks the player ready, as a pick does)
            _ = ChooseMachineAsync(start);
        }
    }

    /// <summary>A chat line; the last 200 are kept. A server's notice is a chat line too, and is told
    /// (<see cref="NoticeReceived"/>); one this session has had (the server sends the last hour's again to a player
    /// who joins a lobby) is neither shown twice nor told again.</summary>
    private void OnChat(ChatMessage c)
    {
        var notice = ServerNotice.Of(c);
        lock (_chat)
        {
            if (notice?.Id is { } id && !_noticeIds.Add(id))
            {
                return;
            }
            if (notice is null && c.FromId == PlayerId)
            {
                OwnLineArrived();
            }
            _chat.Add(c);
            if (_chat.Count > 200)
            {
                _chat.RemoveAt(0);
            }
        }
        if (notice is not null)
        {
            NoticeReceived?.Invoke(notice);
        }
        Changed?.Invoke();
    }

    /// <summary>Left out of a forming match; still in the lobby.</summary>
    private void OnRequeued(Requeued r)
    {
        CourseVote = null;
        CourseChosen = null;
        Choice = null;
        Set(RequeuedText(r.Reason), news: true);
    }

    /// <summary>Taken out of Quick Play: said, and the player sent home.</summary>
    private void OnQuickPlayEnded(QuickPlayEnded r)
    {
        Lobby = null;
        StartsAt = null;
        StatPackProblem = null;
        CourseVote = null;
        CourseChosen = null;
        Choice = null;
        Set(RemovedText(r.Reason, r.Message));
        SentHome?.Invoke(Status);
    }

    /// <summary>This player's game desynced and the server took it out of the race.</summary>
    private void OnRaceDesynced(RaceDesynced d)
    {
        // out of the race (the server took the player out of it: no RaceOver to send when the game closes);
        // told before the status changes, so the screens refresh with the notice in place
        if (Race?.Start.MatchId == d.MatchId)
        {
            Race = null;
        }
        Desynced?.Invoke(DesyncedText);
        Set(DesyncedText, news: true);
    }

    /// <summary>A match start for a player in no lobby, or with a race still on: no game is started.</summary>
    private void OnMatchStartRefused()
    {
        // no game is started (StartsRefused); said once a connection, however many come
        if (StartsRefused++ == 0)
        {
            Refused?.Invoke(Lobby is null
                ? $"a match start from {ConnectedTo} for a player in no lobby was refused (no game was started)"
                : $"a match start from {ConnectedTo} while this player's race {Race!.Start.MatchId} was still on was refused (no second game was started)");
        }
    }

    /// <summary>A match starts: its files are written and the game is started.</summary>
    private void OnMatchStart(MatchStart s)
    {
        Choice = null;
        // A stat pack the game would refuse is never handed to it (the protocol's handbook/stat-packs.md): one that is no
        // pack, or holds a value outside the game's own ranges, and nothing narrower. No game is started; the player
        // is back in the lobby and reads why, and the seat is the game's AI's, as for any game that does not come.
        if (s.Setup.Stats is { } stats && StatPack.FromWire(stats).Refusal is { } why)
        {
            Refused?.Invoke($"the match start {s.MatchId} from {ConnectedTo} carries a stat pack this launcher does not take ({why}): no game was started");
            _ = Client.RaceOverAsync(s.MatchId);
            // (one word written: the status is this sentence from here on, until a race starts or the lobby is left)
            StatPackProblem = StatPackNotTaken + why;
            Set(StatPackProblem);
            return;
        }
        StatPackProblem = null;
        RejoinableRace = null;
        Race = Prepare(s);
        Set("Racing");
        startFork(Race);
    }

    /// <summary>A race's files: the match file (a spectator's says so) and, for a racer who records, the replay's path.</summary>
    private ForkRace Prepare(MatchStart start, bool spectate = false)
    {
        Directory.CreateDirectory(workFolder);
        var matchFile = Path.Combine(workFolder, "match.json");
        // (the input delay: the player's setting as it is now, 0, 1 or 2 and never more)
        var delay = (settings with { InputDelay = inputDelay?.Invoke() ?? settings.InputDelay }).RaceInputDelay;
        var options = new RaceOptions(Client.RelayHost(start.Relay), settings.OtherSounds, delay) { Spectate = spectate };
        MatchFile.Write(matchFile, start, options);
        if (spectate)
        {
            return new ForkRace(matchFile, null, start, Spectate: true);   // (a spectator records nothing)
        }
        string? replay = null;
        if (settings.RecordReplays)
        {
            var folder = settings.ReplayFolder ?? Path.Combine(workFolder, "replays");
            Directory.CreateDirectory(folder);
            replay = Path.Combine(folder, RecompLaunch.ReplayFileName(DateTimeOffset.Now, start.MatchId));
        }
        return new ForkRace(matchFile, replay, start);
    }
}
