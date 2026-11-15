using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's lobby page: the page of the phase the lobby is in
/// (<see cref="LobbyViewModel.Phase"/>), one at a time in the same place, each made when its phase comes and let go
/// when it is over. The race and its result are one page (its parts keep their places from one to the other); the
/// machine stage is its own page or, opened from it, the whole Garage.</summary>
public sealed partial class QuickPlayView : UserControl
{
    private readonly ContentControl? _host;
    private LobbyViewModel? _lobby;
    private string _shown = "";

    public QuickPlayView()
    {
        AvaloniaXamlLoader.Load(this);
        _host = this.FindControl<ContentControl>("PhaseHost");
        DataContextChanged += (_, _) => Hear();
    }

    private void Hear()
    {
        if (_lobby is not null)
        {
            _lobby.PropertyChanged -= OnLobbyChanged;
        }
        _lobby = DataContext as LobbyViewModel;
        if (_lobby is not null)
        {
            _lobby.PropertyChanged += OnLobbyChanged;
        }
        Show();
    }

    private void OnLobbyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LobbyViewModel.Phase) or nameof(LobbyViewModel.FullGarage) or nameof(LobbyViewModel.HostEditing))
        {
            Show();
        }
    }

    /// <summary>Which page a phase shows, by a name: the same name keeps the page that is there.</summary>
    public static string PageOf(LobbyPhase phase, bool fullGarage, bool hostEditing = false) => phase switch
    {
        LobbyPhase.Race or LobbyPhase.Results => "race",
        LobbyPhase.Machine => fullGarage ? "garage" : "machine",
        LobbyPhase.Room => hostEditing ? "rules" : "room",
        _ => phase.ToString().ToLowerInvariant(),
    };

    /// <summary>A page is made once this page is in the window (made before, its parts would miss the theme's
    /// styles, which follow the shell's class).</summary>
    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Show();
    }

    private void Show()
    {
        if (_host is null || _lobby is not { } lobby || !((Avalonia.LogicalTree.ILogical)this).IsAttachedToLogicalTree)
        {
            return;
        }
        var page = PageOf(lobby.Phase, lobby.FullGarage, lobby.HostEditing);
        if (page == _shown && _host.Content is not null)
        {
            return;
        }
        _shown = page;
        _host.Content = page switch
        {
            "finding" => new FindingView(),
            "gather" => new GatherView(),
            "vote" => new VoteView(),
            "machine" => new MachineView(),
            "garage" => new GaragePageView { DataContext = lobby.Garage },
            "starting" => new StartingView(),
            "race" => new RaceView(),
            "room" => new RoomView(),
            "rules" => new HostRulesView(),
            _ => new LobbyView(),
        };
        Controls.Motion.EnterPage(_host, 0, 12);
    }
}
