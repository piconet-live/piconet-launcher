namespace Launcher.App.ViewModels.NextUi;

/// <summary>The lobby's panel (the pad's Y on a lobby's page): its rules in short, its racers and watchers, watching
/// instead of racing, the game on its own instead, and leaving. Everything in it is the lobby page's
/// (<see cref="LobbyViewModel"/>) and the play control's; the shell opens and closes it.</summary>
public sealed class LobbySheetViewModel(MainViewModel main, ShellViewModel shell) : ViewModelBase
{
    public MainViewModel Main { get; } = main;

    public ShellViewModel Shell { get; } = shell;

    public LobbyViewModel Lobby => Main.Lobby;
}

/// <summary>The race's panel (the pad's Y on the race's page): every racer as the race runs, every result once it is
/// over, with the player's own times and a mode's lines. The standings are the classic launcher's
/// (<see cref="RaceViewModel"/>).</summary>
public sealed class RacersSheetViewModel(MainViewModel main, ShellViewModel shell) : ViewModelBase
{
    public MainViewModel Main { get; } = main;

    public ShellViewModel Shell { get; } = shell;

    public LobbyViewModel Lobby => Main.Lobby;

    public RaceViewModel Race => Main.Lobby.Race;

    /// <summary>"All racers" while the race runs, "All results" once it is over.</summary>
    public string Title => Lobby.Phase == LobbyPhase.Results ? "All results" : "All racers";
}

/// <summary>The panel of the server's message of the day (the hub's strip pressed): the whole message, when it was
/// set, and "Hide for today".</summary>
public sealed class MotdSheetViewModel(HubViewModel hub) : ViewModelBase
{
    public HubViewModel Hub { get; } = hub;
}

/// <summary>The panel of every rule of a custom lobby (the room's "All rules", the pad's Y): its name, and the rules in
/// their groups as rows that say their value, those that differ from Quick Play's in the accent.</summary>
public sealed class RulesSheetViewModel(string name, IReadOnlyList<OptionGroup> groups) : ViewModelBase
{
    public string Name { get; } = name;

    public IReadOnlyList<OptionGroup> Groups { get; } = groups;
}
