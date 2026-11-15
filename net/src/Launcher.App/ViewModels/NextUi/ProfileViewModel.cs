using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>One of the profile's recent races: the place, the course, when; "Watch" plays it.</summary>
public sealed record RecentRace(ReplayItem Item)
{
    public string Place => Item.Place;

    public bool Won => Item.Place == ReplayWords.Ordinal(1);

    public bool Out => Item.PlaceOut;

    public string Track => Item.Venue.Length > 0 ? $"{Item.Venue} · {Item.Track}" : Item.Track;

    public string When => ReplayDay.Of(Item);
}

/// <summary>
/// The profile (the top bar's chip; new, the classic launcher has only the name in its left column and the dot): the
/// name as the game shows it, whether the player is online, in a lobby or has no server, which server; "Change name"
/// (a panel), "Server" (the servers to race on); the account, which comes later; the machine raced (to the Garage);
/// three figures of the online races (to Stats); the last three races, each to watch. Everything is the classic
/// launcher's: the name and the dot (<see cref="AccountViewModel"/>), the figures (<see cref="StatsViewModel"/>), the
/// replays (<see cref="ReplaysViewModel"/>).
/// </summary>
public sealed partial class ProfileViewModel : ViewModelBase, IDisposable
{
    private const int RecentCount = 3;

    private readonly MainViewModel _main;
    private readonly ShellViewModel _shell;
    private readonly AppState _state;

    public ProfileViewModel(MainViewModel main, ShellViewModel shell, AppState state)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(state);
        (_main, _shell, _state) = (main, shell, state);
        main.Account.PropertyChanged += OnAccount;
        main.Stats.Tiles.CollectionChanged += OnTiles;
        main.Replays.PropertyChanged += OnReplays;
        state.Changed += OnState;
        ShowFigures();
        ShowRecent();
    }

    public AccountViewModel Account => _main.Account;

    public MainViewModel Main => _main;

    /// <summary>"In races it reads ANA".</summary>
    public string InRaces => $"In races it reads {Account.Name}";

    /// <summary>Online, Offline, In a lobby, No server set (and while it is asked: Checking).</summary>
    public string Status => _state.InQuickPlay ? "In a lobby" : Account.ConnectionText;

    /// <summary>The dot's state ("online", "waiting", "offline"); in a lobby, online.</summary>
    public string Connection => _state.InQuickPlay ? "online" : Account.Connection;

    /// <summary>Which server: "Server: official server", "Server: my.host"; none: what to do.</summary>
    public string ServerLine => Account.ServerText.Length > 0 ? $"Server: {Account.ServerText}" : "Choose a server to race online";

    /// <summary>Races, Wins and Podiums of the online races.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatTile> Figures { get; private set; } = [];

    /// <summary>The last races (online, not practice), newest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RecentRace> Recent { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRecent))]
    public partial bool HasRecent { get; private set; }

    public bool NoRecent => !HasRecent;

    /// <summary>The page shows: the figures and the replays read again (only what is new).</summary>
    public void Shown()
    {
        _main.Stats.Refresh();
        _main.Replays.Refresh();
    }

    /// <summary>"Change name": the name's panel.</summary>
    [RelayCommand]
    private void ChangeName() => _shell.Sheet = new NameSheetViewModel(Account, _shell, this);

    /// <summary>"Server": the servers to race on.</summary>
    [RelayCommand]
    private void ChooseServer() => _shell.ChangeServerCommand.Execute(null);

    /// <summary>"Your machine": the Garage.</summary>
    [RelayCommand]
    private void OpenGarage() => _shell.ShowArea(ShellViewModel.Garage);

    /// <summary>The figures: More › Stats.</summary>
    [RelayCommand]
    private void OpenStats() => _main.Current = _main.Stats;

    /// <summary>"Watch": the race played in the game, as Replays plays it.</summary>
    [RelayCommand]
    private void Watch(RecentRace? race)
    {
        if (race is not null && _main.Replays.PlayCommand.CanExecute(race.Item.Path))
        {
            _main.Replays.PlayCommand.Execute(race.Item.Path);
        }
    }

    public void Dispose()
    {
        _main.Account.PropertyChanged -= OnAccount;
        _main.Stats.Tiles.CollectionChanged -= OnTiles;
        _main.Replays.PropertyChanged -= OnReplays;
        _state.Changed -= OnState;
    }

    private void OnAccount(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InRaces));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ServerLine));
    }

    private void OnState() => OnUi(() =>
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Connection));
    });

    private void OnTiles(object? sender, NotifyCollectionChangedEventArgs e) => ShowFigures();

    private void OnReplays(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReplaysViewModel.Items))
        {
            ShowRecent();
        }
    }

    private void ShowFigures()
    {
        string[] shown = ["Races", "Wins", "Podiums"];
        var tiles = _main.Stats.Tiles.ToDictionary(t => t.Label, StringComparer.Ordinal);
        Figures = [.. shown.Select(label => tiles.TryGetValue(label, out var tile) ? tile : new StatTile(label, "0"))];
    }

    private void ShowRecent()
    {
        Recent = [.. _main.Replays.LatestRaces(RecentCount).Select(i => new RecentRace(i))];
        HasRecent = Recent.Count > 0;
    }
}
