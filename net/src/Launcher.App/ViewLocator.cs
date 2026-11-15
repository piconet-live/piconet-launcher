using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;

namespace Launcher.App;

/// <summary>Each screen's view for its view model: the one place that pairs them, so a new design swaps views here.
/// There are two sets: the classic interface's (<c>Views/</c>), and the redesigned interface's (<c>Views/NextUi/</c>,
/// Settings' "Experimental UI"), which takes the classic view of every page it has none of its own for yet.</summary>
public sealed class ViewLocator : IDataTemplate
{
    /// <summary>The redesigned interface's views: set on the locator its shell holds
    /// (<c>Views/NextUi/NextShell.axaml</c>). The application's own locator leaves it off: the classic views, as
    /// before.</summary>
    public bool Next { get; set; }

    public Control? Build(object? param) => (Next ? Redesigned(param) : null) ?? Classic(param);

    private static Control Classic(object? param) => param switch
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
        ModsViewModel => new ModsView(),
        DiscViewModel => new DiscView(),
        _ => new TextBlock { Text = param?.GetType().Name ?? "" },
    };

    /// <summary>The redesigned interface's view of a page; null: it has none yet, and the classic one shows.</summary>
    private static Control? Redesigned(object? param) => param switch
    {
        HubViewModel => new HubView(),
        ServersViewModel => new ServersView(),
        CustomLobbiesViewModel => new CustomListView(),
        PracticeViewModel => new PracticeSetupView(),
        RulesSheetViewModel => new RulesSheetView(),
        GarageViewModel => new GaragePageView(),
        DiagnosticsSheetViewModel => new DiagnosticsSheetView(),
        DiscViewModel => new FirstStartView(),
        SettingsViewModel => new SettingsPageView(),
        ControllerViewModel => new ControllerPageView(),
        ReplaysViewModel => new ReplaysListView(),
        StatsViewModel => new StatsPageView(),
        ModsViewModel => new ModsListView(),
        AboutViewModel => new AboutView(),
        ProfileViewModel => new ProfileView(),
        ReplaySheetViewModel => new ReplaySheetView(),
        ModSheetViewModel => new ModSheetView(),
        LicencesSheetViewModel => new LicencesSheetView(),
        PrepareSheetViewModel => new PrepareSheetView(),
        ServerSheetViewModel => new ServerSheetView(),
        NameSheetViewModel => new NameSheetView(),
        SortSheetViewModel => new SortSheetView(),
        ColourSheetViewModel => new ColourSheetView(),
        StatsSheetViewModel => new StatsSheetView(),
        MotdSheetViewModel => new MotdSheetView(),
        MenuViewModel => new MenuView(),
        HomeViewModel => new LobbiesView(),
        LobbyViewModel => new QuickPlayView(),
        LobbySheetViewModel => new LobbySheetView(),
        RacersSheetViewModel => new RacersSheetView(),
        _ => null,   // (Home network keeps its classic page: the redesign hides it)
    };

    public bool Match(object? data) => data is ViewModelBase;
}
