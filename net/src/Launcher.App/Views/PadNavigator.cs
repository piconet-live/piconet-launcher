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
/// presses the page's main action (<c>PadNav.Main</c>); left and right change a value; the C-stick scrolls. The
/// focus shows as a soft mark (<see cref="PadMark"/>) from the pad's first press until the mouse moves. The pad is
/// read by <see cref="PadReader"/> only while the window is in front and no game runs (this tells it when the
/// window is in front). With "Navigation sounds" on (Settings, Launcher; off by default) a move between controls
/// sounds the game's cursor, by the pad or by the keyboard's Tab, and the pad's B the game's cancel when it closed
/// or left something; the mouse never sounds (<see cref="LauncherSound"/>).
/// </summary>
public sealed partial class PadNavigator
{
    private readonly Window _window;
    private readonly MainViewModel _main;
    private readonly PadMark _mark;
    private readonly Control? _page;
    private readonly PadIntents _intents = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _tick;
    private Control? _on;
    private Control? _focusedAtLanding;
    private Point? _pointer;
    private bool _landing;   // the pad moves the keyboard's focus: its own sound, not the keyboard's

    /// <param name="page">The page's place in the window (the page's own controls come before the left column's).</param>
    public PadNavigator(Window window, MainViewModel main, PadMark mark, Control? page)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(main);
        _window = window;
        _main = main;
        _mark = mark;
        _page = page;
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick());
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.GotFocusEvent, OnFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, (_, _) => Leave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => Leave(), RoutingStrategies.Tunnel, handledEventsToo: true);
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
        var scope = Scope();
        var on = Current(scope);
        if (!Showing && intent is PadMoved or PadPressed { Button: PadButton.A })
        {
            // the first press shows where the pad is; it does not act
            Land(on ?? First(scope), glide: false);
            return;
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
            case PadPressed { Button: PadButton.L or PadButton.R } shoulder when scope == _window:
                Paged(_main.StepTab(shoulder.Button == PadButton.L ? -1 : 1));
                break;
            case PadPressed { Button: PadButton.Z } when scope == _window:
                Paged(_main.NextSection());
                break;
            case PadScrolled scrolled:
                Scroll(on, scrolled.By);
                break;
            default:
                break;
        }
    }

    private void Move(Visual scope, Control? on, PadMoved moved)
    {
        if (on is null)
        {
            Land(First(scope), glide: false);
            return;
        }
        if (PadActions.Step(on, moved.Move, moved.Repeat))
        {
            return;
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
            }
            return;
        }
        var targets = TargetsIn(scope);
        var boxes = targets.Select(t => BoxOf(t, _window)).ToList();
        if (FocusMap.Pick(BoxOf(on, _window), moved.Move, boxes) is { } next)
        {
            Hear(LauncherSound.Cursor);
            Land(targets[next], glide: true);
        }
    }

    /// <summary>B: what is open over the page closes (the question before leaving a lobby: stay; the notices'
    /// list; a replay's file actions or results), else the page goes one step up.</summary>
    private void Back(Control? on)
    {
        if (_main.Control.Asking)
        {
            _main.Control.StayCommand.Execute(null);
        }
        else if (_main.Notices.ListOpen)
        {
            _main.Notices.ToggleListCommand.Execute(null);
        }
        else if (on?.DataContext is ReplayItem { Acting: false } offered)
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
            if (_main.Back())
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
        on?.FindAncestorOfType<GarageView>() ?? (_page as Visual)?.GetVisualDescendants().OfType<GarageView>().FirstOrDefault(g => g.IsEffectivelyVisible);

    /// <summary>A navigation sound, when the settings have them on (<see cref="LauncherSounds.Heard"/>).</summary>
    private void Hear(LauncherSound sound) => Attention.Sound(sound, _main.SoundSettings);

    /// <summary>The keyboard moved the focus (Tab, or the arrows where a control moves it): the cursor's sound. A
    /// click's focus (the pointer) never sounds, nor does the pad's own landing (its move sounded already).</summary>
    private void OnFocused(object? sender, FocusChangedEventArgs e)
    {
        if (!_landing && e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional)
        {
            Hear(LauncherSound.Cursor);
        }
    }

    /// <summary>The page changed under the pad: once it is laid out, the pad is on its first control.</summary>
    private void Paged(bool changed)
    {
        if (changed)
        {
            Dispatcher.UIThread.Post(() => Land(First(Scope()), glide: true), DispatcherPriority.Loaded);
        }
    }

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
        _landing = true;
        try
        {
            focus.Focus(NavigationMethod.Directional);
        }
        finally
        {
            _landing = false;
        }
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

    /// <summary>The C-stick: the list or page round the pad's control scrolls.</summary>
    private void Scroll(Control? on, double by)
    {
        var from = (Visual?)on ?? _page;
        for (var at = from; at is not null; at = at.GetVisualParent())
        {
            if (at is ScrollViewer scroll && scroll.Extent.Height > scroll.Viewport.Height + 1)
            {
                scroll.Offset = scroll.Offset.WithY(Math.Clamp(scroll.Offset.Y + by, 0, scroll.Extent.Height - scroll.Viewport.Height));
                return;
            }
        }
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

    /// <summary>The mouse moved (more than a jitter) or was used: the pad's mark goes.</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(_window);
        if (_pointer is { } before && Math.Abs(at.X - before.X) + Math.Abs(at.Y - before.Y) > 3)
        {
            Leave();
        }
        _pointer = at;
    }

    private void Leave() => _mark.Hide();
}
