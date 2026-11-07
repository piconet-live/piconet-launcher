using Launcher.Core;
using Launcher.Core.Board;

namespace Launcher.App.Controls;

/// <summary>
/// A pad as <see cref="PadOverlay"/> draws it: the buttons held by name and the sticks and triggers as the game reads
/// them, through the SDK's PADClamp (stick -72 to 72, C-stick -59 to 59, triggers 0 to 150).
/// </summary>
public sealed record PadPicture(IReadOnlySet<string> Buttons, int StickX, int StickY, int CStickX, int CStickY, int L, int R)
{
    /// <summary>The controller helper's reading of a port (raw, 0 to 255): clamped as the game would; null when nothing is
    /// plugged in (the skin then stands at rest).</summary>
    public static PadPicture? Of(HelperPad? pad)
    {
        if (pad is not { Connected: true })
        {
            return null;
        }
        var (stickX, stickY) = PadSkin.ClampedStick(pad.StickX, pad.StickY);
        var (cStickX, cStickY) = PadSkin.ClampedCStick(pad.CStickX, pad.CStickY);
        return new(pad.Buttons, stickX, stickY, cStickX, cStickY, PadSkin.ClampedTrigger(pad.L), PadSkin.ClampedTrigger(pad.R));
    }

    /// <summary>The game's own reading from the status board (already clamped); null: none (at rest).</summary>
    public static PadPicture? Of(PadReading? pad) =>
        pad is null ? null : new(pad.Held, pad.StickX, pad.StickY, pad.CStickX, pad.CStickY, pad.L, pad.R);
}
