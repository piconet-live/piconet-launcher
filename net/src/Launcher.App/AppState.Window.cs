using Launcher.Core;

namespace Launcher.App;

/// <summary>What the launcher gives the window of a game it starts.</summary>
public sealed partial class AppState
{
    /// <summary>This launcher's button on the Windows taskbar, once its own window has one (<see cref="Taskbar"/>,
    /// set by the application when it makes the window); null in a mode without a window and in a test, whose
    /// games keep a button of their own.</summary>
    public TaskbarGroup? TaskbarGroup { get; set; }

    /// <summary>The game's window: its title (the game's name, or a launch file's test label), its strip's name (as
    /// text, which the runner writes in the display face: the game-lettered name left the title bars with Direction D) and
    /// frame rate, the launcher's taskbar button, the player's vertical sync, kind of full screen, rumble and race camera, the settings file's way of showing
    /// a picture where it is one of the two kept for comparison, and its refresh rate where frames are drawn between steps.
    /// The frame rate is always shown on a test build and in a test game (<see cref="LauncherSettings.FrameRateShown"/>).</summary>
    private GameWindow GameWindow =>
        new(Debug.Title ?? "F-Zero GX", Branding.DisplayName,
            Settings.FrameRateShown(StackStamp.Current, testGame: !ReferenceEquals(Debug, DebugSettings.Off)),
            NamePicture: null, TaskbarGroup,
            Settings.GamePresent, Settings.VerticalSync, Settings.GameFullScreen, RefreshRate: Settings.GameRefreshRate,
            Rumble: Settings.Rumble, Camera: Settings.StartCamera);
}
