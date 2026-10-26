using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App;

// The pad that works the window (Launcher.Core/Pads/PadReader.cs): read while the window is in front and no game runs.
public sealed partial class AppState
{
    /// <summary>(A property, not a field: the reader is ended by <see cref="Shutdown"/>, as the session is.)</summary>
    private PadReader? PadsMade { get; set; }
    private bool _padsHeard;

    /// <summary>The pad reader, made at its first use (the window's; a mode without a window never reads a pad).</summary>
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
            }
            return PadsMade;
        }
    }

    /// <summary>The launcher's window is in front (the pads are read only then).</summary>
    public bool WindowActive { get; private set; }

    /// <summary>The Controller page runs a helper of its own: the reader runs none, and hears the page's.</summary>
    public bool PageHelper { get; private set; }

    /// <summary>The window came to the front or was left.</summary>
    public event Action? FocusChanged;

    /// <summary>A window has said whether it is in front (<see cref="WindowFocus"/>); until then (a test's page with
    /// no main window) the Controller page does not wait for it.</summary>
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

    /// <summary>The Controller page starts its helper (true) or has ended it (false). Before the page's starts, the
    /// reader's has gone: the two never hold the GameCube adapter at once.</summary>
    public void PageHelperRuns(bool runs)
    {
        PageHelper = runs;
        PadsChanged();
        if (runs && PadsMade is { } pads)
        {
            pads.Ending.Wait(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>The conditions for reading the pads, as they stand now, given to the reader.</summary>
    public void PadsChanged() =>
        PadsMade?.Update(new PadConditions(WindowActive, Racing, CheckingController, PageHelper));

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
