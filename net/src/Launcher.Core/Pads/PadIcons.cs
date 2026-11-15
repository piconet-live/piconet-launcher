namespace Launcher.Core.Pads;

/// <summary>Whose button icons the redesigned interface shows (Settings, Controller; "Button icons"): the pad's own
/// family (<see cref="Auto"/>: known from the device the pad is read from), or one chosen.</summary>
public enum ButtonIcons
{
    Auto,
    Xbox,
    PlayStation,
    Nintendo,
    GameCube,
    Other,
}

/// <summary>Which of the pad's face buttons confirms ("Confirm button"): the bottom one, the right one, or as the
/// pad's family has it (<see cref="Auto"/>: Nintendo's on the right, every other's at the bottom).</summary>
public enum ConfirmButton
{
    Auto,
    Bottom,
    Right,
}

/// <summary>
/// The pad's icons as the redesigned interface draws them in its hints: a button the launcher acts on (by its GameCube
/// name, <see cref="PadButton"/>) shown as the face button of the player's own pad at the place it sits, in that pad's
/// family's look (Xbox's letters, PlayStation's shapes, Nintendo's letters the other way round, the GameCube's own,
/// numbers for any other). The launcher reads the pad through the game's own mapping, whose standard layouts put the
/// GameCube's A on the bottom face button and B on the right one; with the confirm button on the right the two change
/// places (<see cref="Swap"/>).
/// </summary>
public static class PadIcons
{
    /// <summary>The family a pad's device belongs to, by its name as the game's settings give it
    /// (<c>BACKEND/INDEX/NAME</c>); the GameCube adapter is the GameCube's.</summary>
    public static ButtonIcons FamilyOf(string? device, bool adapter)
    {
        if (adapter)
        {
            return ButtonIcons.GameCube;
        }
        var name = (device ?? "").ToUpperInvariant();
        if (name.Length == 0)
        {
            return ButtonIcons.GameCube;   // no pad known: the launcher's own names (A, B, X, Y) are the GameCube's
        }
        // a GameCube controller on a USB adapter (Nintendo's WUP-028, Mayflash's) or named as one: before Nintendo's
        // own, as "Nintendo GameCube Controller" names both
        if (name.Contains("GAMECUBE", StringComparison.Ordinal) || name.Contains("WUP-028", StringComparison.Ordinal)
            || name.Contains("MAYFLASH", StringComparison.Ordinal) || name.Contains("GC CONTROLLER", StringComparison.Ordinal))
        {
            return ButtonIcons.GameCube;
        }
        if (name.Contains("XBOX", StringComparison.Ordinal) || name.Contains("XINPUT", StringComparison.Ordinal))
        {
            return ButtonIcons.Xbox;
        }
        if (name.Contains("PS4", StringComparison.Ordinal) || name.Contains("PS5", StringComparison.Ordinal) || name.Contains("PS3", StringComparison.Ordinal)
            || name.Contains("DUALSHOCK", StringComparison.Ordinal) || name.Contains("DUALSENSE", StringComparison.Ordinal)
            || name.Contains("PLAYSTATION", StringComparison.Ordinal) || name.Contains("WIRELESS CONTROLLER", StringComparison.Ordinal))
        {
            return ButtonIcons.PlayStation;
        }
        if (name.Contains("NINTENDO", StringComparison.Ordinal) || name.Contains("SWITCH", StringComparison.Ordinal)
            || name.Contains("PRO CONTROLLER", StringComparison.Ordinal) || name.Contains("JOY-CON", StringComparison.Ordinal))
        {
            return ButtonIcons.Nintendo;
        }
        return ButtonIcons.Other;
    }

    /// <summary>The family shown: the one chosen, else the pad's own.</summary>
    public static ButtonIcons Shown(ButtonIcons chosen, string? device, bool adapter) =>
        chosen == ButtonIcons.Auto ? FamilyOf(device, adapter) : chosen;

    /// <summary>The confirm button is the right face button: chosen so, or by the family (Nintendo's).</summary>
    public static bool ConfirmRight(ConfirmButton chosen, ButtonIcons family) =>
        chosen == ConfirmButton.Right || (chosen == ConfirmButton.Auto && family == ButtonIcons.Nintendo);

    /// <summary>A button as the launcher acts on it, from the button the pad pressed: A and B change places when the
    /// right face button confirms (on the GameCube adapter the buttons are the GameCube's own, and stay).</summary>
    public static PadButton Swap(PadButton pressed, bool confirmRight, ButtonIcons family) =>
        !confirmRight || family == ButtonIcons.GameCube ? pressed
        : pressed switch
        {
            PadButton.A => PadButton.B,
            PadButton.B => PadButton.A,
            _ => pressed,
        };

    /// <summary>The icon of a button the launcher acts on, in the family's look: "A", "✕", "1"; "Start", "☰", "+";
    /// L and R as the inputs the game's standard layout puts them on ("LT", "L2", "ZL"; <see cref="Shoulders"/>), Z
    /// as the right shoulder ("RB", "R1").</summary>
    public static string Glyph(PadButton button, ButtonIcons family, bool confirmRight)
    {
        if (family == ButtonIcons.Auto)
        {
            family = ButtonIcons.GameCube;
        }
        switch (button)
        {
            case PadButton.Start:
                return family switch
                {
                    ButtonIcons.Xbox => "☰",
                    ButtonIcons.PlayStation => "Options",
                    ButtonIcons.Nintendo => "+",
                    _ => "Start",
                };
            case PadButton.L or PadButton.R:
                var (l, r) = Shoulders(family);
                return button == PadButton.L ? l : r;
            case PadButton.Z:
                return family switch
                {
                    ButtonIcons.Xbox => "RB",
                    ButtonIcons.PlayStation or ButtonIcons.Other => "R1",
                    ButtonIcons.Nintendo => "R",
                    _ => "Z",
                };
            default:
                break;
        }
        if (family == ButtonIcons.GameCube)
        {
            return button.ToString();
        }
        // the place the button sits at: the confirm button's (A) bottom or right, the other (B) the other; X left, Y top
        var place = button switch
        {
            PadButton.A => confirmRight ? Place.Right : Place.Bottom,
            PadButton.B => confirmRight ? Place.Bottom : Place.Right,
            PadButton.X => Place.Left,
            _ => Place.Top,
        };
        return (family, place) switch
        {
            (ButtonIcons.Xbox, Place.Bottom) => "A",
            (ButtonIcons.Xbox, Place.Right) => "B",
            (ButtonIcons.Xbox, Place.Left) => "X",
            (ButtonIcons.Xbox, _) => "Y",
            (ButtonIcons.PlayStation, Place.Bottom) => "✕",
            (ButtonIcons.PlayStation, Place.Right) => "○",
            (ButtonIcons.PlayStation, Place.Left) => "□",
            (ButtonIcons.PlayStation, _) => "△",
            (ButtonIcons.Nintendo, Place.Bottom) => "B",
            (ButtonIcons.Nintendo, Place.Right) => "A",
            (ButtonIcons.Nintendo, Place.Left) => "Y",
            (ButtonIcons.Nintendo, _) => "X",
            (_, Place.Bottom) => "1",
            (_, Place.Right) => "2",
            (_, Place.Left) => "3",
            _ => "4",
        };
    }

    /// <summary>The GameCube's L and R as the family names the inputs the game's standard layout puts them on (the
    /// launcher's areas step by them): a pad's triggers, LT and RT, L2 and R2, ZL and ZR (its shoulder buttons are not
    /// L and R: the right one is Z, the left one nothing); the GameCube's own L and R.</summary>
    public static (string Left, string Right) Shoulders(ButtonIcons family) => family switch
    {
        ButtonIcons.Xbox => ("LT", "RT"),
        ButtonIcons.PlayStation or ButtonIcons.Other => ("L2", "R2"),
        ButtonIcons.Nintendo => ("ZL", "ZR"),
        _ => ("L", "R"),
    };

    /// <summary>The family in words, for the Settings page.</summary>
    public static string Words(ButtonIcons family) => family switch
    {
        ButtonIcons.Auto => "Auto",
        ButtonIcons.PlayStation => "PlayStation",
        ButtonIcons.GameCube => "GameCube",
        ButtonIcons.Other => "Other",
        _ => family.ToString(),
    };

    private enum Place
    {
        Bottom,
        Right,
        Left,
        Top,
    }
}
