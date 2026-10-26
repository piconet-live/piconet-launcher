using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// A lobby's stage with a decision (the course vote, the machine choice) always has one obvious way to it (a player in
// the Garage must not be left stuck there when the course vote is called): as it
// opens the launcher brings the player to the lobby's page on that stage's face, from the Garage place or the lobby's
// own Garage; unless they are in the middle of a custom machine's change not saved, which is not thrown away under
// them: then the left column shows the stage as a link that brings them there.
public sealed partial class MainViewModel
{
    /// <summary>The stage with a decision the player was not brought to ("Course vote", "Machines"); "" none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecisionShown))]
    public partial string DecisionText { get; set; } = "";

    public bool DecisionShown => DecisionText.Length > 0;

    private LobbyScreen _decision;

    /// <summary>A stage with a decision opened (<see cref="LobbyScreens.BringsToLobby"/>): the player brought to it,
    /// or, mid-change in the builder, told of it.</summary>
    private void BringToDecision(LobbyScreen screen)
    {
        if (Garage.MidEdit && (Current is GarageViewModel || (Current is LobbyViewModel && Lobby.GarageShown)))
        {
            (_decision, DecisionText) = (screen, screen == LobbyScreen.Starting ? "Course vote" : "Machines");
            return;
        }
        GoToDecision(screen);
    }

    /// <summary>The lobby's page on the stage's face: the vote (the lobby's own), or the machine choice (its Garage).</summary>
    private void GoToDecision(LobbyScreen screen)
    {
        DecisionText = "";
        Current = Lobby;
        if (screen == LobbyScreen.Starting)
        {
            Lobby.ShowLobbyFace();
        }
    }

    /// <summary>The left column's link to the stage the player was not brought to.</summary>
    [RelayCommand]
    private void ShowDecision() => GoToDecision(_decision);

    /// <summary>The stage the link names is over: no link.</summary>
    private void ForgetDecision(LobbyScreen screen)
    {
        if (DecisionShown && screen != _decision)
        {
            DecisionText = "";
        }
    }
}
