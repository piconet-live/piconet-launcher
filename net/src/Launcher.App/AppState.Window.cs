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
        WithAurora(new(Debug.Title ?? "F-Zero GX", Branding.DisplayName,
            Settings.FrameRateShown(StackStamp.Current, testGame: !ReferenceEquals(Debug, DebugSettings.Off)),
            NamePicture: null, TaskbarGroup,
            Settings.GamePresent, Settings.VerticalSync, Settings.GameFullScreen, RefreshRate: Settings.GameRefreshRate,
            Rumble: Settings.Rumble, Camera: Settings.StartCamera, ShowInputs: Settings.ShowInputs,
            ModernHud: Hud.Modern, HudLayout: Hud.Modern ? Path.Combine(DataFolder, "hud-layout.json") : null, HudMeter: Hud.Meter));

    /// <summary>The HUD's keys: left out in a build that does not offer the modern HUD (a test build only, for 0.6).</summary>
    private (bool Modern, string? Meter) Hud => Settings.HudLaunchKeys(offered: StackStamp.Current.Test);

    /// <summary>The picture engine's keys: left out in a build that does not offer Aurora, and at their defaults.</summary>
    private GameWindow WithAurora(GameWindow window)
    {
        var (engine, size, edges, textures, colourblind) = Settings.AuroraLaunchKeys(offered: StackStamp.Current.Test);
        return window with { Renderer = engine, AuroraSize = size, AuroraEdges = edges, AuroraTextures = textures, AuroraColourblind = colourblind };
    }
}
