using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Garage's favourites (a heart over the 3D view; the Favourites shelf shows the marked ones: GarageViewModel.Shelves.cs)
// and its part in a lobby (the lobby's machine stage is the Garage): the machines the server
// offers (the others hidden, or with "All 41" shown greyed and never picked), the
// range it keeps the setting in (GarageViewModel.Setting.cs), its stat pack (GarageViewModel.Lens.cs), and the
// machine picked told to the lobby.
public sealed partial class GarageViewModel
{
    /// <summary>The player picked a machine (a cell pressed, the arrow keys): their machine from then on. Not
    /// raised when the page itself moves the selection (<see cref="Select"/>).</summary>
    public event Action<int>? Picked;

    /// <summary>A machine's look was kept in the settings (a moment after its last change).</summary>
    public event Action? LookSaved;

    /// <summary>The look of the machine being set up changed (its colour, its setting), at once: before it is kept.</summary>
    public event Action? LookChanged;

    /// <summary>A machine is marked: the Favourites shelf has something to show.</summary>
    [ObservableProperty]
    public partial bool HasFavourites { get; set; }

    /// <summary>The machine being set up is marked.</summary>
    public bool SelectedFavourite => MachineIndex >= 0 && MachineIndex < _cards.Length && _cards[MachineIndex].Favourite;

    /// <summary>The machines a lobby's server offers (ids); null: every machine (no lobby, or its server allows all).</summary>
    public IReadOnlyList<int>? Offer { get; private set; }

    /// <summary>The lobby offers fewer than all: the "All 41" switch shows.</summary>
    [ObservableProperty]
    public partial bool OfferLimited { get; set; }

    /// <summary>The "All 41" switch: the machines the lobby does not offer shown too, greyed, never picked.</summary>
    [ObservableProperty]
    public partial bool ShowAll { get; set; }

    /// <summary>Whether a machine can be picked here: every one out of a lobby, the offered ones in it.</summary>
    public bool Offers(int id) => Machines.Has(id) && Offer?.Contains(id) != false;

    /// <summary>The heart of a machine pressed: marked, or no longer.</summary>
    [RelayCommand]
    private void ToggleFavourite(int id)
    {
        if (!Machines.Has(id))
        {
            return;
        }
        var marked = (_state.Settings.FavouriteMachines ?? []).ToList();
        if (!marked.Remove(id))
        {
            marked.Add(id);
        }
        _state.Update(s => s with { FavouriteMachines = marked });
        ShowFavourites();
        ShowChoices();
    }

    /// <summary>The heart over the 3D view: the machine being set up.</summary>
    [RelayCommand]
    private void ToggleSelectedFavourite() => ToggleFavourite(MachineIndex);

    /// <summary>The "All 41" switch pressed.</summary>
    [RelayCommand]
    private void ToggleShowAll()
    {
        ShowAll = !ShowAll;
        ShowChoices();
    }

    /// <summary>The lobby's part: the machines on offer, the range its server keeps the setting in, and the stat pack
    /// its races run on, whose values stand over the disc's in the stats and the order; all null out of a lobby.</summary>
    /// <param name="packLabel">The pack as the lobby's line names it (its name and author); shown as the stats'
    /// fixed choice while the pack's values are.</param>
    public void InLobby(IReadOnlyList<int>? offer, Online.Protocol.SettingRange? setting, Online.Protocol.StatPack? pack = null, string packLabel = "")
    {
        SettingRange = setting ?? Online.Protocol.SettingRange.Free;
        var inLobby = offer is not null || setting is not null || pack is not null
            || _state.Session is { Lobby: not null, ConnectedTo: not null };
        LensOfLobby(inLobby, pack, packLabel);
        var all = offer is null || Enumerable.Range(0, Machines.Names.Count).All(offer.Contains);
        var next = all ? null : offer;
        if ((Offer is null) != (next is null) || (Offer is not null && next is not null && !Offer.SequenceEqual(next)))
        {
            Offer = next;
            OfferLimited = next is not null;
            ShowAll = ShowAll && OfferLimited;
            ShowChoices();
        }
    }

    /// <summary>The page's own move of the selection (a lobby joined, a machine choice opening): not a pick; SELECT
    /// again, on that machine.</summary>
    public void Select(int machine)
    {
        LeavePreview();
        if (Machines.Has(machine) && machine != MachineIndex)
        {
            MachineIndex = machine;
        }
    }

    private void ShowFavourites()
    {
        var marked = _state.Settings.FavouriteMachines ?? [];
        foreach (var card in _cards)
        {
            card.Favourite = marked.Contains(card.Id);
        }
        HasFavourites = marked.Count > 0;
        OnPropertyChanged(nameof(SelectedFavourite));
    }

    /// <summary>The cells on show, in their order: the shelf's machines (Stock all, Favourites the marked ones, Custom
    /// none: its cards are <see cref="ShelfCustoms"/>) in
    /// the order chosen that the search and chips leave; in a lobby the ones it offers, and with "All 41" the others
    /// too, greyed.</summary>
    private List<GarageCard> Wanted()
    {
        var table = Table;
        foreach (var card in _cards)
        {
            card.Offered = Offer?.Contains(card.Id) != false;
        }
        return
        [
            .. Sort.Order(table).Select(id => _cards[id])
                .Where(c => Shelf != CustomShelf)
                .Where(c => c.Offered || ShowAll)
                .Where(c => Shelf != FavouritesShelf || c.Favourite)
                .Where(c => Filter.Open || Filter.Matches(Machines.Names[c.Id], s => table?.Value(c.Id, s))),
        ];
    }

    /// <summary>Brings the cells to <see cref="Wanted"/> with as few changes as it takes (a cell that stays is not
    /// taken off the page), and the shelves' counts to them.</summary>
    private void ShowChoices()
    {
        var wanted = Wanted();
        for (var i = Choices.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Choices[i]))
            {
                Choices.RemoveAt(i);
            }
        }
        for (var place = 0; place < wanted.Count; place++)
        {
            var at = Choices.IndexOf(wanted[place]);
            if (at < 0)
            {
                Choices.Insert(place, wanted[place]);
            }
            else if (at != place)
            {
                Choices.Move(at, place);
            }
        }
        ShowShelves();
    }
}
