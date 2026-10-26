using Avalonia.Controls;
using Avalonia.VisualTree;
using Launcher.Core;

namespace Launcher.App.Views;

// The Garage's roster and its keys (SELECT): the tile of the machine raced, a step across the tiles (the rows
// wrapping), the shelves turned by the pad's shoulders, a letter to the next machine of it; Escape closes the Sort &
// filter panel or goes back from PREVIEW, Enter on PREVIEW's stage is its primary.
public sealed partial class GarageView
{
    /// <summary>The tile of the roster whose machine is the one raced (the shelf's
    /// <see cref="ViewModels.GarageViewModel.Current"/>), else its first tile; null with no tile. The view is laid
    /// out first, so a shelf just changed has its tiles.</summary>
    public Button? CurrentCard()
    {
        UpdateLayout();   // (a shelf just changed: its tiles are made in the layout)
        var cards = RosterCards().ToList();
        return _garage?.Current() is { } current ? cards.FirstOrDefault(b => ReferenceEquals(b.DataContext, current)) ?? cards.FirstOrDefault()
            : cards.FirstOrDefault();
    }

    /// <summary>The roster's tiles on show, in their order (New machine last on Custom).</summary>
    public IEnumerable<Button> RosterCards() =>
        this.FindControl<ItemsControl>("Roster")!.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cell") && b.IsEffectivelyVisible);

    /// <summary>Whether a control is one of the roster's tiles.</summary>
    public static bool IsCard(Control control) =>
        control is Button b && b.Classes.Contains("cell") && b.FindAncestorOfType<GarageView>() is not null;

    /// <summary>A step across the roster from a tile (the arrow keys, the pad's D-pad sideways): <paramref name="by"/>
    /// tiles on in the shelf's order, the rows wrapping at their ends; the tile reached takes the focus. Nothing is
    /// picked: a tile is opened by pressing it. Null at the roster's ends.</summary>
    public Button? StepRoster(Control card, int by)
    {
        ArgumentNullException.ThrowIfNull(card);
        var cards = RosterCards().ToList();
        var at = cards.IndexOf(card as Button ?? card.FindAncestorOfType<Button>(includeSelf: true)!);
        var to = at + by;
        if (at < 0 || to < 0 || to >= cards.Count)
        {
            return null;
        }
        cards[to].Focus(Avalonia.Input.NavigationMethod.Directional);
        return cards[to];
    }

    /// <summary>Whether a tile stands first in its row (the pad's D-pad left leaves the roster there).</summary>
    public bool AtRowStart(Control card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var at = RosterCards().ToList().IndexOf(card as Button ?? card.FindAncestorOfType<Button>(includeSelf: true)!);
        return at >= 0 && at % Math.Max(1, RosterColumns) == 0;
    }

    /// <summary>The shelf before or after (the pad's shoulders on a tile); this view, its roster laid out, when it
    /// changed; null at an end.</summary>
    public GarageView? TurnShelf(int by)
    {
        if (_garage is not { Selecting: true } garage)
        {
            return null;
        }
        var to = Math.Clamp(garage.Shelf + by, ViewModels.GarageViewModel.FavouritesShelf, ViewModels.GarageViewModel.CustomShelf);
        if (to == garage.Shelf)
        {
            return null;
        }
        garage.ShowShelfCommand.Execute(to);
        UpdateLayout();
        return this;
    }

    /// <summary>The next tile, after the one in focus, whose name begins with <paramref name="letter"/> (round to the
    /// first); null when none does.</summary>
    public Button? Jump(char letter, Control? from)
    {
        var cards = RosterCards().ToList();
        var at = from is null ? -1 : cards.IndexOf(from as Button ?? from.FindAncestorOfType<Button>(includeSelf: true)!);
        for (var k = 1; k <= cards.Count; k++)
        {
            var card = cards[(at + k + cards.Count) % cards.Count];
            var name = card.DataContext switch
            {
                ViewModels.GarageCard g => Machines.Names[g.Id],
                ViewModels.CustomCard c => c.Name,
                _ => "",
            };
            if (name.Length > 0 && char.ToUpperInvariant(name[0]) == char.ToUpperInvariant(letter))
            {
                card.Focus(Avalonia.Input.NavigationMethod.Directional);
                return card;
            }
        }
        return null;
    }

    private void OnKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Handled || e.Source is not Control source || source is TextBox || source.FindAncestorOfType<TextBox>() is not null)
        {
            return;
        }
        if (e.Key == Avalonia.Input.Key.Escape && _garage is { SortFilterOpen: true } sorting)
        {
            sorting.CloseSortFilterCommand.Execute(null);
            this.FindControl<Button>("SortFilter")!.Focus(Avalonia.Input.NavigationMethod.Directional);
            e.Handled = true;
            return;
        }
        if (e.Key == Avalonia.Input.Key.Escape && _garage is { Previewing: true } garage)
        {
            garage.BackCommand.Execute(null);   // (the keyboard back on the tile: FocusPhase)
            e.Handled = true;
            return;
        }
        if (e.Key == Avalonia.Input.Key.Enter && _garage is { Previewing: true, UseShown: true } previewing
            && source.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            previewing.UseMachineCommand.Execute(null);   // (Enter on the stage: the primary)
            e.Handled = true;
            return;
        }
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not { } card || !IsCard(card))
        {
            return;
        }
        var by = e.Key switch
        {
            Avalonia.Input.Key.Left => -1,
            Avalonia.Input.Key.Right => 1,
            Avalonia.Input.Key.Up => -Math.Max(1, RosterColumns),
            Avalonia.Input.Key.Down => Math.Max(1, RosterColumns),
            _ => 0,
        };
        if (by != 0)
        {
            StepRoster(card, by);
            e.Handled = true;   // (an end keeps the focus where it is)
        }
        else if (e.Key is >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            Jump((char)('A' + (e.Key - Avalonia.Input.Key.A)), card);
            e.Handled = true;
        }
    }
}
