using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The builder's part in the Garage's stats lens: the stats seen through the Garage's
// stat pack (the parts' blocks as the pack leaves them, summed as a race of that pack sums them; a changed value shows
// with the stock one beside it), the three letters the game gives a parts machine, and the heart that puts a saved
// machine on the Favourites shelf.
public sealed partial class CustomBuilderViewModel
{
    /// <summary>The stat pack the Garage's stats are seen through (its Stats choice, or a lobby's pack); null: the
    /// disc's own.</summary>
    [ObservableProperty]
    public partial StatPack? Pack { get; set; }

    partial void OnPackChanged(StatPack? value) => ShowStats();

    /// <summary>The cards changed: their names, marks or number.</summary>
    public event Action? SavedChanged;

    /// <summary>The open machine is a saved one: it can be marked.</summary>
    public bool CanFavourite => Editing is { } i && i < (_state.Settings.Racer.Customs ?? []).Count;

    /// <summary>The open saved machine is marked with the heart.</summary>
    public bool Favourite => Editing is { } i && (_state.Settings.Racer.Customs ?? []) is var saved && i < saved.Count && saved[i].Favourite;

    /// <summary>The heart over the 3D view: the open saved machine marked, or no longer.</summary>
    [RelayCommand]
    private void ToggleFavourite()
    {
        var saved = (_state.Settings.Racer.Customs ?? []).ToList();
        if (Editing is not { } i || i >= saved.Count)
        {
            return;
        }
        saved[i] = saved[i] with { Favourite = !saved[i].Favourite };
        _state.Update(s => s with { Racer = s.Racer with { Customs = saved } });
        ShowSaved();
    }

    /// <summary>The line over the 3D view: the build's letters and its weight, as a stock machine's.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatFigure> Summary { get; set; } = [];

    /// <summary>A saved machine's values as the Garage shows them (through <see cref="Pack"/>), for its search and
    /// chips; null where not known.</summary>
    public Func<MachineStat, float?> ValuesOf(int index)
    {
        var saved = _state.Settings.Racer.Customs ?? [];
        var row = index >= 0 && index < saved.Count ? _parts?.Stats(saved[index], Pack) : null;
        return stat => row is not null && (int)stat < row.Length ? row[(int)stat] : null;
    }

    /// <summary>The build's stats through the pack, each with the stock value beside it when the pack changes it, and
    /// its letters first, as the game's machine select shows them.</summary>
    private void ShowStats()
    {
        var row = _parts?.Stats(Build, Pack);
        var stock = Pack is null ? row : _parts?.Stats(Build);
        // over the 3D view, as over a stock machine's: its letters and its weight
        MachineStat[] shown = [MachineStat.BodyRating, MachineStat.BoostRating, MachineStat.GripRating, MachineStat.Weight];
        Summary = row is null ? [] :
        [
            .. shown.Where(s => (int)s < row.Length && row[(int)s] is not null)
                .Select(s => new StatFigure(s == MachineStat.Weight ? "" : MachineStats.Of(s).Label, MachineStats.Text(s, row[(int)s]))),
        ];
        Stats = row is null ? [] :
        [
            .. MachineStats.Shown.Where(s => s.Stat != MachineStat.Number && row[(int)s.Stat] is not null).Select(s =>
            {
                var (value, was) = (row[(int)s.Stat], stock?[(int)s.Stat]);
                var changed = Pack is not null && was is not null && value != was;
                return new StatFigure(s.Label, MachineStats.Text(s.Stat, value), changed ? MachineStats.Text(s.Stat, was) : "", changed);
            }),
        ];
    }
}
