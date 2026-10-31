using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The lobby page. The view's own part: which state's row heights apply (a class), how tall the vote's
/// pictures can be for the window (so that the six courses, the strip, the 30 seats and the chat fit without
/// scrolling down to the window's least height), the Garage made while the page shows it, the keyboard there (the
/// arrow keys move between the tiles, Enter opens one, Enter elsewhere is Ready), and a standing pressed to follow its racer.</summary>
public sealed partial class LobbyView : UserControl
{
    // What stands on the page besides the part that gives way, in each state (the page's padding under it, the line
    // of 32 px and its gap of 16, the racers' list of 10 rows with its title): for the vote two rows of cards, each
    // with 72 px of lines and gaps under its picture (the cards' margins, 12 in all: -4 over the first row, whose
    // room for the lift stands in the line's gap, and 16 under the last); while gathering the votes' row (48 and a
    // gap of 16 under its line) over 15 seats.
    private const double LineRest = 48;
    // The vote's rest is 24 px more than its parts add up to: measured on Windows at the least window (1280 x 720),
    // where the page scrolled by 23 px without it. The racers' list with its title and the chat's box take 244
    // (measured headless at 1280 x 720: the page scrolled by 16 px with 228).
    private const double VoteRest = 20 + LineRest + 12 + (2 * 72) + 244 + 24;
    private const double GatheringRest = 20 + LineRest + 65 + 28;

    /// <summary>Narrower than this the vote's three columns of seats leave a name and its tag no room beside the
    /// ping: the list takes two columns where the page is tall enough for their 15 rows, else three without the
    /// pings (a name and its tag come first).</summary>
    private const double Narrow = 1100;

    /// <summary>What a vote's list of two columns adds (15 rows of 20 px, not 10), and the least height of the vote's
    /// pictures that is worth it.</summary>
    private const double TwoColumnsRest = (5 * 20) + 12;
    private const double TwoColumnsPicture = 64;

    private ScrollViewer? _page;
    private LobbyViewModel? _lobby;
    private GarageView? _garage;

    public LobbyView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Watch();
        // (handled keys too: a cell that has the keyboard takes Enter for itself)
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _page = this.FindAncestorOfType<ScrollViewer>();
        if (_page is not null)
        {
            _page.SizeChanged += OnPageSized;
        }
        Fit();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_page is not null)
        {
            _page.SizeChanged -= OnPageSized;
        }
        _page = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPageSized(object? sender, SizeChangedEventArgs e) => Fit();

    private void Watch()
    {
        if (_lobby is not null)
        {
            _lobby.PropertyChanged -= OnLobbyChanged;
        }
        _lobby = DataContext as LobbyViewModel;
        if (_lobby is not null)
        {
            _lobby.PropertyChanged += OnLobbyChanged;
        }
        Classes.Set("gathering", _lobby?.Gathering == true);
        Classes.Set("choosing", _lobby?.Choosing == true);
        ShowGarage();
        Fit();
    }

    private void OnLobbyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LobbyViewModel.Screen))
        {
            Classes.Set("gathering", _lobby?.Gathering == true);
            Classes.Set("choosing", _lobby?.Choosing == true);
            Fit();
        }
        else if (e.PropertyName == nameof(LobbyViewModel.GarageShown))
        {
            ShowGarage();
            Fit();
        }
    }

    /// <summary>The Garage under the lobby's line, there only while the page shows it (its 3D view is the graphics
    /// card's work): the launcher's own Garage page, without its title, the selected machine's cell holding the
    /// keyboard so that the arrow keys and Enter work at once.</summary>
    private void ShowGarage()
    {
        var place = this.FindControl<Panel>("GaragePlace")!;
        if (_lobby is { GarageShown: true } lobby)
        {
            if (_garage is null || _garage.DataContext != lobby.Garage)
            {
                place.Children.Clear();
                _garage = new GarageView { DataContext = lobby.Garage, Titled = false };
                place.Children.Add(_garage);
                lobby.Garage.Roster.CollectionChanged -= OnCellsMoved;
                lobby.Garage.Roster.CollectionChanged += OnCellsMoved;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(FocusSelected, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        else if (_garage is not null)
        {
            (_garage.DataContext as GarageViewModel)?.Roster.CollectionChanged -= OnCellsMoved;
            place.Children.Clear();
            _garage = null;
        }
    }

    /// <summary>The cells were put in another order, or some came or went (the stats arriving, the heart's filter):
    /// a cell that is moved leaves the page for a moment, and if it had the keyboard, nothing has it afterwards and
    /// the arrow keys and Enter go nowhere. So when nothing has the keyboard after that, the selected machine's
    /// cell takes it.</summary>
    private void OnCellsMoved(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            {
                FocusSelected();
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>How long after a key typed into a box (the chat's message) the page does not take the keyboard for
    /// a machine's cell, and how long after the machine stage opens Enter is not Ready: the stage's page takes the
    /// chat's box away, and an Enter meant to send a message must not be the player's Ready.</summary>
    private static readonly TimeSpan TypingLasts = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EnterWaits = TimeSpan.FromSeconds(1);

    private DateTimeOffset _typedAt;

    /// <summary>The selected machine's cell takes the keyboard (the first cell, when the heart's filter hides the
    /// selected machine), unless a box has it or was being typed in a moment ago.</summary>
    private void FocusSelected()
    {
        if (_garage is { } garage && _lobby is { GarageShown: true } lobby
            && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox
            && DateTimeOffset.UtcNow - _typedAt > TypingLasts)
        {
            garage.CurrentCard()?.Focus();
        }
    }

    /// <summary>What gives way to the window's height: the vote's pictures (96 px at most, 40 at least), a seat's row
    /// while gathering (26 to 20). (The Garage fits its own view and cells under the lobby's lines: GarageView.)</summary>
    private void Fit()
    {
        if (_lobby is not { } lobby || _page is not { Bounds.Height: > 0 } page)
        {
            return;
        }
        var (width, height) = (page.Bounds.Width, page.Bounds.Height);
        var narrow = width < Narrow;
        lobby.Narrow = narrow && Math.Floor((height - VoteRest - TwoColumnsRest) / 2) >= TwoColumnsPicture;
        lobby.PictureHeight = Math.Clamp(Math.Floor((height - VoteRest - (lobby.Narrow ? TwoColumnsRest : 0)) / 2), 40, 96);
        lobby.SeatHeight = lobby.Gathering ? Math.Clamp(Math.Floor((height - GatheringRest) / 15), 20, 26) : 20;
        Classes.Set("noping", narrow && !lobby.Narrow);
    }

    /// <summary>The keyboard in the lobby's Garage. Enter is Ready, while a machine choice is on, from anywhere that
    /// does nothing with it; not from a box being typed in, not from a button (a tile of the roster opens its
    /// PREVIEW, another button is pressed), and not in the first second of the stage. The arrow keys, letters and
    /// Escape are the Garage's own (GarageView).</summary>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }
        if (source is TextBox || source.FindAncestorOfType<TextBox>() is not null)
        {
            _typedAt = DateTimeOffset.UtcNow;
            return;
        }
        if (_lobby is not { GarageShown: true } lobby || _garage is null)
        {
            return;
        }
        var button = source as Button ?? source.FindAncestorOfType<Button>();
        if (e.Key == Key.Enter)
        {
            // (Enter on a tile of the roster opens its PREVIEW: the tile's own press)
            if (button is null && !e.Handled && lobby.CanReady && DateTimeOffset.UtcNow - lobby.StageOpenedAt > EnterWaits)
            {
                lobby.ReadyCommand.Execute(null);
                e.Handled = true;
            }
        }
        // (the arrow keys on the strip's cards are the Garage's own: GarageView)
    }
}
