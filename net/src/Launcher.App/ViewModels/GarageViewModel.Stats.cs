using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Garage's stats: the machine's values as the game's own disc gives them (MachineStatTable, read once in the
// background: nothing of the game is in the launcher), and the order of the machines' cells by one of them. The
// order is kept in the settings, and it orders the cells only: a machine is its id everywhere else.
public sealed partial class GarageViewModel
{
    private readonly Func<Task<MachineStatTable?>> _readStats;
    private MachineStatTable? _table;                      // the disc's
    private MachineStatTable? _packed;                     // the disc's table with the lens's pack over it
    private string? _packedBy;                             // that pack's fingerprint

    /// <summary>The stats shown, sorted and filtered by: the disc's, or seen through a stat pack (GarageViewModel.Lens.cs)
    /// that pack's values over them (the 19 numbers of the 41 machines; the letters stay the disc's, as in the game
    /// itself, whose letters are another table than the one a pack changes).</summary>
    private MachineStatTable? Table
    {
        get
        {
            if (LensPack is not { } pack || _table is null)
            {
                return _table;
            }
            if (_packedBy != pack.Fingerprint || _packed is null)
            {
                (_packed, _packedBy) = (_table.With(pack), pack.Fingerprint);
            }
            return _packed;
        }
    }
    private string? _statsDisc;
    private int _reads;       // counts readings, so the last one's answer is the one shown

    /// <summary>The disc gave the machines' stats (without them the page says where they come from).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoStats))]
    public partial bool HasStats { get; set; }

    public bool NoStats => !HasStats;

    /// <summary>Every stat the page lists, with the machine's value; one is pressed to sort by it.</summary>
    public IReadOnlyList<StatRow> Stats { get; } = [.. MachineStats.Shown.Select(s => new StatRow(s))];

    /// <summary>What the game's own machine select shows of the machine: body, boost, grip and the weight.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Summary { get; set; } = [];

    /// <summary>The order the cells are in.</summary>
    public MachineSort Sort { get; private set; }


    /// <summary>The order in words, on the page's heading row: "Sorted by number", "Sorted by weight, high to low".</summary>
    [ObservableProperty]
    public partial string SortText { get; set; } = "";

    /// <summary>A stat's row was pressed: the machines by that stat; pressed again, the other way round. A stat is
    /// first shown best or largest first (A to E, high to low), the number as the game has it.</summary>
    [RelayCommand]
    private void SortBy(MachineStat stat)
    {
        if (!MachineStats.Has(stat))
        {
            return;
        }
        ApplySort(Sort.By == stat ? Sort with { Descending = !Sort.Descending } : FirstWay(stat));
    }

    /// <summary>The cells in the order chosen, and the order in words.</summary>
    private void ShowSort()
    {
        var info = MachineStats.Of(MachineStats.Has(Sort.By) ? Sort.By : MachineStat.Number);
        var order = Sort.Descending ? info.Orders.Falling : info.Orders.Rising;
        SortText = Sort == MachineSort.ByNumber ? "Sorted by number" : $"Sorted by {info.Label.ToLowerInvariant()}, {order}";
        foreach (var row in Stats)
        {
            row.Sorted = row.Stat == info.Stat;
            row.Order = row.Sorted ? order : "";
        }
        ShowSortChoices(info);
        ShowStats();   // (the line over the 3D view names the sorted stat's value)
        ShowChoices();
    }

    /// <summary>The machine's values in the list and over the 3D view.</summary>
    private void ShowStats()
    {
        var machine = MachineIndex;
        var (table, stock) = (Table, _table);
        foreach (var row in Stats)
        {
            row.Value = table?.Text(machine, row.Stat) ?? "";
            var (value, was) = (table?.Value(machine, row.Stat), stock?.Value(machine, row.Stat));
            row.Changed = !ReferenceEquals(table, stock) && was is not null && value != was;
            row.Stock = row.Changed ? stock!.Text(machine, row.Stat) : "";
        }
        // body, boost, grip and the weight, as the game's machine select; and the stat the machines are sorted by,
        // when it is another: the one place its value is shown, for the machine selected (never on a machine's cell)
        var shown = new List<MachineStat> { MachineStat.BodyRating, MachineStat.BoostRating, MachineStat.GripRating, MachineStat.Weight };
        if (MachineStats.Has(Sort.By) && Sort.By != MachineStat.Number && !shown.Contains(Sort.By))
        {
            shown.Add(Sort.By);
        }
        Summary = table is null ? [] :
        [
            .. shown.Select(s => new StatFigure(s == MachineStat.Weight ? "" : MachineStats.Of(s).Label, table.Text(machine, s)))
                .Where(f => f.Value.Length > 0),
        ];
    }

    private async Task ReadStatsAsync()
    {
        _statsDisc = _state.Settings.DiscPath;
        var read = ++_reads;
        var table = await _readStats();
        if (read != _reads)
        {
            return;   // another disc since
        }
        (_table, _packed) = (table, null);
        HasStats = table is not null;
        ShowStats();
        ShowSort();
    }

    /// <summary>The settings name another disc: its stats. The look's own handler of the change has loaded that disc
    /// by now, because it was there before this page was made (<see cref="App.Pages"/>).</summary>
    private void ReadStatsOfAnotherDisc()
    {
        if (_statsDisc != _state.Settings.DiscPath)
        {
            _ = ReadStatsAsync();
        }
    }
}

/// <summary>A stat on the Garage page: its label, the shown machine's value, and, when the machines are sorted by it,
/// the order in words ("high to low").</summary>
public sealed partial class StatRow(MachineStatInfo info) : ObservableObject
{
    public MachineStat Stat { get; } = info.Stat;

    public string Label { get; } = info.Label;

    /// <summary>What the row says to a screen reader, and when it is pointed at.</summary>
    public string Hint { get; } = $"Sort the machines by {info.Label.ToLowerInvariant()}";

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    /// <summary>The stock value, beside a value the stat pack seen through changes; "" otherwise.</summary>
    [ObservableProperty]
    public partial string Stock { get; set; } = "";

    /// <summary>The stat pack seen through changes the value (a dot marks it).</summary>
    [ObservableProperty]
    public partial bool Changed { get; set; }

    [ObservableProperty]
    public partial bool Sorted { get; set; }

    [ObservableProperty]
    public partial string Order { get; set; } = "";
}

/// <summary>A value with its label ("Body", "B"; over the 3D view the weight has no label); seen through a stat pack
/// that changes it, <see cref="Changed"/> and the stock value beside it.</summary>
public sealed record StatFigure(string Label, string Value, string Stock = "", bool Changed = false);
