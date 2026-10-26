using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The stand-in service's lobby (<see cref="StandInService"/>), played for one player: stand-in racers who chat,
/// vote to start, vote for a course and choose their machines, on a short clock, as a coordinator's lobby goes
/// (forming, starting with the course vote and its draw, choosing). Where the race would start the stand-in has no
/// race server: the lobby says so and forms again. Quick Play's lobby has the five stand-in racers; a custom lobby
/// (<paramref name="room"/>) has the room's members and host, its rules, and the host's requests (rules, kick,
/// start). What it sends goes through <c>send</c>; its timers end with <c>stop</c>. One lock over its state: the
/// launcher's calls and the timers come from any thread.
/// </summary>
internal sealed class StandInLobby(string name, Action<ServerMessage> send, CancellationToken stop, StandInRoom? room = null)
{
    public const string LobbyId = "stand-in-lobby";

    public const int Capacity = 30;

    public const string You = "stand-in-you";

    private const string YourTag = "DEV2";

    /// <summary>How long the course vote and the machine stage last on the stand-in (a real server's are longer).</summary>
    private const int VoteMs = 12_000, ChooseMs = 15_000, DrawMs = 4_000;

    /// <summary>The stand-in racers.</summary>
    public static IReadOnlyList<PlayerInfo> Others { get; } =
    [
        new("stand-in-1", "MIRA", 0, new Rgb(232, 67, 46), Tag: "M7RA"),
        new("stand-in-2", "JODY", 5, new Rgb(46, 160, 232), Tag: "J9DY"),
        new("stand-in-3", "ZOE", 9, new Rgb(250, 210, 40), Tag: "Z2E3"),
        new("stand-in-4", "SAM", 13, new Rgb(120, 220, 90), Tag: "S4M5"),
        new("stand-in-5", "PICO", 20, new Rgb(200, 90, 230), Tag: "P6C7"),
    ];

    private static readonly string[] Lines = ["gl everyone", "who's up for Big Blue?", "one more", "nice draw", "gg"];

    private readonly Lock _gate = new();
    private readonly List<PlayerInfo> _others = room?.Members ?? [.. Others];
    private readonly string _id = room?.Id ?? LobbyId;
    private string _name = name;
    private PlayerInfo? _me;   // in the lobby once queued
    private string _stage = LobbyState.Forming;

    /// <summary>The lobby's stage now, for the browser's list.</summary>
    public string Stage
    {
        get
        {
            lock (_gate)
            {
                return _stage;
            }
        }
    }

    /// <summary>The player is in this lobby.</summary>
    public bool Holds
    {
        get
        {
            lock (_gate)
            {
                return _me is not null;
            }
        }
    }
    private bool _votedStart, _votedCpu;
    private int _othersVotedStart, _match;
    private IReadOnlyList<int> _options = [];
    private int[] _votes = [];
    private readonly HashSet<string> _voted = [];
    private readonly HashSet<string> _chosen = [];
    private int _line;

    private string MatchId => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"stand-in-{_match}");

    private static readonly ServerRules Rules = new(3, Capacity, CpuFillMode.Vote, CourseSelectMode.Vote, null,
        [.. Enumerable.Range(0, Machines.Names.Count)], SettingRange.Free, new RaceRules());

    /// <summary>The hello taken: the welcome, and the notice that this is the stand-in.</summary>
    public void Welcome()
    {
        send(new Welcome(You, StandInService.Name, ControlProtocol.Version, Rules, Renames: true, Tag: YourTag));
        send(new ChatMessage(ControlProtocol.GlobalChannel, Notices.ServerId, Notices.SenderName,
            "This is the stand-in service (dev mode): lobbies, chat and votes work, races do not start.",
            DateTimeOffset.UtcNow, Notices.Info, "stand-in-welcome"));
    }

    public void Queue(int machine, Rgb color, int? stockColor)
    {
        lock (_gate)
        {
            _me = new PlayerInfo(You, _name, machine, color, stockColor, Tag: YourTag);
            SendState();
            send(new LobbyPings(_id, [new PlayerPing(You, 12), .. _others.Select((p, i) => new PlayerPing(p.Id, 30 + (i * 17)))]));
        }
        Later(1_500, () => SayFirst(0));
    }

    /// <summary>The player leaves (<paramref name="reason"/>: the host removed them, or the lobby closed). A host
    /// who leaves hands the lobby to the member who came first.</summary>
    public void Leave(string? reason = null)
    {
        lock (_gate)
        {
            _me = null;
            _stage = LobbyState.Forming;
            if (room is not null && room.HostId == You && _others.Count > 0)
            {
                (room.HostId, room.HostName) = (_others[0].Id, _others[0].Name);
            }
            send(new LeftLobby(_id, reason));
        }
    }

    /// <summary>A stand-in comes into the lobby (a custom lobby's guests arrive one after another).</summary>
    public void Arrive(PlayerInfo who)
    {
        lock (_gate)
        {
            if (_others.All(p => p.Id != who.Id) && Players.Count < (room?.Capacity ?? Capacity))
            {
                _others.Add(who);
                SendState();
            }
        }
    }

    /// <summary>The host removes a member: nothing for another's request, or for a member who is not there.</summary>
    public bool Kick(string playerId)
    {
        lock (_gate)
        {
            if (room is null || room.HostId != You || _others.RemoveAll(p => p.Id == playerId) == 0)
            {
                return false;
            }
            SendState();
            return true;
        }
    }

    /// <summary>The host starts the next race now: the course vote opens, as when the start vote passes.</summary>
    public bool StartRace()
    {
        lock (_gate)
        {
            if (room is null || room.HostId != You || _stage != LobbyState.Forming || _me is null)
            {
                return false;
            }
            StartMatch();
            return true;
        }
    }

    /// <summary>The host's new name, rules and password, while the lobby forms; a refusal's code otherwise.</summary>
    public string? SetRules(SetLobbyRules change)
    {
        lock (_gate)
        {
            if (room is null || room.HostId != You)
            {
                return CustomLobby.NotHost;
            }
            if (_stage != LobbyState.Forming)
            {
                return CustomLobby.RulesLocked;
            }
            room.Name = change.Name is { Length: > 0 } named ? named.Trim() : room.Name;
            room.Rules = StandInRooms.Whole(change.Rules);
            room.Password = change.ClearPassword ? null : change.Password ?? room.Password;
            SendState();
            return null;
        }
    }

    public void Rename(string name)
    {
        lock (_gate)
        {
            _name = name;
            if (_me is { } me)
            {
                _me = me with { Name = name };
                SendState();
            }
        }
    }

    public void Chat(string channel, string text)
    {
        send(new ChatMessage(channel, You, _name, text, DateTimeOffset.UtcNow, FromTag: YourTag));
        var at = _line + 1;
        Later(1_200, () => SayFirst(at, Lines[_line++ % Lines.Length], channel));
    }

    public void VoteCpuFill(bool yes)
    {
        lock (_gate)
        {
            _votedCpu = yes;
            SendState();
        }
    }

    /// <summary>The start vote: the stand-ins follow the player's yes, one after another, and the match forms when
    /// more than half have voted.</summary>
    public void VoteStart(bool yes)
    {
        lock (_gate)
        {
            if (_stage != LobbyState.Forming || _me is null)
            {
                return;
            }
            _votedStart = yes;
            SendState();
        }
        for (var i = 1; yes && i <= 3; i++)
        {
            Later(i * 700, () =>
            {
                lock (_gate)
                {
                    if (_stage != LobbyState.Forming || !_votedStart)
                    {
                        return;
                    }
                    _othersVotedStart = Math.Min(_othersVotedStart + 1, _others.Count);
                    SendState();
                    if (StartVotes * 2 > Players.Count)
                    {
                        StartMatch();
                    }
                }
            });
        }
    }

    /// <summary>(Under the lock.) The match forms: the course vote, five courses of the pool and Random course.</summary>
    private void StartMatch()
    {
        _match++;
        _stage = LobbyState.Starting;
        _options = [.. Courses.Pool.OrderBy(_ => Random.Shared.Next()).Take(5), CourseVote.RandomCourse];
        _votes = new int[_options.Count];
        _voted.Clear();
        _chosen.Clear();
        SendState();
        send(new CourseVote(MatchId, _options, VoteMs));
        var match = _match;
        foreach (var (other, i) in _others.ToList().Select((p, i) => (p, i)))
        {
            Later(1_000 + (i * 1_500), () =>
            {
                lock (_gate)
                {
                    if (match == _match && _stage == LobbyState.Starting)
                    {
                        Vote(other.Id, Random.Shared.Next(_options.Count));
                    }
                }
            });
        }
        Later(VoteMs, () => Draw(match));
    }

    public void VoteCourse(string matchId, int option)
    {
        lock (_gate)
        {
            if (matchId == MatchId && _stage == LobbyState.Starting && option >= 0 && option < _options.Count && !_voted.Contains(You))
            {
                Vote(You, option);
            }
        }
    }

    private void Vote(string who, int option)
    {
        if (_voted.Add(who))
        {
            _votes[option]++;
            send(new CourseVotes(MatchId, [.. _votes], [.. _voted]));
        }
    }

    /// <summary>The vote's draw (weighted by the votes), then the machine stage on the course drawn.</summary>
    private void Draw(int match)
    {
        int course;
        lock (_gate)
        {
            if (match != _match || _stage != LobbyState.Starting)
            {
                return;
            }
            var total = _votes.Sum();
            var winner = 0;
            if (total == 0)
            {
                winner = Random.Shared.Next(_options.Count);   // nobody voted: every option alike
            }
            else
            {
                for (var pick = Random.Shared.Next(total); winner < _votes.Length - 1; winner++)
                {
                    pick -= _votes[winner];
                    if (pick < 0)
                    {
                        break;
                    }
                }
            }
            course = _options[winner] == CourseVote.RandomCourse ? Courses.Pool[Random.Shared.Next(Courses.Pool.Count)] : _options[winner];
            send(new CourseChosen(MatchId, _options, [.. _votes], winner, course));
        }
        Later(DrawMs, () =>
        {
            lock (_gate)
            {
                if (match != _match || _stage != LobbyState.Starting)
                {
                    return;
                }
                _stage = LobbyState.Choosing;
                SendState();
                send(new ChooseMachine(MatchId, course, Rules.AllowedMachines, ChooseMs));
            }
            foreach (var (other, i) in _others.ToList().Select((p, i) => (p, i)))
            {
                Later(1_500 + (i * 1_200), () =>
                {
                    lock (_gate)
                    {
                        if (match == _match && _stage == LobbyState.Choosing && _chosen.Add(other.Id))
                        {
                            SendState();
                            EndWhenAllChose();
                        }
                    }
                });
            }
            Later(ChooseMs, () =>
            {
                lock (_gate)
                {
                    if (match == _match && _stage == LobbyState.Choosing)
                    {
                        NoRace();
                    }
                }
            });
        });
    }

    public void Choose(string matchId, int machine, Rgb color, int? stockColor)
    {
        lock (_gate)
        {
            if (matchId != MatchId || _stage != LobbyState.Choosing || _me is null)
            {
                return;
            }
            _me = _me with { Machine = machine, Color = color, StockColor = stockColor };
            _chosen.Add(You);
            SendState();
            EndWhenAllChose();
        }
    }

    /// <summary>(Under the lock.) Everybody has chosen: where the race would start.</summary>
    private void EndWhenAllChose()
    {
        if (_chosen.Count >= Players.Count)
        {
            NoRace();
        }
    }

    /// <summary>(Under the lock.) No race server: the lobby forms again, and the player reads why.</summary>
    private void NoRace()
    {
        _stage = LobbyState.Forming;
        (_votedStart, _othersVotedStart) = (false, 0);
        _chosen.Clear();
        SendState();
        send(new ErrorMessage("stand_in", StandInService.NoRacesText));
        Later(1_000, () => SayFirst(4, Lines[4]));
    }

    private int StartVotes => (_votedStart ? 1 : 0) + _othersVotedStart;

    private List<PlayerInfo> Players => [.. _me is { } me ? [me] : Array.Empty<PlayerInfo>(), .. _others];

    /// <summary>(Under the lock.) The lobby as it is now, to the player.</summary>
    private void SendState()
    {
        if (_me is null)
        {
            return;
        }
        var choosing = _stage == LobbyState.Choosing;
        var players = Players.Select(p => p with { Chosen = choosing && _chosen.Contains(p.Id) }).ToList();
        send(new LobbyState(_id, players, room?.Capacity ?? Capacity, VotingMinimum: 2, VotingOpen: _stage == LobbyState.Forming,
            StartVotes, _votedCpu ? 1 : 0, _stage, Custom: room is not null, HostId: room?.HostId, Rules: room?.Rules,
            Name: room?.Name, Locked: room?.Password is not null));
    }

    /// <summary>A line from the stand-in at this place among the lobby's others (round the list), when there is one.</summary>
    private void SayFirst(int at, string? text = null, string channel = ControlProtocol.LobbyChannel)
    {
        PlayerInfo? who;
        lock (_gate)
        {
            // (none once the player has left: a timer of the lobby they were in says nothing to the next)
            who = _me is not null && _others.Count > 0 ? _others[at % _others.Count] : null;
        }
        if (who is not null)
        {
            Say(who, text ?? Lines[0], channel);
        }
    }

    private void Say(PlayerInfo who, string text, string channel = ControlProtocol.LobbyChannel) =>
        send(new ChatMessage(channel, who.Id, who.Name, text, DateTimeOffset.UtcNow, FromTag: who.Tag));

    /// <summary>Does something after a while, unless the connection ended meanwhile.</summary>
    private void Later(int ms, Action act) => _ = Task.Delay(ms, stop).ContinueWith(t =>
    {
        if (!t.IsCanceled)
        {
            act();
        }
    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
}
