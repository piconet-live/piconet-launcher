namespace Launcher.App.ViewModels;

// What the pad's shoulder buttons and B do to the pages (Views/PadNavigator.cs): the tabs in order, the tab's pages
// in order, and the way back.
public sealed partial class MainViewModel
{
    /// <summary>The pad that works the window (read while the window is in front and no game runs).</summary>
    public Launcher.Core.Pads.PadReader PadReader => _state.Pads;

    /// <summary>The pad works the pages (Settings, Launcher: "Controller navigation"; off by default).</summary>
    public bool PadNavigation => _state.Settings.PadNavigation;

    /// <summary>The window came to the front (true) or was left: the pads are read only while it is in front.</summary>
    public void WindowFocus(bool active) => _state.WindowFocus(active);

    /// <summary>The tabs as L and R go through them, left to right as the window shows them, Settings last (it is at
    /// the left column's foot).</summary>
    public static IReadOnlyList<string> TabOrder { get; } = ["play", "garage", "replays", "settings"];

    /// <summary>L (-1) and R (1): the tab before or after the one showing, its page the one last shown there; at
    /// either end nothing changes. False: nothing changed (the first screen has no tabs).</summary>
    public bool StepTab(int by)
    {
        if (!Navigation)
        {
            return false;
        }
        var at = TabOrder.ToList().IndexOf(Tab);
        var to = Math.Clamp(at + by, 0, TabOrder.Count - 1);
        if (at < 0 || to == at)
        {
            return false;
        }
        ShowTab(TabOrder[to]);
        return true;
    }

    /// <summary>Z: the tab's next page (Quick Play, Custom lobbies, Lobby, Practice; Replays, Stats; Settings' four), round to the
    /// first after the last. False: the tab has one page.</summary>
    public bool NextSection()
    {
        if (Sections.Count < 2)
        {
            return false;
        }
        var at = Sections.ToList().FindIndex(s => s.Current);
        Show(Sections[(at + 1) % Sections.Count]);
        return true;
    }

    /// <summary>B, with nothing open over the page: one step up. A Play page other than Quick Play and the lobby goes
    /// (Create first back to the browser) to the one the player plays from (the lobby while in Quick Play); the Garage, Replays (and Stats) and Settings go to Play.
    /// False: there is no way up (Quick Play, the lobby, the first screen).</summary>
    public bool Back()
    {
        switch (Current)
        {
            case CustomLobbiesViewModel { Creating: true } lobbies:
                lobbies.BackCommand.Execute(null);   // Create: back to the browser
                return true;
            case LanViewModel or PracticeViewModel or CustomLobbiesViewModel:
                // (a visit to the custom lobbies' browser, in no lobby, has no lobby page)
                Current = _state.InQuickPlay && _state.Session is not { Custom: true, Lobby: null } ? Lobby : Home;
                return true;
            case GarageViewModel or ReplaysViewModel or StatsViewModel or SettingsViewModel or ControllerViewModel:
                ShowTab("play");
                return true;
            default:
                return false;
        }
    }
}
