using Online.Protocol;

namespace Launcher.Core;

/// <summary>A custom lobby on the stand-in service: its name, host, rules (whole, as a server sends them),
/// password, room and stand-in members. Changed only under its lobby's lock or the browser's.</summary>
internal sealed class StandInRoom(string id, string name, string hostId, string hostName, LobbyRules rules, string? password,
    List<PlayerInfo> members, string stage = LobbyState.Forming)
{
    public string Id { get; } = id;

    public string Name { get; set; } = name;

    public string HostId { get; set; } = hostId;

    public string HostName { get; set; } = hostName;

    public LobbyRules Rules { get; set; } = rules;

    public string? Password { get; set; } = password;

    public List<PlayerInfo> Members { get; } = members;

    /// <summary>The stage the list shows while the player is not in the lobby.</summary>
    public string Stage { get; } = stage;

    /// <summary>The racers rule: the lobby's room.</summary>
    public int Capacity => Rules.Value(HostRules.Racers) ?? StandInLobby.Capacity;

    /// <summary>The player's lobby here while they are in it.</summary>
    public StandInLobby? Lobby { get; set; }

}

/// <summary>
/// The stand-in service's custom lobbies (<see cref="StandInService"/>): a browser's list of a few made-up lobbies
/// (one locked, its password "gx"; one racing), the player's own once they make one, and the refusals a server gives
/// (a wrong password, a full or closed lobby). A lobby made by the player fills with two stand-ins, one after
/// another. What it sends goes through <c>send</c>; one lock over the list.
/// </summary>
internal sealed class StandInRooms(string name, Action<ServerMessage> send, CancellationToken stop)
{
    /// <summary>The made-up lobbies' password.</summary>
    public const string Password = "gx";

    /// <summary>The stand-in's Quick Play rules, whole (the stand-in lobby's clock: a 12 s vote, a 15 s machine
    /// choice, 2 players to start).</summary>
    public static LobbyRules QuickPlay { get; } = new(LobbyRules.Settable.Where(r => r.Kind != RuleKind.Pack).ToDictionary(r => r.Name, r => r.Name switch
    {
        "vote-seconds" => 12,
        "choose-seconds" => 15,
        "voting-minimum" => 2,
        _ => r.Default,
    }).Append(new KeyValuePair<string, int>(LobbyRules.SettingHighKey, HostRules.MachineSetting.Default))
        .ToDictionary(p => p.Key, p => p.Value));

    /// <summary>Rules whole: the host's values over Quick Play's.</summary>
    public static LobbyRules Whole(LobbyRules rules)
    {
        var values = QuickPlay.Values.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, value) in rules?.Values ?? new Dictionary<string, int>())
        {
            values[key] = value;
        }
        return new LobbyRules(values, rules?.StatPack);
    }

    private static List<PlayerInfo> Of(params int[] others) => [.. others.Select(i => StandInLobby.Others[i])];

    private readonly Lock _gate = new();
    private readonly List<StandInRoom> _rooms =
    [
        new("stand-in-room-1", "Big Blue nights", "stand-in-2", "JODY", Whole(new(new Dictionary<string, int> { ["laps"] = 5 })), null, Of(1, 0, 3)),
        new("stand-in-room-2", "Friday cup", "stand-in-3", "ZOE",
            Whole(new(new Dictionary<string, int> { ["racers"] = 12, ["latency-limit"] = 150, ["cpu-fill"] = (int)CpuFillMode.Never })),
            Password, Of(2, 4)),
        new("stand-in-room-3", "Mines stay gone", "stand-in-5", "PICO",
            Whole(new(new Dictionary<string, int> { ["mine-regen"] = 0, ["boost-start"] = 1 })), null, Of(4, 1, 2, 3), LobbyState.Racing),
    ];

    private bool _browsing;
    private int _made;

    /// <summary>The lobby the player is in (null: none).</summary>
    public StandInLobby? Current { get; private set; }

    public void Browse(bool on)
    {
        lock (_gate)
        {
            _browsing = on;
        }
        Changed();
    }

    /// <summary>The list again to a browsing player: something in it changed.</summary>
    public void Changed()
    {
        // (the rooms taken under the list's lock, their lobbies read after it: a lobby tells of itself under its own)
        List<StandInRoom> rooms;
        lock (_gate)
        {
            if (!_browsing)
            {
                return;
            }
            rooms = [.. _rooms];
        }
        send(new LobbyList([.. rooms.Select(Summary)], QuickPlay));
    }

    private static LobbySummary Summary(StandInRoom r)
    {
        var inside = r.Lobby is { Holds: true };
        return new LobbySummary(r.Id, r.Name, r.HostName, r.Members.Count + (inside ? 1 : 0), r.Capacity, r.Password is not null,
            r.Rules, r.Lobby?.Stage ?? r.Stage);
    }

    public void Create(CreateLobby request, string playerName)
    {
        LeaveCurrent();
        StandInRoom room;
        lock (_gate)
        {
            _made++;
            room = new StandInRoom(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"stand-in-mine-{_made}"),
                request.Name.Trim(), StandInLobby.You, playerName, Whole(request.Rules), string.IsNullOrEmpty(request.Password) ? null : request.Password, []);
            _rooms.Add(room);
        }
        Enter(room, request.Machine, request.Color, request.StockColor);
        // two stand-ins come in, one after another
        Later(1_500, () => room.Lobby?.Arrive(StandInLobby.Others[0]));
        Later(3_000, () => room.Lobby?.Arrive(StandInLobby.Others[1]));
    }

    public void Join(JoinLobby request)
    {
        StandInRoom? room;
        string? refused;
        lock (_gate)
        {
            room = _rooms.FirstOrDefault(r => r.Id == request.LobbyId);
            refused = room is null ? CustomLobby.NoSuchLobby
                : room.Lobby?.Stage is LobbyState.Starting or LobbyState.Choosing ? CustomLobby.LobbyClosed
                : room.Password is { } password && password != request.Password ? CustomLobby.BadPassword
                : room.Members.Count + 1 > room.Capacity ? CustomLobby.LobbyFull
                : null;
        }
        if (refused is not null)
        {
            send(new ErrorMessage(refused, refused switch
            {
                CustomLobby.BadPassword => "Wrong password",
                CustomLobby.LobbyFull => "Lobby full",
                CustomLobby.LobbyClosed => "Lobby closed",
                _ => "No such lobby",
            }));
            return;
        }
        LeaveCurrent();
        Enter(room!, request.Machine, request.Color, request.StockColor);
    }

    private void Enter(StandInRoom room, int machine, Rgb color, int? stockColor)
    {
        var lobby = new StandInLobby(name, Send, stop, room);
        lock (_gate)
        {
            room.Lobby = lobby;
            Current = lobby;
        }
        lobby.Queue(machine, color, stockColor);
        Changed();
    }

    /// <summary>The player out of the lobby they are in (LeaveLobby, or a create or join elsewhere).</summary>
    public void LeaveCurrent()
    {
        StandInLobby? current;
        lock (_gate)
        {
            current = Current;
            Current = null;
        }
        current?.Leave();
        Changed();
    }

    public void Kick(string playerId)
    {
        if (Current?.Kick(playerId) == false)
        {
            send(new ErrorMessage(CustomLobby.NotHost, "Only the host can do that"));
        }
        Changed();
    }

    public void Start()
    {
        if (Current?.StartRace() == false)
        {
            send(new ErrorMessage(CustomLobby.NotHost, "Only the host can do that"));
        }
    }

    public void SetRules(SetLobbyRules change)
    {
        if (Current?.SetRules(change) is { } refused)
        {
            send(new ErrorMessage(refused, refused == CustomLobby.RulesLocked ? "Rules can change only while the lobby waits" : "Only the host can do that"));
        }
        Changed();
    }

    // a lobby's word to the player; its member count shows in the list too
    private void Send(ServerMessage message)
    {
        send(message);
        if (message is LobbyState)
        {
            Changed();
        }
    }

    private void Later(int ms, Action act) => _ = Task.Delay(ms, stop).ContinueWith(t =>
    {
        if (!t.IsCanceled)
        {
            act();
        }
    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
}
