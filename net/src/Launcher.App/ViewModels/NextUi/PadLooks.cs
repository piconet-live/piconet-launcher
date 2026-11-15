using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>The pad's look in the redesigned interface: the family of its icons (the one chosen, else the pad's own, by
/// the device the pad is read from) and whether its right face button confirms; and a hint's button in that look.</summary>
public static class PadLooks
{
    /// <summary>The family and the confirm button as the settings and the pad say them now.</summary>
    public static (ButtonIcons Family, bool ConfirmRight) Of(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var port = state.Pads.NavPortInfo;
        var family = PadIcons.Shown(state.Settings.ButtonIcons, port?.Device, port?.Kind == PortSettings.AdapterNumber);
        return (family, PadIcons.ConfirmRight(state.Settings.ConfirmButton, family));
    }

    /// <summary>A hint's button ("A", "B", "X", "Y", "Start", "L  R"; anything else as it is) in the family's look.</summary>
    public static string Glyph(string button, ButtonIcons family, bool confirmRight)
    {
        var (left, right) = PadIcons.Shoulders(family);
        return button switch
        {
            "A" => PadIcons.Glyph(PadButton.A, family, confirmRight),
            "B" => PadIcons.Glyph(PadButton.B, family, confirmRight),
            "X" => PadIcons.Glyph(PadButton.X, family, confirmRight),
            "Y" => PadIcons.Glyph(PadButton.Y, family, confirmRight),
            "Start" => PadIcons.Glyph(PadButton.Start, family, confirmRight),
            "Z" => PadIcons.Glyph(PadButton.Z, family, confirmRight),
            "L  R" => $"{left}  {right}",
            _ => button,
        };
    }
}

/// <summary>Developer mode's two lines in the hint bar: where the window is (the area, the page and what it is at), and
/// the input (keyboard or pad, its family, the confirm button), the focus and the scale.</summary>
public static class DevLines
{
    public static (string Place, string Input) Of(MainViewModel main, AppState state, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(shell);
        var page = main.Current;
        var where = page switch
        {
            DiscViewModel disc => $"first start · step {shell.FirstStart.Step} of {FirstStartViewModel.Steps} · disc {(disc.Ready ? "ready" : disc.Preparing ? "preparing" : disc.Verified ? "checked" : "none")}",
            LobbyViewModel lobby => $"lobby {state.Session?.Lobby?.LobbyId ?? "queue"} · {lobby.Phase}" + (lobby.FullGarage ? " · garage" : ""),
            GarageViewModel garage => $"garage · machine {garage.MachineIndex} · {(garage.StagedCustom ? "custom" : "stock")} · shelf {garage.Shelf}",
            null => "–",
            _ => page.GetType().Name.Replace("ViewModel", "", StringComparison.Ordinal).ToLowerInvariant(),
        };
        var (family, confirmRight) = PadLooks.Of(state);
        var input = shell.PadInUse ? "pad · " + PadIcons.Words(family) + (confirmRight ? " · confirm right" : "") : "keyboard and mouse";
        return ($"{(shell.Area.Length > 0 ? shell.Area : "no area")} · {where}",
            $"input {input} · focus {(shell.FocusName.Length > 0 ? shell.FocusName : "–")} · scale {state.Settings.ShownInterfaceScale}%");
    }
}
