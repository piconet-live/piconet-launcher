using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>A device in the Controller page's list: one that is plugged in, or the one the port names that is not.</summary>
public sealed record DeviceChoice(string Id, string Label, ControllerDevice? Device)
{
    public override string ToString() => Label;
}

/// <summary>One row of the Controller page's mapping table: a GameCube control, the input it is set to, and whether
/// the page is waiting for the player to press the input they want for it.</summary>
public sealed partial class MapRow(PadControl control) : ObservableObject
{
    public PadControl Control { get; } = control;

    public string Label => Control.Label;

    /// <summary>The input, as a player reads it; "": not set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(Unset))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown), nameof(Unset))]
    public partial bool Listening { get; set; }

    public string Shown => Listening ? "Listening…" : Input.Length > 0 ? Input : "Not set";

    /// <summary>Nothing is set for the control: "Not set", in the off colour.</summary>
    public bool Unset => !Listening && Input.Length == 0;

    /// <summary>What a screen reader calls the row's button.</summary>
    public string SetName => $"Set {Label}";
}
