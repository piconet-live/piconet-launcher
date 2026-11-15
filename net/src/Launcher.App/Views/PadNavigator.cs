using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.App.ViewModels;
using Launcher.Core;
using Launcher.Core.Pads;

namespace Launcher.App.Views;

/// <summary>
/// The pad works the window (everything but typing, with no keyboard on the screen). The
/// D-pad and the main stick move the focus by position on the screen (<see cref="FocusMap"/>); A presses; B closes
/// what is open over the page (the Garage's Sort &amp; filter panel; its PREVIEW: back to SELECT), else goes one step up; L and R go through the
/// tabs (on the Garage roster's tiles: its shelves), Z through a tab's pages; Start
/// presses the page's main action (<c>PadNav.Main</c>); left and right change a value; the C-stick scrolls (up and down,
/// and to the side). The focus shows as a soft mark (<see cref="PadMark"/>) from the pad's first press until the mouse
/// moves; the redesigned interface draws the focus itself, and there a press acts at once on what has the focus. Its
/// arrow keys move the focus as the D-pad does (<see cref="Arrow"/>), and its keys scroll a page (<see cref="ScrollPage"/>). The pad is
/// read from the launcher's helper (<see cref="PadReader"/>, which holds the devices while no game runs) and acts only
/// while the window is in front (this tells it when the window is in front), and works the pages only with "Controller navigation" on (Settings, Launcher; off by default). Then a
/// move of the pad between controls sounds the game's cursor, and its B the game's cancel when it closed or left
/// something; the keyboard never sounds, and the mouse only in the redesigned interface, where moving onto a control
/// gives it the focus as the pad's move does (the cursor's sound, <see cref="OnPointerMoved"/>; <see cref="LauncherSound"/>).
/// </summary>
public sealed partial class PadNavigator
{
    private readonly Window _window;
    private readonly MainViewModel _main;
    private readonly PadMark _mark;
    private readonly PadIntents _intents = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _tick;
    private Control? _on;
    private Control? _focusedAtLanding;
    private Point? _pointer;
    private bool _keys;

    /// <param name="page">The page's place in the window (the page's own controls come before the left column's).</param>
    public PadNavigator(Window window, MainViewModel main, PadMark mark, Control? page)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(main);
        _window = window;
        _main = main;
        _mark = mark;
        Page = page;
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick());
        NextUi.NextFocus.Install();   // (a list scrolled to the focus keeps room for its ring)
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, (_, _) => Leave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => Leave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(Button.ClickEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        window.LayoutUpdated += (_, _) => Follow();
        window.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => Follow(), RoutingStrategies.Bubble, handledEventsToo: true);
        window.Activated += (_, _) => main.WindowFocus(true);
        window.Deactivated += (_, _) =>
        {
            main.WindowFocus(false);
            _intents.Reset();
        };
        main.PadReader.Pad += pad => Dispatcher.UIThread.Post(() => Read(pad));
        main.PadReader.Released += () => Dispatcher.UIThread.Post(_intents.Reset);
    }

    /// <summary>The page's place in the interface that shows (the window sets it when the interface changes: the
    /// classic one's, or the redesigned one's).</summary>
    public Control? Page { get; set; }

    /// <summary>The page's main action pressed, as Start presses it (the redesigned interface's S key). False: the
    /// page has none.</summary>
    public bool PressMain()
    {
        var scope = Scope();
        if (MainAction(scope, Current(scope)) is not { } main)
        {
            return false;
        }
        PadActions.Press(main);
        return true;
    }

    /// <summary>The pad's mark shows (the pad was used since the mouse last moved).</summary>
    public bool Showing => _mark.Showing;

    /// <summary>The control the pad is on, while the mark shows.</summary>
    public Control? On => Showing ? _on : null;

    /// <summary>A reading of the pad (on the window's thread).</summary>
    public void Read(HelperPad pad)
    {
        ArgumentNullException.ThrowIfNull(pad);
        var now = _clock.Elapsed;
        var intents = _intents.Read(pad, now);
        if (_main.Current is ControllerViewModel { Capturing: true })
        {
            return;   // "press to set" takes this press
        }
        if (!_main.PadNavigation)
        {
            return;   // (Settings: the controller does not work the pages)
        }
        foreach (var intent in intents)
        {
            Act(intent);
        }
        if (_intents.Holding && !_tick.IsEnabled)
        {
            _tick.Start();
        }
    }

    /// <summary>One intent, as the pad's reading or its repeat gave it (tests give them directly).</summary>
    public void Act(PadIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (_main.ExperimentalUi && intent is PadPressed { Button: PadButton.A or PadButton.B } face)
        {
            // the redesigned interface's "Confirm button": the right face button confirms (the classic one: as ever)
            var (family, confirmRight) = _main.Shell.PadLook();
            intent = new PadPressed(PadIcons.Swap(face.Button, confirmRight, family));
        }
        var scope = Scope();
        var on = Current(scope);
        if (_main.ExperimentalUi)
        {
            _main.Shell.PadInUse = true;   // the hints show the pad's buttons
        }
        if (!Showing && intent is PadMoved or PadPressed { Button: PadButton.A })
        {
            // the first press shows where the pad is; it does not act. The redesigned interface draws the focus
            // itself: there it acts at once on what has the focus, and only lands when nothing has it.
            if (!_main.ExperimentalUi || on is null)
            {
                Land(on ?? (scope == _window ? Landing() : null) ?? First(scope), glide: false);
                return;
            }
            Land(on, glide: false);
        }
        switch (intent)
        {
            case PadMoved moved:
                Move(scope, on, moved);
                break;
            case PadPressed { Button: PadButton.A }:
                if (on is not null)
                {
                    PadActions.Press(on);
                    AfterPage();
                }
                break;
            case PadPressed { Button: PadButton.B }:
                Back(on);
                break;
            case PadPressed { Button: PadButton.Start }:
                if (MainAction(scope, on) is { } main)
                {
                    Land(main, glide: true);
                    PadActions.Press(main);
                    AfterPage();
                }
                break;
            case PadPressed { Button: PadButton.L or PadButton.R } shoulder when on is not null && GarageView.IsCard(on):
                // on the Garage's roster the shoulders go through its shelves, the pad on the
                // tile of the machine raced, else the shelf's first
                if (on.FindAncestorOfType<GarageView>()!.TurnShelf(shoulder.Button == PadButton.L ? -1 : 1) is { } garage)
                {
                    Dispatcher.UIThread.Post(() => Land(garage.CurrentCard() ?? First(Scope()), glide: true), DispatcherPriority.Loaded);
                }
                break;
            case PadPressed { Button: PadButton.L or PadButton.R or PadButton.Z or PadButton.X or PadButton.Y } paging:
                Shoulder(scope, paging.Button);
                break;
            case PadScrolled scrolled:
                Scroll(on, scrolled.By, vertical: true);
                Scroll(on, scrolled.Across, vertical: false);
                break;
            default:
                break;
        }
    }

    /// <summary>The shoulders and X and Y. In the redesigned interface X and Y are the chat and the main menu from
    /// anywhere (<see cref="ChatOrHome"/>); Z (R1 in the standard layout) steps through the sub-tabs where the pad is, a
    /// sheet's too, and where there are none it is the page's second action (the lobby's panel, the room's rules, the
    /// race's racers: <c>ShellViewModel.Context</c>, which closes the panel it opened when pressed again); the
    /// others go to another place with nothing open over the page.</summary>
    private void Shoulder(Visual scope, PadButton button)
    {
        if (_main.ExperimentalUi && button is PadButton.X or PadButton.Y)
        {
            ChatOrHome(button);
        }
        else if (button == PadButton.Z && _main.ExperimentalUi)
        {
            if (!StepTab(1) && _main.Shell.Context())
            {
                AfterPage();
            }
        }
        else if (scope == _window)
        {
            Places(button);
        }
    }

    /// <summary>The redesigned interface's X and Y (the owner, 2026-11-15): X opens and closes the chat, Y goes to the
    /// main menu (asked first where something would be lost or missed: <c>HubViewModel.GoHome</c>), the pad then on the
    /// hub's first entry. Not while a question or the keyboard on the screen is open, nor before the first start is
    /// through.</summary>
    private void ChatOrHome(PadButton button)
    {
        var shell = _main.Shell;
        if (button == PadButton.X)
        {
            shell.ToggleChat();
            return;
        }
        if (!_main.Navigation || shell.AskingConfirm || shell.KeyboardOpen)
        {
            return;
        }
        var before = _main.Current;
        if (before is ViewModels.NextUi.HubViewModel && !shell.OverPage)
        {
            return;   // (the main menu already)
        }
        shell.Hub.GoHomeCommand.Execute(null);
        if (!ReferenceEquals(before, _main.Current))
        {
            Hear(LauncherSound.Back);
            Paged(true);
        }
    }

    /// <summary>The buttons that go to another place, with nothing open over the page. The classic interface: L and
    /// R through the tabs, Z through a tab's pages. The redesigned one: L and R through the areas (its X, Y and Z are
    /// <see cref="Shoulder"/>'s).</summary>
    private void Places(PadButton button)
    {
        var by = button == PadButton.L ? -1 : 1;
        if (!_main.ExperimentalUi)
        {
            if (button is PadButton.L or PadButton.R)
            {
                Paged(_main.StepTab(by));
            }
            else if (button == PadButton.Z)
            {
                Paged(_main.NextSection());
            }
            return;
        }
        switch (button)
        {
            case PadButton.L or PadButton.R:
                Paged(_main.Shell.StepArea(by));
                break;
            default:
                break;
        }
    }

    /// <summary>An arrow key of the redesigned interface: the focus moves as the D-pad moves it, a row's value steps
    /// left and right; with nothing focused, the page's first control takes it. Quiet (the keyboard never sounds).
    /// False: nothing moved (the key goes on: a list scrolls).</summary>
    public bool Arrow(PadMove move)
    {
        var scope = Scope();
        _keys = true;
        try
        {
            return Move(scope, Current(scope), new PadMoved(move, Repeat: false));
        }
        finally
        {
            _keys = false;
        }
    }

    /// <summary>B, Escape and Backspace in the redesigned interface, before they leave the page: the focus back on the
    /// page's own list (<c>PadNav.Home</c>: the Garage's machines, the lobbies, the servers, the replays, the courses)
    /// when it is elsewhere on the page (a card's button, the actions beside the list), on the item the page says (the
    /// machine on the stage, the course picked) or the list's first. False: the focus is in the list already, on no
    /// control, over a panel, or the page has no list; the page is left then.</summary>
    public bool FocusHome(bool heard = false)
    {
        var scope = Scope();
        if (scope != _window || Page is not Visual page || Current(scope) is not { } on || !page.IsVisualAncestorOf(on))
        {
            return false;
        }
        var home = page.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => PadNav.GetHome(c) && c.IsEffectivelyVisible);
        if (home is null || home.IsVisualAncestorOf(on))
        {
            return false;
        }
        var targets = TargetsIn(home);
        // (the page's own word, not the cursor it kept: B goes back to the list's item, the machine on the stage)
        var said = page.GetVisualDescendants().OfType<IPadLanding>()
            .FirstOrDefault(l => l is not Controls.NextUi.PageDeck && l is Visual { IsEffectivelyVisible: true })?.Landing();
        var to = said is { } landing && home.IsVisualAncestorOf(landing) ? landing
            : targets.FirstOrDefault(t => t.Classes.Contains("picked") || t.Classes.Contains("on"))
              ?? (FocusMap.First([.. targets.Select(t => BoxOf(t, _window))]) is { } first ? targets[first] : null);
        if (to is null)
        {
            return false;
        }
        if (heard)
        {
            Hear(LauncherSound.Back);   // (the pad's B; the keyboard never sounds)
        }
        Land(to, glide: true);
        return true;
    }

    /// <summary>The next (1) or the previous (-1) of the sub-tabs where the pad is (<c>PadNav.Tabs</c>: Practice's
    /// cups, the Garage's shelves, the Replays' filter, Stats' best times, the colours' targets), round at the ends,
    /// pressed as A presses it (the focus then on to its list). R1 on the pad (the GameCube's Z: the standard layout
    /// leaves L1 free, so R1 goes round), Ctrl+arrows on the keyboard. False: there are none here.</summary>
    public bool StepTab(int by, bool heard = true)
    {
        var scope = Scope();
        var within = scope == _window ? Page : scope;
        if (within?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => PadNav.GetTabs(c) && c.IsEffectivelyVisible) is not { } tabs)
        {
            return false;
        }
        var chips = TargetsIn(tabs).Where(c => c.IsEffectivelyEnabled).OrderBy(c => BoxOf(c, _window).Y).ThenBy(c => BoxOf(c, _window).X).ToList();
        if (chips.Count < 2)
        {
            return false;
        }
        var at = chips.FindIndex(c => c.Classes.Contains("go") || c.Classes.Contains("on"));
        var to = chips[at < 0 ? (by > 0 ? 0 : chips.Count - 1) : (at + by + chips.Count) % chips.Count];
        if (heard)
        {
            Hear(LauncherSound.Cursor);
        }
        PadActions.Press(to);
        return true;
    }

    /// <summary>Page Up (-1) and Page Down (1): the list or page round the focus scrolls by most of its height. False:
    /// nothing scrolls there.</summary>
    public bool ScrollPage(int by)
    {
        var from = (Visual?)Current(Scope()) ?? Page;
        for (var at = from; at is not null; at = at.GetVisualParent())
        {
            if (at is ScrollViewer scroll && scroll.Extent.Height > scroll.Viewport.Height + 1)
            {
                return Scroll(scroll, by * scroll.Viewport.Height * 0.85, vertical: true);
            }
        }
        return false;
    }

    private bool Move(Visual scope, Control? on, PadMoved moved)
    {
        if (on is null)
        {
            Land((scope == _window ? Landing() : null) ?? First(scope), glide: false);   // (where the page says, its cursor)
            return _on is not null;
        }
        if (PadActions.Step(on, moved.Move, moved.Repeat, out var changed))
        {
            if (changed)
            {
                Hear(LauncherSound.Cursor);   // (a value changed sounds as a move does)
            }
            return true;
        }
        if (_main.ExperimentalUi && moved.Move is PadMove.Left or PadMove.Right && PadNav.IsValue(on) && on.IsEffectivelyEnabled)
        {
            return true;   // (the redesigned interface: left and right on a value step it, and at its end stay on it)
        }
        if (moved.Move is PadMove.Left or PadMove.Right && GarageView.IsCard(on)
            && !(moved.Move == PadMove.Left && on.FindAncestorOfType<GarageView>()!.AtRowStart(on)))
        {
            // across the Garage's roster: the next tile in the shelf's order, a row's end wrapping to the next row
            // (nothing is picked: A opens a tile); left from a row's first tile leaves the roster, as anywhere
            if (on.FindAncestorOfType<GarageView>()!.StepRoster(on, moved.Move == PadMove.Right ? 1 : -1) is { } tile)
            {
                Hear(LauncherSound.Cursor);
                Land(tile, glide: true);
                return true;
            }
            return false;
        }
        var targets = TargetsIn(scope);
        var boxes = targets.Select(t => BoxOf(t, _window)).ToList();
        if (FocusMap.Pick(BoxOf(on, _window), moved.Move, boxes) is { } next)
        {
            Hear(LauncherSound.Cursor);
            Land(targets[next], glide: true);
            return true;
        }
        return false;
    }

    /// <summary>B: what is open over the page closes (the question before ray tracing: cancel; the question before
    /// leaving a lobby: stay; the notices' list; a replay's file actions or results), else the page goes one step up.</summary>
    private void Back(Control? on)
    {
        if (_main.ExperimentalUi)
        {
            // the redesigned interface: what is open over the page is the shell's to close
            if (!_main.Shell.CloseOverPage())
            {
                BackInPage(on);
                return;
            }
        }
        else if (_main.Settings.AskingExperimental)
        {
            _main.Settings.KeepClassicCommand.Execute(null);   // the question before the experimental UI: not now
        }
        else if (_main.Graphics.Asking)
        {
            _main.Graphics.CancelCommand.Execute(null);
        }
        else if (_main.Control.Asking)
        {
            _main.Control.StayCommand.Execute(null);
        }
        else if (_main.Notices.ListOpen)
        {
            _main.Notices.ToggleListCommand.Execute(null);
        }
        else
        {
            BackInPage(on);
            return;
        }
        Hear(LauncherSound.Back);
        AfterPage();
    }

    /// <summary>B with nothing open over the page: what is open in the page closes (a replay's file actions or
    /// results; the Garage's Sort &amp; filter panel; its PREVIEW: back to SELECT), else the page goes one step up.</summary>
    private void BackInPage(Control? on)
    {
        if (on?.DataContext is ReplayItem { Acting: false } offered)
        {
            offered.Offer = "actions";
        }
        else if (on?.DataContext is ReplayItem { ShowingStats: true } stats)
        {
            stats.ShowingStats = false;
        }
        else if (GarageIn(on) is { DataContext: GarageViewModel { SortFilterOpen: true } sorting } sortView)
        {
            // the Garage's Sort & filter panel: closed, the pad back on its button
            sorting.CloseSortFilterCommand.Execute(null);
            Hear(LauncherSound.Back);
            Dispatcher.UIThread.Post(() => Land(sortView.FindControl<Button>("SortFilter") ?? First(Scope()), glide: true), DispatcherPriority.Loaded);
            return;
        }
        else if (GarageIn(on) is { DataContext: GarageViewModel { Previewing: true } garage } view)
        {
            // the Garage's PREVIEW: back to SELECT, the pad on the tile of the machine raced
            garage.BackCommand.Execute(null);
            Hear(LauncherSound.Back);
            Dispatcher.UIThread.Post(() => Land(view.CurrentCard() ?? First(Scope()), glide: true), DispatcherPriority.Loaded);
            return;
        }
        else
        {
            if (_main.ExperimentalUi && FocusHome(heard: true))
            {
                return;   // (the page's own list first; B again leaves the page)
            }
            if (_main.ExperimentalUi ? _main.Shell.Back() : _main.Back())
            {
                Hear(LauncherSound.Back);
                Paged(true);
            }
            return;
        }
        Hear(LauncherSound.Back);
        AfterPage();
    }

    /// <summary>The Garage the pad is in: round its control, else the page's (the lobby's, or the Garage page).</summary>
    private GarageView? GarageIn(Control? on) =>
        on?.FindAncestorOfType<GarageView>() ?? (Page as Visual)?.GetVisualDescendants().OfType<GarageView>().FirstOrDefault(g => g.IsEffectivelyVisible);

    /// <summary>A navigation sound, when the settings have them on (<see cref="LauncherSounds.Heard"/>).</summary>
    private void Hear(LauncherSound sound)
    {
        if (!_keys)
        {
            Attention.Sound(sound, _main.SoundSettings);
        }
    }

    /// <summary>The page changed under the pad: once it is laid out, the pad is on its first control.</summary>
    private void Paged(bool changed)
    {
        if (changed)
        {
            Dispatcher.UIThread.Post(() => Land(Landing() ?? First(Scope()), glide: true), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Where a page says the pad lands on it (<see cref="IPadLanding"/>); null: the page says nothing.</summary>
    private Control? Landing() =>
        (Page as Visual)?.GetVisualDescendants().OfType<IPadLanding>().FirstOrDefault(l => l is Visual { IsEffectivelyVisible: true })?.Landing() is { } on
        && Usable(on, Scope()) ? on : null;

    /// <summary>A press may have changed what shows (a row's actions, a question, a page): once laid out, the pad
    /// stays on its control, or lands on the first of what shows now.</summary>
    private void AfterPage() => Dispatcher.UIThread.Post(() =>
    {
        var scope = Scope();
        Land(Current(scope) ?? First(scope), glide: true);
    }, DispatcherPriority.Loaded);

    private void Land(Control? control, bool glide)
    {
        if (control is null)
        {
            return;
        }
        _on = control;
        // a row of choices takes the keyboard's focus on its chosen item (the list itself takes none)
        var focus = control is ListBox list && list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) is { } item ? item : control;
        focus.Focus(NavigationMethod.Directional);
        _focusedAtLanding = Focused();
        control.BringIntoView();
        _mark.Show(MarkBox(control), glide, RadiusOf(control));
    }

    /// <summary>The mark follows its control when the page moves under it (a scroll, a layout): at once, or, while it
    /// is gliding there (a list scrolled to the control the pad went to), on to where it is now. A control that has
    /// gone takes the mark with it.</summary>
    private void Follow()
    {
        if (!Showing || _on is null)
        {
            return;
        }
        if (!_on.IsEffectivelyVisible || TopLevel.GetTopLevel(_on) is null)
        {
            _mark.Hide();
            _on = null;
            return;
        }
        _mark.Show(MarkBox(_on), glide: _mark.Moving, RadiusOf(_on));
    }

    /// <summary>A control's corner (its top left's), in the mark's units: the ring follows its shape.</summary>
    private double RadiusOf(Control control)
    {
        var radius = control switch
        {
            Avalonia.Controls.Primitives.TemplatedControl t => t.CornerRadius.TopLeft,
            Border b => b.CornerRadius.TopLeft,
            _ => 0,
        };
        // (a full round's 999 is as round as the control)
        radius = Math.Min(radius, Math.Min(control.Bounds.Width, control.Bounds.Height) / 2);
        return control.TranslatePoint(new Point(radius, 0), _mark) is { } a && control.TranslatePoint(default, _mark) is { } o ? a.X - o.X : radius;
    }

    private Rect MarkBox(Control control) =>
        BoxOf(control, _mark) is var b ? new Rect(b.X, b.Y, b.Width, b.Height) : default;

    /// <summary>The C-stick: the list or page round the pad's control scrolls, up and down or to the side (the first
    /// round it that scrolls that way).</summary>
    private void Scroll(Control? on, double by, bool vertical)
    {
        if (by == 0)
        {
            return;
        }
        var from = (Visual?)on ?? Page;
        for (var at = from; at is not null; at = at.GetVisualParent())
        {
            if (at is ScrollViewer scroll && Scroll(scroll, by, vertical))
            {
                return;
            }
        }
    }

    /// <summary>A scroll viewer scrolled that way, within its ends. False: it does not scroll that way.</summary>
    private static bool Scroll(ScrollViewer scroll, double by, bool vertical)
    {
        if (vertical && scroll.Extent.Height > scroll.Viewport.Height + 1)
        {
            scroll.Offset = scroll.Offset.WithY(Math.Clamp(scroll.Offset.Y + by, 0, scroll.Extent.Height - scroll.Viewport.Height));
            return true;
        }
        if (!vertical && scroll.Extent.Width > scroll.Viewport.Width + 1)
        {
            scroll.Offset = scroll.Offset.WithX(Math.Clamp(scroll.Offset.X + by, 0, scroll.Extent.Width - scroll.Viewport.Width));
            return true;
        }
        return false;
    }

    private void Tick()
    {
        foreach (var intent in _intents.Tick(_clock.Elapsed))
        {
            if (_main.Current is ControllerViewModel { Capturing: true })
            {
                break;
            }
            Act(intent);
        }
        if (!_intents.Holding)
        {
            _tick.Stop();
        }
    }

    /// <summary>The mouse moved (more than a jitter) or was used: the pad's mark goes. In the redesigned interface the
    /// control the mouse moved onto takes the focus, as the pad's move gives it (<see cref="NextUi.NextFocus"/>), with
    /// the same sound: the game's cursor, when the settings have the navigation's sounds on.</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(_window);
        if (_pointer is { } before && Math.Abs(at.X - before.X) + Math.Abs(at.Y - before.Y) > 3)
        {
            Leave();
            if (_main.ExperimentalUi && NextUi.NextFocus.Hover(_window, e))
            {
                Attention.Sound(LauncherSound.Cursor, _main.SoundSettings);
            }
        }
        _pointer = at;
    }

    private void Leave() => _mark.Hide();

    /// <summary>In the redesigned interface a large button pressed, the way into a place (its class "confirms": the
    /// hub's four entries, More's cards, Settings' groups), by the pointer, the keys or the pad alike: the game's
    /// confirm, the sound of "Use this machine" (the owner, 2026-11-15), heard as the menu's sounds are. The top
    /// bar's way to a place pressed (the launcher's name, an area): the focus goes on to the page, as the pad's L and R
    /// take it, and the button keeps no focus (its look stayed lit after a click, as if the pointer were still on it).</summary>
    private void OnPressed(object? sender, RoutedEventArgs e)
    {
        if (!_main.ExperimentalUi || e.Source is not Button button)
        {
            return;
        }
        if (button.Classes.Contains("confirms"))
        {
            Attention.Sound(LauncherSound.Confirm, _main.SoundSettings);
        }
        if (button.Classes.Contains("wordmark") || button.Classes.Contains("area"))
        {
            Paged(true);
        }
    }
}
