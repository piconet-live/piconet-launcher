using CommunityToolkit.Mvvm.Input;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// A server pressed on "Choose a server": the next step as a panel, the two ways onto it said plainly. "Join a race":
/// Quick Play puts the player in a Quick Play lobby open there (as RACE does), else says "No race found" and joins
/// nothing; "Host a lobby": a Quick Play lobby there (Quick Play's queue, which opens a new one when none has room),
/// never a custom lobby: custom lobbies are the hub's Custom lobby (the owner, 2026-11-15); "See its lobbies": the
/// lobbies people are in there. Each sets the server first. Nothing is joined or made by the press on the row alone.
/// </summary>
public sealed partial class ServerSheetViewModel(ServerRow row, ServersViewModel servers) : ViewModelBase, IOwnedSheet
{
    public ViewModelBase Owner { get; } = servers;

    public ServerRow Row { get; } = row;

    /// <summary>"Join a race": Quick Play on this server.</summary>
    [RelayCommand]
    private Task Join() => servers.JoinRace(Row);

    /// <summary>"Host a lobby": a Quick Play lobby on this server, never a custom one.</summary>
    [RelayCommand]
    private Task Host() => servers.HostOn(Row);

    /// <summary>"See its lobbies": the lobbies people are in on this server.</summary>
    [RelayCommand]
    private void Lobbies() => servers.LobbiesOn(Row);
}
