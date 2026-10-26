using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.App;

/// <summary>Each screen's view for its view model: the one place that pairs them, so a new design swaps views here.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        HomeViewModel => new HomeView(),
        GarageViewModel => new GarageView(),
        LobbyViewModel => new LobbyView(),
        LanViewModel => new LanView(),
        PracticeViewModel => new PracticeView(),
        CustomLobbiesViewModel => new CustomLobbiesView(),
        SettingsViewModel => new SettingsView(),
        ControllerViewModel => new ControllerView(),
        ReplaysViewModel => new ReplaysView(),
        StatsViewModel => new StatsView(),
        DiscViewModel => new DiscView(),
        _ => new TextBlock { Text = param?.GetType().Name ?? "" },
    };

    public bool Match(object? data) => data is ViewModelBase;
}
