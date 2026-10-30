using Online.Protocol;

namespace Launcher.Core;

// A visit to the custom lobbies: the browser's list, a lobby made or joined with the player's racer, and the host's
// requests. Such a visit never queues for Quick Play (no EnterQuickPlay): the racer goes with the create or the join.
public sealed partial class QuickPlaySession
{
    /// <summary>What the status says while the player is connected to the custom lobbies and in none.</summary>
    public const string CustomLobbiesText = "Custom lobbies";

    /// <summary>The refusals of a custom lobby's requests, said on the browser or in the lobby as a notice.</summary>
    public static IReadOnlySet<string> CustomLobbyRefusals { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CustomLobby.BadPassword, CustomLobby.NotHost, CustomLobby.NoSuchLobby, CustomLobby.LobbyFull, CustomLobby.LobbyClosed,
        CustomLobby.BadRules, CustomLobby.RulesLocked, CustomLobby.BadName, CustomLobby.BadMode,
    };

    /// <summary>A visit to the custom lobbies: after the welcome the player is in no lobby and not queued, and sees
    /// the browser's list; a lobby is made or joined from there.</summary>
    public bool Custom { get; init; }

    /// <summary>The custom lobbies the server lists for this player (while <see cref="Browsing"/>), as last sent.</summary>
    public IReadOnlyList<LobbySummary> Lobbies { get; private set; } = [];

    /// <summary>The server's Quick Play rules, whole, as the list carries them: what a lobby's rules are compared
    /// with. Null until a list names them (the rules' own defaults stand in: <see cref="HostRules"/>).</summary>
    public LobbyRules? QuickPlayRules { get; private set; }

    /// <summary>The browser shows: the server sends the list and its changes.</summary>
    public bool Browsing { get; private set; }

    /// <summary>Why the player is out of the last custom lobby they were in when it was not their doing
    /// (<see cref="CustomLobby.Kicked"/>, <see cref="CustomLobby.Closed"/>); null otherwise. Cleared by a create or a
    /// join.</summary>
    public string? LeftReason { get; private set; }

    /// <summary>The last refusal of a custom lobby's request (<see cref="CustomLobbyRefusals"/>); null since the last
    /// create, join or rules change was sent.</summary>
    public ErrorMessage? LobbyRefusal { get; private set; }

    /// <summary>The name of the custom lobby the player is in, as its state names it; "" in none.</summary>
    public string CustomName => Lobby is { Custom: true, Name: { } name } ? name : "";

    /// <summary>The custom lobby the player is in has a password, as its state says.</summary>
    public bool CustomLocked => Lobby is { Custom: true, Locked: true };

    /// <summary>In a custom lobby now.</summary>
    public bool InCustomLobby => Lobby is { Custom: true };

    /// <summary>This player is the host of the custom lobby they are in.</summary>
    public bool IsHost => Lobby is { Custom: true, HostId: { } host } && host == PlayerId;

    /// <summary>The welcome of a custom visit: the racer's machine as the server allows it, no queue; the list asked
    /// for when the browser already shows.</summary>
    private void OnCustomWelcome()
    {
        Machine = QueueMachine(Racer.Machine, Rules);
        Set(CustomLobbiesText);
        if (Browsing)
        {
            _ = Client.BrowseLobbiesAsync(true);
        }
        ServerHasName(settings.Racer.Name.Trim());
    }

    private void OnLobbyList(LobbyList list)
    {
        Lobbies = list.Lobbies;
        QuickPlayRules = list.QuickPlay ?? QuickPlayRules;
        Changed?.Invoke();
    }

    /// <summary>The browser shows (the list and its changes come) or no longer does.</summary>
    public Task BrowseAsync(bool on)
    {
        Browsing = on;
        return _client is not null && PlayerId is not null ? _client.BrowseLobbiesAsync(on) : Task.CompletedTask;
    }

    /// <summary>The last stat pack file of the host's own this session sent with a lobby's rules
    /// (<see cref="LobbyRules.PackFile"/>): the server names it in the lobby's rules alone, so the host's rules
    /// editor offers it again from here. Null: none sent.</summary>
    public HostPackFile? HostPack { get; private set; }

    private void RulesSent(LobbyRules rules)
    {
        if (rules.PackFile is { } text && rules.StatPack is { } name)
        {
            HostPack = new HostPackFile(name, text);
        }
    }

    /// <summary>A lobby of the player's, as its host, with these rules; a password when not empty.</summary>
    public Task CreateLobbyAsync(string name, LobbyRules rules, string? password)
    {
        ArgumentNullException.ThrowIfNull(rules);
        RulesSent(rules);
        var (machine, color, setting, stock, custom) = RacerToSend();
        Requested();
        return Client.CreateLobbyAsync(new CreateLobby(name, rules, string.IsNullOrEmpty(password) ? null : password,
            machine, color, setting, stock, custom));
    }

    /// <summary>Joins a lobby of the list (its password for a locked one).</summary>
    public Task JoinLobbyAsync(string lobbyId, string? password = null)
    {
        var (machine, color, setting, stock, custom) = RacerToSend();
        Requested();
        return Client.JoinLobbyAsync(new JoinLobby(lobbyId, string.IsNullOrEmpty(password) ? null : password,
            machine, color, setting, stock, custom));
    }

    /// <summary>The host's change: the name (null: the same), the rules whole, a new password (null: the same) or
    /// none.</summary>
    public Task SetLobbyRulesAsync(string? name, LobbyRules rules, string? password, bool clearPassword)
    {
        ArgumentNullException.ThrowIfNull(rules);
        LobbyRefusal = null;
        RulesSent(rules);
        return Client.SetLobbyRulesAsync(new SetLobbyRules(name, rules, string.IsNullOrEmpty(password) ? null : password, clearPassword));
    }

    public Task KickAsync(string playerId) => Client.KickPlayerAsync(playerId);

    public Task StartLobbyRaceAsync() => Client.StartLobbyRaceAsync();

    /// <summary>Out of the custom lobby, back to the browser (the connection stays).</summary>
    public Task LeaveLobbyAsync()
    {
        LeftReason = null;
        return Client.LeaveLobbyAsync();
    }

    private void Requested()
    {
        (LeftReason, LobbyRefusal) = (null, null);
        Changed?.Invoke();
    }

    /// <summary>The racer as a create or a join carries it, as the queue's would: the machine the server allows, its
    /// look, the player's own setting (the server brings it into the lobby's range, which a create or a join from the
    /// browser does not know yet); and kept as what the server was told.</summary>
    private (int Machine, Rgb Color, int? Setting, int? StockColor, CustomMachine? Custom) RacerToSend()
    {
        var profile = Racer;
        Machine = QueueMachine(profile.Machine, Rules);
        var look = profile.LookOf(Machine.Value);
        Sent = new RacedMachine(Machine.Value, CustomMachine.Checked(profile.Custom));
        return (Machine.Value, look.Color ?? profile.Color, Math.Clamp(look.Setting, 0, 100),
            look.StockColor, profile.Custom);
    }
}
