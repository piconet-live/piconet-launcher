using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>A panel of the redesigned Garage (the shell keeps it open while a Garage shows: the Garage area's page, a
/// lobby's whole Garage).</summary>
public interface IGarageSheet
{
    /// <summary>B inside the panel: a step back (the colours: from the wheel to the palette). False: the panel is on
    /// its first step, and B closes it.</summary>
    bool StepBack() => false;
}

/// <summary>A way round of the order in the Sort &amp; filter panel: its words ("A to E", "high to low"), on when the
/// machines are in that order.</summary>
public sealed record WayChoice(string Label, bool Descending, bool On);

/// <summary>
/// The Garage's Sort &amp; filter panel (its button, and Y): every stat to sort by as chips, the way round, the
/// letters A to E of body, boost and grip that leave the machines that have them, "Clear the filter"; in a lobby that
/// offers fewer machines, the others shown greyed. What it sets is the classic Garage's own order and filter
/// (GarageViewModel.SortFilter.cs), kept as before.
/// </summary>
public sealed partial class SortSheetViewModel : ViewModelBase, IGarageSheet, IDisposable
{
    public SortSheetViewModel(GarageViewModel garage)
    {
        ArgumentNullException.ThrowIfNull(garage);
        Garage = garage;
        garage.PropertyChanged += OnGarage;
        ShowWays();
    }

    public GarageViewModel Garage { get; }

    /// <summary>The order's two ways round, the one chosen on.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WayChoice> Ways { get; private set; } = [];

    /// <summary>A stat chosen to sort by.</summary>
    [RelayCommand]
    private void Choose(SortChoice? choice)
    {
        if (choice is not null)
        {
            Garage.ChooseSortCommand.Execute(choice);
        }
    }

    /// <summary>A way round chosen.</summary>
    [RelayCommand]
    private void Way(WayChoice? way)
    {
        if (way is { On: false })
        {
            Garage.FlipSortCommand.Execute(null);
        }
    }

    /// <summary>A letter chosen, or no longer.</summary>
    [RelayCommand]
    private void Letter(LetterChip? chip)
    {
        if (chip is not null)
        {
            Garage.ToggleLetterCommand.Execute(chip);
        }
    }

    /// <summary>"Clear the filter": every letter let go.</summary>
    [RelayCommand]
    private void Clear() => Garage.ClearLetters();

    public void Dispose() => Garage.PropertyChanged -= OnGarage;

    private void OnGarage(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GarageViewModel.SortOrderText))
        {
            ShowWays();
        }
    }

    private void ShowWays()
    {
        var info = MachineStats.Of(MachineStats.Has(Garage.Sort.By) ? Garage.Sort.By : MachineStat.Number);
        Ways = [new WayChoice(info.Orders.Rising, false, !Garage.Sort.Descending), new WayChoice(info.Orders.Falling, true, Garage.Sort.Descending)];
    }
}

/// <summary>A target of the colours panel ("Machine", "Body", "Trail"), on while it is the one coloured.</summary>
public sealed record ColourTarget(string Label, int Index, bool On);

/// <summary>
/// The Garage's colours panel ("+" beside the card's colours; a colour row's A in the builder): what is coloured (a
/// stock machine's: the machine or the trail; a custom machine's: a part or the trail), its own colours as swatches,
/// twelve colours to paint it with, the colour typed as hex and on the wheel (for the mouse), and "Back to the
/// original". Every colour goes where the classic Garage puts it: a machine's look, a part of a build, the racer's
/// trail; each is kept as it is made.
/// </summary>
public sealed partial class ColourSheetViewModel : ViewModelBase, IGarageSheet, IDisposable
{
    /// <summary>The twelve colours to paint with.</summary>
    public static IReadOnlyList<string> Hues { get; } =
        ["#E8432E", "#F28C28", "#F2C230", "#8BD346", "#2FBF71", "#2BC3C3", "#2E9BE8", "#3A5BE0", "#7B4FE0", "#D24FD0", "#F2F2F2", "#3A3F47"];

    private readonly GarageViewModel _garage;
    private readonly bool _custom;
    private bool _showing;

    /// <param name="target">A stock machine's: 0 the machine, 1 the trail; a custom machine's: 0-2 a part, 3 the trail.</param>
    public ColourSheetViewModel(GarageViewModel garage, int target)
    {
        ArgumentNullException.ThrowIfNull(garage);
        _garage = garage;
        _custom = garage.StagedCustom;
        IReadOnlyList<string> labels = _custom ? ["Body", "Cockpit", "Booster", "Trail"] : ["Machine", "Trail"];
        Labels = labels;
        Target = Math.Clamp(target, 0, labels.Count - 1);
        garage.PropertyChanged += OnChanged;
        garage.Builder.PropertyChanged += OnChanged;
        garage.Trail.PropertyChanged += OnChanged;
        if (OnTrail)
        {
            garage.ShowStagedTrail();
        }
        Show();
    }

    private IReadOnlyList<string> Labels { get; }

    /// <summary>The machine's name, over the panel.</summary>
    public string MachineName => _garage.StagedName;

    /// <summary>What is coloured: its place in <see cref="Targets"/>.</summary>
    [ObservableProperty]
    public partial int Target { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ColourTarget> Targets { get; private set; } = [];

    /// <summary>The target's own colours (and the player's paint once there is one).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StockSwatch> Swatches { get; private set; } = [];

    /// <summary>The panel's second step: a colour of one's own mixed on the wheel or typed as hex; false: the palette
    /// (the target's own colours and twelve to paint with).</summary>
    [ObservableProperty]
    public partial bool Mixing { get; private set; }

    /// <summary>"Mix your own colour": the wheel and the hex.</summary>
    [RelayCommand]
    private void Mix() => Mixing = true;

    /// <summary>"Use this colour": the palette again, with the colour mixed painted on.</summary>
    [RelayCommand]
    private void Mixed() => Mixing = false;

    /// <summary>B: from the wheel back to the palette.</summary>
    public bool StepBack()
    {
        if (!Mixing)
        {
            return false;
        }
        Mixing = false;
        return true;
    }

    /// <summary>The target's colour as "#RRGGBB": typed, or set by the wheel; a whole colour is painted on it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HexValid))]
    public partial string Hex { get; set; } = "";

    public bool HexValid => Rgb.Parse(Hex) is not null;

    private bool OnTrail => Target == Labels.Count - 1;

    partial void OnHexChanged(string value)
    {
        if (!_showing && Rgb.Parse(value) is { } colour)
        {
            Paint(colour);
        }
    }

    /// <summary>A target chosen.</summary>
    [RelayCommand]
    private void Choose(ColourTarget? target)
    {
        if (target is null || target.Index == Target)
        {
            return;
        }
        Target = target.Index;
        if (OnTrail)
        {
            _garage.ShowStagedTrail();
        }
        Show();
    }

    /// <summary>One of the target's own colours.</summary>
    [RelayCommand]
    private void PickSwatch(StockSwatch? swatch)
    {
        if (swatch is not { Known: true })
        {
            return;
        }
        if (OnTrail)
        {
            _garage.Trail.PickSwatchCommand.Execute(swatch.Index);
        }
        else if (_custom)
        {
            _garage.Builder.PickSwatchOf(Target, swatch.Index);
        }
        else
        {
            _garage.PickSwatchCommand.Execute(swatch.Index);
        }
        Show();
    }

    /// <summary>One of the twelve colours, painted on the target.</summary>
    [RelayCommand]
    private void PaintHue(string? hue)
    {
        if (Rgb.Parse(hue ?? "") is { } colour)
        {
            Paint(colour);
            Show();
        }
    }

    /// <summary>"Back to the original": the target's own colour (the trail: the game's own).</summary>
    [RelayCommand]
    private void Original()
    {
        if (OnTrail)
        {
            _garage.Trail.PickSwatchCommand.Execute(0);
        }
        else if (_custom)
        {
            _garage.Builder.PickSwatchOf(Target, 0);
        }
        else
        {
            _garage.PickSwatchCommand.Execute(0);
        }
        Show();
    }

    public void Dispose()
    {
        _garage.PropertyChanged -= OnChanged;
        _garage.Builder.PropertyChanged -= OnChanged;
        _garage.Trail.PropertyChanged -= OnChanged;
    }

    private void Paint(Rgb colour)
    {
        var hex = Rgb.Hex(colour);
        if (OnTrail)
        {
            _garage.Trail.ColorHex = hex;
        }
        else if (_custom)
        {
            _garage.Builder.PaintPart(Target, colour);
        }
        else
        {
            _garage.ColorHex = hex;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_showing && e.PropertyName is null or "" or nameof(GarageViewModel.Swatches) or nameof(GarageViewModel.ColorHex)
            or nameof(CustomBuilderViewModel.Build) or nameof(TrailPicker.Swatches))
        {
            Show();
        }
    }

    /// <summary>The targets, the swatches and the colour for the target as it stands.</summary>
    private void Show()
    {
        _showing = true;
        try
        {
            Targets = [.. Labels.Select((l, i) => new ColourTarget(l, i, i == Target))];
            Swatches = OnTrail ? _garage.Trail.Swatches : _custom ? _garage.Builder.SwatchesOf(Target) : _garage.Swatches;
            var hex = OnTrail ? _garage.Trail.ColorHex : _custom ? Swatches.FirstOrDefault(s => s.Selected)?.Hex ?? Hex : _garage.ColorHex;
            if (Rgb.Parse(hex) is not null && !string.Equals(hex, Hex, StringComparison.OrdinalIgnoreCase))
            {
                Hex = hex;
            }
        }
        finally
        {
            _showing = false;
        }
    }
}

/// <summary>
/// "All stats" on the Garage's card: every value of the machine on the stage, as the disc has them or as the stat
/// pack the stats are seen through has them (a value the pack changes marked, the stock one beside it); a stock
/// machine's rows sort the machines by their stat, as the classic Stats tab's do.
/// </summary>
public sealed partial class StatsSheetViewModel(GarageViewModel garage) : ViewModelBase, IGarageSheet
{
    public GarageViewModel Garage { get; } = garage ?? throw new ArgumentNullException(nameof(garage));

    public string MachineName => Garage.StagedName;

    /// <summary>A custom machine's values (summed from its parts), else the stock machine's rows.</summary>
    public bool Custom => Garage.StagedCustom;

    public bool Stock => !Custom;

    /// <summary>The disc gave no stats (or the parts are not read): the panel says so.</summary>
    public bool NoStats => Custom ? Garage.Builder.Stats.Count == 0 : Garage.NoStats;

    /// <summary>A stock machine's row pressed: the machines sorted by its stat (again: the other way round).</summary>
    [RelayCommand]
    private void SortBy(StatRow? row)
    {
        if (row is not null)
        {
            Garage.SortByCommand.Execute(row.Stat);
        }
    }
}
