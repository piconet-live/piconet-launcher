using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Garage's Sort & filter panel (a row of every letter says little: a small panel sorts and filters by any
// stat): SELECT's one control for the order and the
// letters, floating over the roster. SORT: every stat the Stats tab shows, one chosen, and the way round; it writes the
// same order the Stats tab's rows do (Sort). FILTER: for body, boost and grip the letters A to E, any of them (none:
// every letter). The order in words beside the Stats choice opens it too.
public sealed partial class GarageViewModel
{
    /// <summary>The panel shows.</summary>
    [ObservableProperty]
    public partial bool SortFilterOpen { get; set; }

    /// <summary>Every stat to sort by, the one sorted by on.</summary>
    public IReadOnlyList<SortChoice> SortChoices { get; } = [.. MachineStats.Shown.Select(s => new SortChoice(s))];

    /// <summary>The order's way round in words ("A to E", "high to low"): the panel's switch.</summary>
    [ObservableProperty]
    public partial string SortOrderText { get; set; } = "";

    /// <summary>Body, boost and grip, each with its letters.</summary>
    public IReadOnlyList<GradeRow> Grades { get; } =
        [.. MachineFilter.Graded.Select(g => new GradeRow(g.Stat, g.Label))];

    /// <summary>How many letters are chosen, beside the panel's button ("" for none).</summary>
    [ObservableProperty]
    public partial string FilterCountText { get; set; } = "";

    /// <summary>The panel's button, or the order in words: the panel, or closed again.</summary>
    [RelayCommand]
    private void ToggleSortFilter() => SortFilterOpen = !SortFilterOpen;

    [RelayCommand]
    private void CloseSortFilter() => SortFilterOpen = false;

    /// <summary>A stat chosen in the panel: the machines by it, the way round it is first shown (best or largest
    /// first); chosen again, nothing changes (the switch turns it round).</summary>
    [RelayCommand]
    private void ChooseSort(SortChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Stat != Sort.By)
        {
            ApplySort(FirstWay(choice.Stat));
        }
    }

    /// <summary>The panel's switch: the order the other way round.</summary>
    [RelayCommand]
    private void FlipSort() => ApplySort(Sort with { Descending = !Sort.Descending });

    /// <summary>A letter pressed: chosen, or no longer.</summary>
    [RelayCommand]
    private void ToggleLetter(LetterChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        Filter = Filter.Toggled(chip.Stat, chip.Index);
        ShowLetters();
        ShowChoices();
    }

    /// <summary>A stat's first way round: best or largest first (A to E, high to low), the number as the game has it.</summary>
    private static MachineSort FirstWay(MachineStat stat) =>
        new(stat, MachineStats.Of(stat).Kind is MachineStatKind.Value or MachineStatKind.Weight);

    /// <summary>The order chosen (here or on the Stats tab), kept in the settings.</summary>
    private void ApplySort(MachineSort sort)
    {
        if (!MachineStats.Has(sort.By))
        {
            return;
        }
        Sort = sort;
        ShowSort();
        if (_state.Settings.GarageSort != Sort)
        {
            _state.Update(s => s with { GarageSort = Sort });
        }
    }

    /// <summary>The panel's marks for the order: the stat on, the way round in words.</summary>
    private void ShowSortChoices(MachineStatInfo info)
    {
        foreach (var choice in SortChoices)
        {
            choice.On = choice.Stat == info.Stat;
        }
        SortOrderText = Sort.Descending ? info.Orders.Falling : info.Orders.Rising;
    }

    /// <summary>The letters' marks and their count.</summary>
    private void ShowLetters()
    {
        foreach (var row in Grades)
        {
            foreach (var letter in row.Letters)
            {
                letter.On = Filter.Has(row.Stat, letter.Index);
            }
        }
        FilterCountText = Filter.LetterCount > 0 ? Filter.LetterCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    }
}

/// <summary>A stat to sort by in the Sort &amp; filter panel, on while the machines are sorted by it.</summary>
public sealed partial class SortChoice(MachineStatInfo info) : ObservableObject
{
    public MachineStat Stat { get; } = info.Stat;

    public string Label { get; } = info.Label;

    [ObservableProperty]
    public partial bool On { get; set; }
}

/// <summary>A graded stat's row of letters in the Sort &amp; filter panel.</summary>
public sealed class GradeRow(MachineStat stat, string label)
{
    public MachineStat Stat { get; } = stat;

    public string Label { get; } = label;

    public IReadOnlyList<LetterChip> Letters { get; } =
        [.. MachineFilter.Letters.Select((c, i) => new LetterChip(stat, i, c.ToString(), label))];
}

/// <summary>A letter of a graded stat, chosen or not.</summary>
public sealed partial class LetterChip(MachineStat stat, int index, string letter, string of) : ObservableObject
{
    public MachineStat Stat { get; } = stat;

    /// <summary>0 for A.</summary>
    public int Index { get; } = index;

    public string Letter { get; } = letter;

    /// <summary>"Body A", for a screen reader.</summary>
    public string Name { get; } = $"{of} {letter}";

    [ObservableProperty]
    public partial bool On { get; set; }
}
