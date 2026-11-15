using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>One server of Quick Play's list: its name, its address, what it says of itself (racers online, open
/// lobbies) and how quickly it answers.</summary>
public sealed partial class ServerRow : ObservableObject, IDisposable
{
    /// <param name="removable">One of the player's own that can come off the list (not the official server, nor
    /// the stand-in in dev mode).</param>
    public ServerRow(string address, string name, bool official, ServerStatusFeed feed, bool removable = true)
    {
        ArgumentNullException.ThrowIfNull(feed);
        Address = address;
        Name = name;
        Official = official;
        Feed = feed;
        Own = removable && !official;
    }

    public string Address { get; }

    /// <summary>The server's name: this build's official server by its label, one of the player's by what it calls
    /// itself once it has answered (its host until then).</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>This build's official server (not one the player added: it cannot be removed).</summary>
    public bool Official { get; }

    /// <summary>One of the player's own: "Remove" takes it off the list.</summary>
    public bool Own { get; }

    internal ServerStatusFeed Feed { get; }

    /// <summary>Under the name: its racers and lobbies, or that it is being asked, or that it does not answer.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "Checking…";

    /// <summary>How quickly it answered: "31 ms"; "" before an answer.</summary>
    [ObservableProperty]
    public partial string Ping { get; set; } = "";

    /// <summary>The ping as a mark: "good" (under 80 ms), "fair" (under 160), "poor"; "" before an answer.</summary>
    [ObservableProperty]
    public partial string Quality { get; set; } = "";

    /// <summary>Beside the name: "Best connection", or "Last used"; "" neither.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTag))]
    public partial string Tag { get; set; } = "";

    public bool HasTag => Tag.Length > 0;

    /// <summary>The last answer's round trip; null: none yet, or it stopped answering.</summary>
    public TimeSpan? Latency { get; private set; }

    /// <summary>The feed's news, as the row says it.</summary>
    internal void Show()
    {
        var status = Feed.Latest;
        Latency = Feed.Failed ? null : Feed.Latency;
        if (status is null)
        {
            (Line, Ping, Quality) = (Feed.Failed ? "No answer" : "Checking…", "", "");
            return;
        }
        if (!Official && status.Name is { Length: > 0 } called)
        {
            Name = called;
        }
        Line = Words(status) + (Feed.Failed ? " · no answer now" : "");
        if (Latency is { } took)
        {
            var ms = Math.Max(1, (int)Math.Round(took.TotalMilliseconds));
            Ping = ms.ToString(CultureInfo.InvariantCulture) + " ms";
            Quality = ms < 80 ? "good" : ms < 160 ? "fair" : "poor";
        }
        else
        {
            (Ping, Quality) = ("", "");
        }
    }

    /// <summary>"46 online · 3 lobbies".</summary>
    public static string Words(ServiceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var online = status.PlayersOnline;
        var lobbies = status.Lobbies.Count;
        return $"{online} online · {lobbies} {(lobbies == 1 ? "lobby" : "lobbies")}";
    }

    public void Dispose() => Feed.Dispose();
}

/// <summary>
/// The redesigned interface's "Choose a server" (Quick Play's first page): a row each for this build's official
/// server and for the servers the player added (<see cref="LauncherSettings.Servers"/>), with their racers, lobbies
/// and ping. A press opens the server's panel: "Join a race" sets that server (as Settings' Server box would) and joins
/// Quick Play there (its fullest open lobby; none open: "No race found"); "Host a lobby" a Quick Play lobby there
/// (Quick Play's queue, never a custom lobby); "See its lobbies", the server's lobbies page. Each server is asked of its state only
/// while the page shows (<see cref="Watching"/>), and only servers the player chose are ever asked: the official one
/// that every launcher of this build is made for, and the ones they added themselves.
/// </summary>
public sealed partial class ServersViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly HttpMessageHandler? _answers;
    private readonly Func<Task> _race;
    private bool _watching;

    /// <param name="answers">How the servers are asked (the stand-in's answers in dev mode, a test's own); null: the
    /// network.</param>
    /// <param name="race">Joins Quick Play on the server set (RACE).</param>
    public ServersViewModel(AppState state, HttpMessageHandler? answers, Func<Task> race)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(race);
        _state = state;
        _answers = answers;
        _race = race;
        Build();
        state.Changed += () => OnUi(Mark);
    }

    public ObservableCollection<ServerRow> Rows { get; } = [];

    /// <summary>The page shows: its servers are asked every few seconds; else nobody is.</summary>
    public bool Watching
    {
        get => _watching;
        set
        {
            _watching = value;
            foreach (var row in Rows)
            {
                row.Feed.Watching = value;
                if (value)
                {
                    row.Feed.Start();
                    _ = row.Feed.AskAsync();
                }
            }
        }
    }

    /// <summary>The server set now (the one Quick Play joins), by its row's name; "" none.</summary>
    [ObservableProperty]
    public partial string CurrentName { get; set; } = "";

    /// <summary>"Add a server…" is open: the address box and its buttons show.</summary>
    [ObservableProperty]
    public partial bool Adding { get; set; }

    [ObservableProperty]
    public partial string Draft { get; set; } = "";

    /// <summary>Why the address typed was not added; "" none.</summary>
    [ObservableProperty]
    public partial string AddProblem { get; set; } = "";

    /// <summary>The row the server set has (the one the pad lands on); null: none on the list.</summary>
    public ServerRow? Current => Rows.FirstOrDefault(r => r.Address == _state.Settings.QuickPlayServer);

    /// <summary>A server pressed: its next step, the ways onto it in a panel (<see cref="ServerSheetViewModel"/>: join a
    /// race, host a lobby); nothing is joined by the press alone. Without a place for the panel: Quick Play there.</summary>
    [RelayCommand]
    private Task Join(ServerRow row)
    {
        if (ShowSheet is { } show)
        {
            show(new ServerSheetViewModel(row, this));
            return Task.CompletedTask;
        }
        return JoinRace(row);
    }

    /// <summary>Where the page's panels show (the shell's); null: none.</summary>
    public Action<ViewModelBase>? ShowSheet { get; set; }

    /// <summary>A Quick Play lobby hosted on the server set (the shell's way there: Quick Play's queue); null: none.</summary>
    public Func<Task>? Hosting { get; set; }

    /// <summary>The lobbies of the server set (the shell's way there); null: none.</summary>
    public Action? Browsing { get; set; }

    /// <summary>"See its lobbies": the server set, and the lobbies people are in there (each joined from the list, or
    /// RACE).</summary>
    public void LobbiesOn(ServerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Choose(row);
        Browsing?.Invoke();
    }

    /// <summary>"Join a race": the server set, and Quick Play joined there: an open Quick Play lobby, else "No race
    /// found" and nothing joined (<see cref="PlayControlViewModel.NoOpenLobby"/>).</summary>
    public Task JoinRace(ServerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Choose(row);
        return _race();
    }

    /// <summary>"Host a lobby": the server set, and a Quick Play lobby there (Quick Play's queue, which opens a new
    /// one when none has room: <see cref="PlayControlViewModel.HostAsync"/>), never a custom lobby (the owner,
    /// 2026-11-15: the two are kept apart; custom lobbies are the hub's Custom lobby).</summary>
    public Task HostOn(ServerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Choose(row);
        return Hosting?.Invoke() ?? Task.CompletedTask;
    }

    [RelayCommand]
    private void Add() => (Adding, Draft, AddProblem) = (true, "", "");

    [RelayCommand]
    private void CancelAdd() => (Adding, AddProblem) = (false, "");

    /// <summary>The address typed, added to the list (and kept in the settings).</summary>
    [RelayCommand]
    private void ConfirmAdd()
    {
        var address = Draft.Trim();
        if (!SettingsCheck.IsServer(address))
        {
            AddProblem = "That is no server address. It looks like wss://example.org/ws";
            return;
        }
        if (Rows.Any(r => r.Address == address))
        {
            AddProblem = "That server is on the list already.";
            return;
        }
        if ((_state.Settings.Servers?.Count ?? 0) >= LauncherSettings.MostServers)
        {
            AddProblem = $"The list holds {LauncherSettings.MostServers} servers of your own. Remove one first.";
            return;
        }
        _state.Update(s => s with { Servers = [.. s.Servers ?? [], address] });
        var row = RowOf(address, official: false);
        Rows.Add(row);
        (Adding, Draft, AddProblem) = (false, "", "");
        Mark();
    }

    /// <summary>One of the player's own servers off the list. When it was the server set, the official one is set
    /// (or none, on a build without one).</summary>
    [RelayCommand]
    private void Remove(ServerRow row)
    {
        if (!row.Own)
        {
            return;
        }
        _state.Update(s =>
        {
            var kept = (s.Servers ?? []).Where(a => a != row.Address).ToList();
            var set = s.Server?.Trim() == row.Address;
            return s with
            {
                Servers = kept.Count > 0 ? kept : null,
                Server = set ? null : s.Server,
                NoServer = set ? LauncherSettings.OnlineServer is null : s.NoServer,
            };
        });
        Rows.Remove(row);
        row.Dispose();
        Mark();
    }

    /// <summary>The row with the quickest answer; of equals the first; null when none has answered.</summary>
    public ServerRow? Best() => Rows.Where(r => r.Latency is not null).OrderBy(r => r.Latency).FirstOrDefault();

    /// <summary>The server set, as Settings' Server box sets it: the official one by its button, another by its address.</summary>
    private void Choose(ServerRow row)
    {
        if (_state.StandIn || _state.Settings.QuickPlayServer == row.Address)
        {
            return;   // (in dev mode the stand-in is the only server: nothing to set)
        }
        _state.Update(s => row.Official
            ? ServerChoice.After(s, null, ServerAct.Official)
            : s with { Server = row.Address, NoServer = false });
    }

    /// <summary>The list: the official server (not in dev mode, where the stand-in stands for every server), the
    /// server set when it is none of the others, and the player's own.</summary>
    private void Build()
    {
        var s = _state.Settings;
        var addresses = new List<(string Address, bool Official)>();
        if (!_state.StandIn && LauncherSettings.OnlineServer is { } official)
        {
            addresses.Add((official, true));
        }
        if (s.QuickPlayServer is { } set && addresses.TrueForAll(a => a.Address != set))
        {
            addresses.Add((set, false));
        }
        foreach (var own in s.Servers ?? [])
        {
            if (addresses.TrueForAll(a => a.Address != own))
            {
                addresses.Add((own, false));
            }
        }
        foreach (var (address, isOfficial) in addresses)
        {
            Rows.Add(RowOf(address, isOfficial));
        }
        Mark();
    }

    private ServerRow RowOf(string address, bool official)
    {
        var name = _state.StandIn && _state.StandInName is { } standIn ? standIn
            : official ? StackStamp.Current.ServerLabel : ServerHealth.HostOf(address);
        var feed = new ServerStatusFeed(() => address, _answers);
        var row = new ServerRow(address, name, official, feed, removable: !_state.StandIn);
        feed.Changed += () => OnUi(() =>
        {
            row.Show();
            Mark();
        });
        if (_watching)
        {
            feed.Watching = true;
            feed.Start();
        }
        return row;
    }

    /// <summary>The tags: the quickest answer's "Best connection", the server set's "Last used".</summary>
    private void Mark()
    {
        var best = Best();
        var current = Current;
        foreach (var row in Rows)
        {
            row.Tag = row == best ? "Best connection" : row == current ? "Last used" : "";
        }
        CurrentName = current?.Name ?? "";
        OnPropertyChanged(nameof(Current));
    }

    public void Dispose()
    {
        foreach (var row in Rows)
        {
            row.Dispose();
        }
    }
}
