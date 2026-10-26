using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Garage in two phases, as the lobby goes through its steps: SELECT, the
// roster, every machine of the shelf at once as a tile of its still (no paging, no scrolling), the machine raced
// ringed; then PREVIEW, the machine large on the stage with the panel (its parts, colour, stats, setting), and the one
// primary, USE THIS MACHINE, which makes it the machine raced and goes back to SELECT. A tile pressed only shows the
// machine: nothing is raced until the primary (or, in a lobby, READY). Back (Escape, B, the SELECT step) returns to the
// roster with the machine raced as it was; a colour or setting changed meanwhile is kept with its machine (each
// machine's look is kept as it is made, as before). On the Custom shelf the last tile is New machine, which opens the
// builder on a new machine.
public sealed partial class GarageViewModel
{
    public const int SelectPhase = 0, PreviewPhase = 1;

    // what SELECT stood on when PREVIEW opened: its machine and shelf, put back by Back
    private int _held = -1;
    private int _heldShelf = StockShelf;

    /// <summary>The phase on show: <see cref="SelectPhase"/> or <see cref="PreviewPhase"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Previewing), nameof(Selecting), nameof(ChosenIndex))]
    public partial int Phase { get; set; } = SelectPhase;

    /// <summary>A lobby's machine stage is on: its READY is PREVIEW's primary, and USE THIS MACHINE does not show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseShown))]
    public partial bool ReadyIsPrimary { get; set; }

    /// <summary>USE THIS MACHINE shows: PREVIEW's primary, out of a lobby's machine stage.</summary>
    public bool UseShown => !ReadyIsPrimary;

    /// <summary>In the middle of a custom machine's change not saved (PREVIEW in the builder): a lobby's stage opening
    /// does not take the page from under it.</summary>
    public bool MidEdit => Previewing && CustomShown && Builder.Unsaved;

    /// <summary>PREVIEW: the stage and the panel.</summary>
    public bool Previewing => Phase == PreviewPhase;

    /// <summary>SELECT: the shelves and the roster.</summary>
    public bool Selecting => Phase == SelectPhase;

    /// <summary>The step line under the title: SELECT · PREVIEW, the phase on show lit.</summary>
    public IReadOnlyList<PhaseStep> PhaseSteps { get; } = [new("Select", SelectPhase), new("Preview", PreviewPhase)];

    /// <summary>The stock machine SELECT stands on (the one raced, or in a lobby the one its choice starts on): while
    /// PREVIEW shows another, the one it opened from.</summary>
    public int ChosenIndex => Previewing ? _held : MachineIndex;

    /// <summary>The tiles of the roster, in the shelf's order: its stock machines (<see cref="GarageCard"/>), its custom
    /// ones (<see cref="CustomCard"/>), and on the Custom shelf New machine last (<see cref="NewMachineTile"/>).</summary>
    public ObservableCollection<object> Roster { get; } = [];

    /// <summary>How many tiles the roster is sized for, whatever the shelf holds: the stock machines, or the Custom
    /// shelf at its fullest with New machine, whichever is more; so a shelf of a few keeps the tiles' size.</summary>
    public static int RosterRoom => Math.Max(Machines.Names.Count, RacerProfile.MostCustoms + 1);

    /// <summary>Everything the shelf holds, in its order: its stock machines, then its custom ones.</summary>
    public IReadOnlyList<object> Shelved() => [.. Choices, .. ShelfCustoms];

    partial void OnPhaseChanged(int value)
    {
        SortFilterOpen = false;   // (SELECT's panel)
        foreach (var step in PhaseSteps)
        {
            step.On = step.Phase == value;
        }
        ShowSelected();
        OnPropertyChanged(nameof(SelectedFavourite));
    }

    /// <summary>A stock machine's tile pressed: PREVIEW with it on the stage. Nothing is raced.</summary>
    [RelayCommand]
    private void PreviewMachine(int id)
    {
        if (!Offers(id))
        {
            return;   // (a machine the lobby does not offer, shown greyed: never opened)
        }
        Hold();
        MachineIndex = id;
        Phase = PreviewPhase;
    }

    /// <summary>A custom machine's tile pressed: PREVIEW with the builder on it (its draft, if it was changed and not
    /// saved). Nothing is raced.</summary>
    [RelayCommand]
    private void PreviewCustom(CustomCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Hold();
        Shelf = CustomShelf;
        Builder.OpenSavedCommand.Execute(card);
        Phase = PreviewPhase;
    }

    /// <summary>New machine (the Custom shelf's last tile): PREVIEW with the builder on a new machine.</summary>
    [RelayCommand]
    private void NewMachine()
    {
        Hold();
        Shelf = CustomShelf;
        Builder.NewMachineCommand.Execute(null);
        Phase = PreviewPhase;
    }

    /// <summary>Back to SELECT, on the machine and the shelf it stood on: the machine raced is as it was.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!Previewing)
        {
            return;
        }
        var (held, shelf) = (_held, _heldShelf);
        Phase = SelectPhase;
        Shelf = shelf;
        if (Machines.Has(held))
        {
            MachineIndex = held;
        }
        ShowSelected();
    }

    /// <summary>USE THIS MACHINE: the machine on the stage is the one raced (a custom machine saved first when it was
    /// changed), and SELECT again with it ringed; the confirm's sound.</summary>
    [RelayCommand]
    private void UseMachine()
    {
        if (!Previewing)
        {
            return;
        }
        _confirming = true;   // (a Save on the way is this one confirm)
        try
        {
            Use();
        }
        finally
        {
            _confirming = false;
        }
        Confirm();
    }

    // a confirm under way (USE THIS MACHINE, READY): the builder's Save inside it sounds nothing more
    private bool _confirming;

    /// <summary>A machine was confirmed (USE THIS MACHINE, Save racing the build, a lobby's READY): the window plays the
    /// game's confirm. Not a tile pressed, not a move: those are navigation.</summary>
    public event Action? Confirmed;

    /// <summary>Tells <see cref="Confirmed"/> (a lobby's READY calls it).</summary>
    public void Confirm() => Confirmed?.Invoke();

    /// <summary>The machine on the stage made the one raced and SELECT again (the primary; in a lobby, READY). Told
    /// to a lobby (<see cref="Picked"/>).</summary>
    public void Use()
    {
        if (!Previewing)
        {
            return;
        }
        if (CustomShown)
        {
            Builder.UseCommand.Execute(null);   // (raced: the builder tells it, and Picked follows)
        }
        else
        {
            PickMachine(MachineIndex);
        }
        _held = MachineIndex;
        var shelf = CustomShown && _heldShelf != FavouritesShelf ? CustomShelf : _heldShelf;
        Phase = SelectPhase;
        Shelf = shelf;
        ShowSelected();
    }

    /// <summary>A step of the line pressed: SELECT is Back; PREVIEW opens the machine SELECT stands on (or the custom
    /// machine open on the Custom shelf).</summary>
    [RelayCommand]
    private void ShowStep(PhaseStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Phase == SelectPhase)
        {
            Back();
        }
        else if (!Previewing)
        {
            if (CustomShown)
            {
                Hold();
                Phase = PreviewPhase;
            }
            else if (Machines.Has(MachineIndex))
            {
                PreviewMachine(MachineIndex);
            }
        }
    }

    /// <summary>What SELECT stands on, kept for Back (once: a second preview from PREVIEW keeps the first).</summary>
    private void Hold()
    {
        if (!Previewing)
        {
            (_held, _heldShelf) = (MachineIndex, Shelf);
        }
    }

    /// <summary>The page's own move to SELECT (a lobby's machine choice opening or closing).</summary>
    private void LeavePreview()
    {
        if (Previewing)
        {
            var shelf = _heldShelf;
            Phase = SelectPhase;
            Shelf = shelf;
        }
    }

    /// <summary>The tile that stands for what is raced on this shelf: the stock machine lit, the custom machine
    /// raced, else the machine SELECT stands on; null when the shelf does not hold it.</summary>
    public object? Current()
    {
        var shelved = Shelved();
        return shelved.FirstOrDefault(c => c is GarageCard { Selected: true })
            ?? shelved.FirstOrDefault(c => c is CustomCard { Raced: true })
            ?? shelved.FirstOrDefault(c => c is GarageCard g && g.Id == ChosenIndex);
    }

    /// <summary>The roster brought to the shelf, with as few changes as it takes.</summary>
    private void ShowRoster()
    {
        List<object> wanted = [.. Shelved()];
        if (CustomShown)
        {
            wanted.Add(NewMachineTile.Instance);
        }
        if (wanted.SequenceEqual(Roster))
        {
            return;
        }
        for (var i = Roster.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Roster[i]))
            {
                Roster.RemoveAt(i);
            }
        }
        for (var place = 0; place < wanted.Count; place++)
        {
            var at = Roster.IndexOf(wanted[place]);
            if (at < 0)
            {
                Roster.Insert(place, wanted[place]);
            }
            else if (at != place)
            {
                Roster.Move(at, place);
            }
        }
    }
}

/// <summary>A step of the Garage's line: its word and its phase, lit when it shows.</summary>
public sealed partial class PhaseStep(string label, int phase) : ObservableObject
{
    public string Label { get; } = label;

    public int Phase { get; } = phase;

    [ObservableProperty]
    public partial bool On { get; set; } = phase == GarageViewModel.SelectPhase;
}

/// <summary>The Custom shelf's last tile: a new machine.</summary>
public sealed class NewMachineTile
{
    public static NewMachineTile Instance { get; } = new();

    private NewMachineTile()
    {
    }
}
