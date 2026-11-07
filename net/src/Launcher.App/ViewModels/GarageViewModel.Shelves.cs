using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The Garage's shelves: Favourites (the marked stock machines and the marked custom
// ones), Stock (the 41) and Custom (the saved custom machines, the builder open over them), each with its count; on
// every shelf a search box and the letters chosen (Sort & filter) that leave the machines that match, through the
// stats' lens.
// The shelf changes what stands under the shelves' row and what the 3D view and its column show, never where.
public sealed partial class GarageViewModel
{
    public const int FavouritesShelf = 0, StockShelf = 1, CustomShelf = 2;

    /// <summary>The three shelves' tabs, with their counts.</summary>
    public IReadOnlyList<ShelfTab> ShelfTabs { get; } = [new("Favourites", FavouritesShelf), new("Stock", StockShelf), new("Custom", CustomShelf)];

    /// <summary>The shelf on show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StockShown), nameof(CustomShown), nameof(FavouritesShown))]
    public partial int Shelf { get; set; } = StockShelf;

    /// <summary>A stock machine in the 3D view and its column, and the cells: on the Favourites and Stock shelves.</summary>
    public bool StockShown => Shelf != CustomShelf;

    public bool CustomShown => Shelf == CustomShelf;

    public bool FavouritesShown => Shelf == FavouritesShelf;

    /// <summary>What the search box holds.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>The search and the letters chosen as they stand (the letters: GarageViewModel.SortFilter.cs).</summary>
    public MachineFilter Filter { get; private set; } = new();

    /// <summary>The custom machines' cards under the shelves' row: on Custom every saved one, on Favourites the marked
    /// ones; each in the order chosen, that the search and letters leave (a card pressed opens it on the Custom
    /// shelf, and races it).</summary>
    public ObservableCollection<CustomCard> ShelfCustoms { get; } = [];

    /// <summary>The shelf shows nothing: its words ("No favourites", "No match") stand in the cells' place.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    partial void OnShelfChanged(int value)
    {
        foreach (var tab in ShelfTabs)
        {
            tab.On = tab.Shelf == value;
        }
        if (value != CustomShelf && _state.Settings.FavouritesOnly != (value == FavouritesShelf))
        {
            _state.Update(s => s with { FavouritesOnly = value == FavouritesShelf });   // (the shelf the page opens on)
        }
        ShowChoices();
        ShowSections();
    }

    partial void OnQueryChanged(string value)
    {
        Filter = Filter with { Query = value ?? "" };
        ShowChoices();
    }

    /// <summary>The greyed Custom shelf's word; null: it is open.</summary>
    public string? CustomsOffText => CustomsOff ? CustomRules.NoCustoms : null;

    partial void OnCustomsOffChanged(bool value) => OnPropertyChanged(nameof(CustomsOffText));

    /// <summary>A shelf's tab pressed.</summary>
    [RelayCommand]
    private void ShowShelf(int shelf)
    {
        if (shelf is >= FavouritesShelf and <= CustomShelf && !(shelf == CustomShelf && CustomsOff))
        {
            Shelf = shelf;
        }
    }

    /// <summary>The shelf the page opens on: Custom while a custom machine is raced, Favourites when the player
    /// left it there (and something is marked), else Stock.</summary>
    private int FirstShelf() => _state.Settings.Racer.Custom is not null ? CustomShelf
        : _state.Settings.FavouritesOnly && (HasFavourites || (_state.Settings.Racer.Customs ?? []).Any(c => c.Favourite)) ? FavouritesShelf
        : StockShelf;

    /// <summary>The counts on the tabs, the marked custom machines and the words for an empty shelf.</summary>
    private void ShowShelves()
    {
        if (Builder is null)
        {
            return;   // (still being made)
        }
        var customs = StockShelf == Shelf || CustomsOff ? [] : SortedCustoms()
            .Where(c => (CustomShown || c.Favourite) && (Filter.Open || Filter.Matches(c.Name, Builder.ValuesOf(c.Index)))).ToList();
        if (!customs.SequenceEqual(ShelfCustoms))
        {
            ShelfCustoms.Clear();
            foreach (var card in customs)
            {
                ShelfCustoms.Add(card);
            }
        }
        var offered = Enumerable.Range(0, Machines.Names.Count).Count(Offers);
        var marked = _cards.Count(c => c.Favourite) + (CustomsOff ? 0 : Builder.Saved.Count(c => c.Favourite));
        ShelfTabs[FavouritesShelf].Count = marked.ToString(CultureInfo.InvariantCulture);
        ShelfTabs[StockShelf].Count = Offer is null ? Machines.Names.Count.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{offered} / {Machines.Names.Count}");
        ShelfTabs[CustomShelf].Count = Builder.CountText;
        EmptyText = Choices.Count > 0 || ShelfCustoms.Count > 0 ? ""
            : FavouritesShown && marked == 0 ? "No favourites"
            : CustomShown && Builder.Saved.Count == 0 ? "No custom machines" : "No match";
        ShowRoster();
    }

    /// <summary>The saved custom machines in the order the stock ones are in (by number: as saved), through the lens.</summary>
    private IEnumerable<CustomCard> SortedCustoms()
    {
        if (Sort.By == MachineStat.Number || !MachineStats.Has(Sort.By))
        {
            return Sort.Descending ? Builder.Saved.Reverse() : Builder.Saved;
        }
        var values = Builder.Saved.ToDictionary(c => c, c => Builder.ValuesOf(c.Index)(Sort.By));
        var known = Builder.Saved.Where(c => values[c] is not null);
        return [.. (Sort.Descending ? known.OrderByDescending(c => values[c]) : known.OrderBy(c => values[c])).ThenBy(c => c.Index),
            .. Builder.Saved.Where(c => values[c] is null)];
    }
}

/// <summary>A shelf's tab: its name, its count ("41", "12 / 41") and whether it shows.</summary>
public sealed partial class ShelfTab(string label, int shelf) : ObservableObject
{
    public string Label { get; } = label;

    public int Shelf { get; } = shelf;

    [ObservableProperty]
    public partial string Count { get; set; } = "";

    [ObservableProperty]
    public partial bool On { get; set; }
}
