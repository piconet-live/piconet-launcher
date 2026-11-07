using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App;

// The launcher's controller helper (Launcher.Core/Pads/PadReader.cs): it holds the devices, the GameCube adapter among
// them, for as long as the launcher runs and no game does; the pad works the window only while it is in front.
public sealed partial class AppState
{
    /// <summary>(A property, not a field: the reader is ended by <see cref="Shutdown"/>, as the session is.)</summary>
    private PadReader? PadsMade { get; set; }
    private bool _padsHeard;

    /// <summary>The launcher's helper, made at its first use (the window's, or the Controller page's; a mode without a
    /// window never starts one) and started at once.</summary>
    public PadReader Pads
    {
        get
        {
            if (PadsMade is null)
            {
                PadsMade = new PadReader(StartPadHelper, askAdapter: !Debug.LeaveAdapter, FirstPads);
                if (!_padsHeard)
                {
                    _padsHeard = true;
                    Changed += PadsChanged;
                }
                PadsChanged();
            }
            return PadsMade;
        }
    }

    /// <summary>The launcher's window is in front (the pad works the window only then).</summary>
    public bool WindowActive { get; private set; }

    /// <summary>The window came to the front or was left.</summary>
    public event Action? FocusChanged;

    /// <summary>A window has said whether it is in front (<see cref="WindowFocus"/>).</summary>
    public bool FocusKnown { get; private set; }

    public void WindowFocus(bool active)
    {
        if (FocusKnown && active == WindowActive)
        {
            return;
        }
        FocusKnown = true;
        WindowActive = active;
        PadsChanged();
        FocusChanged?.Invoke();
    }

    /// <summary>The window the helper was made for has gone (or a test's page that made it): the helper is ended, and
    /// the adapter is free. A later use makes a new one.</summary>
    public void ReleasePads()
    {
        var pads = PadsMade;
        PadsMade = null;
        pads?.Dispose();
    }

    /// <summary>The conditions for the launcher's helper, as they stand now, given to it.</summary>
    public void PadsChanged() =>
        PadsMade?.Update(new PadConditions(WindowActive, Racing, CheckingController));

    private ControllerHelper? StartPadHelper()
    {
        var paths = GamePaths;
        try
        {
            RecompLaunch.PrepareUserFolder(paths.UserFolder, Settings.UserFolder);
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            return null;
        }
        return ControllerHelper.Start(paths);
    }

    /// <summary>A game with no controller settings gets its first ones as a start would give them
    /// (<see cref="ControllerStart.PrepareAsync"/>): port 1 is the first gamepad, else the keyboard.</summary>
    private bool FirstPads(IReadOnlyList<ControllerDevice> devices)
    {
        var paths = GamePaths;
        try
        {
            return !ControllerFiles.HasSettings(paths.UserFolder)
                && ControllerFiles.WriteFirstStart(paths.UserFolder,
                    StandardLayouts.FirstStart(devices, ControllerFiles.GamepadProfile(paths.Runner)));
        }
        catch (Exception e) when (ControllerFiles.Failed(e))
        {
            return false;
        }
    }
}
