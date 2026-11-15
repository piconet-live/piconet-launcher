using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.Core.Pads;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's shell (Settings, Launcher, "Experimental UI"): the window's content in place of
/// the classic interface's, on the same view model (<see cref="MainViewModel"/>, its <see cref="ShellViewModel"/>).
/// Here is what is the view's own: the keys that do what the controller's buttons do, which of the two the hints
/// show, the background's wave and the page's entrance at a change, and where the focus is while a question shows.</summary>
public sealed partial class NextShell : UserControl
{
    /// <summary>How far a new page rises in.</summary>
    private const double PageRise = 12;

    private readonly HoloFlag? _flag;
    private readonly Control? _page;
    private MainViewModel? _main;
    private string _area = "";
    private Control? _focusBefore;
    private Point? _pointer;

    public NextShell()
    {
        AvaloniaXamlLoader.Load(this);
        _flag = this.FindControl<HoloFlag>("Flag");
        _page = this.FindControl<ContentControl>("NextPageHost");
        DataContextChanged += (_, _) => Hear();
        foreach (var name in new[] { "SheetLayer", "ConfirmQuestion", "GameQuestion", "RaysAsk", "KeyboardLayer" })
        {
            if (this.FindControl<Panel>(name) is { } question)
            {
                question.PropertyChanged += OnQuestionChanged;
            }
        }
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => KeysInUse(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnSideButton, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>The root's class "moving" while the interface moves (not under Reduced motion): what runs by itself
    /// in the theme (a search's marks, a wait's bar) runs only under it.</summary>
    private void OnMotionChanged() => Classes.Set("moving", !Controls.Motion.Reduced);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Controls.Motion.ReducedChanged += OnMotionChanged;
        OnMotionChanged();
        PadNav.Typing = TypeInto;   // (the pad stops on a text box, and A opens the keyboard on the screen for it)
        PrepareLater(0);
    }

    /// <summary>The large pages, made and laid out ahead while the window is idle after its start, one at a time (the
    /// Garage's grid of machines, Practice's courses, Settings' rows): the first change to each is at once as well
    /// (<see cref="PageDeck.Prepare"/>). Not before the launcher has opened (the first start), and not over a page
    /// change at work.</summary>
    private void PrepareLater(int next)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_main is not { } main || VisualRoot is null || this.FindControl<PageDeck>("Pages") is not { } deck)
            {
                return;
            }
            ViewModels.ViewModelBase[] large = [main.Garage, main.Practice, main.Settings];
            if (!main.Navigation)
            {
                PrepareLater(next);   // (the first start: later)
                return;
            }
            if (next < large.Length)
            {
                deck.Prepare(large[next]);
                PrepareLater(next + 1);
            }
        }, TimeSpan.FromMilliseconds(next == 0 ? 1500 : 400), DispatcherPriority.Background);
    }

    /// <summary>A text box the pad pressed: the keyboard on the screen for it, named as the box is, a password's as
    /// dots; Done puts the text in the box (its binding takes it on), and the focus back on it.</summary>
    private bool TypeInto(TextBox box)
    {
        if (_main is null || !box.IsEffectivelyEnabled)
        {
            return false;
        }
        var title = AutomationProperties.GetName(box) is { Length: > 0 } name ? name : box.PlaceholderText ?? "Text";
        _main.Shell.OpenKeyboard(title, box.Text ?? "", box.MaxLength, box.PasswordChar != '\0', text =>
        {
            box.Text = text;
            box.Focus(NavigationMethod.Directional);
        });
        return true;
    }

    /// <summary>The control the focus is on, by its name, for Developer mode's line about input.</summary>
    private void OnFocused(object? sender, FocusChangedEventArgs e)
    {
        if (_main is { DeveloperMode: true } main && e.Source is Control control)
        {
            main.Shell.FocusName = AutomationProperties.GetName(control) is { Length: > 0 } name ? name : control.Name ?? control.GetType().Name;
        }
    }

    /// <summary>The recall's last three seconds, each a tick: a countdown the player hears in the game, where the
    /// launcher's band does not show.</summary>
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.RecallSeconds) && _main is { } main)
        {
            RecallSounds.Second(main, main.Shell.RecallSeconds);
        }
    }

    /// <summary>A press beside a panel closes it.</summary>
    private void OnSheetDimPressed(object? sender, PointerPressedEventArgs e) => _main?.Shell.CloseSheetCommand.Execute(null);

    /// <summary>The page's place in the shell (the pad's first stop on a new page is in it).</summary>
    public Control? PageHost => _page;

    private void Hear()
    {
        if (_main is not null)
        {
            _main.PropertyChanged -= OnMainChanged;
            _main.Shell.RecallBegan -= OnRecallBegan;
            _main.Shell.PropertyChanged -= OnShellChanged;
        }
        _main = DataContext as MainViewModel;
        if (_main is not null)
        {
            _main.PropertyChanged += OnMainChanged;
            _main.Shell.RecallBegan += OnRecallBegan;
            _main.Shell.PropertyChanged += OnShellChanged;
            _area = _main.Shell.Area;
        }
    }

    /// <summary>The lobby calls the player back from their practice: the window asks to be noticed (the player is in
    /// the game's window).</summary>
    private void OnRecallBegan()
    {
        if (_main is { } main)
        {
            RecallSounds.Begun(main);   // (heard in the game too)
        }
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            Attention.Flash(window);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Controls.Motion.ReducedChanged -= OnMotionChanged;
        if (PadNav.Typing == TypeInto)
        {
            PadNav.Typing = null;   // (the classic interface has no keyboard on the screen)
        }
        if (_main is not null)
        {
            _main.PropertyChanged -= OnMainChanged;
            _main.Shell.RecallBegan -= OnRecallBegan;
            _main.Shell.PropertyChanged -= OnShellChanged;
            _main = null;
        }
    }

    /// <summary>Another page: it rises in; in another area, the background's wave runs the way the areas were
    /// stepped. Input goes on all the while: the page is laid out where it ends.</summary>
    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Current) || _main is not { } main)
        {
            return;
        }
        var area = main.Shell.Area;
        if (area != _area)
        {
            var order = ShellViewModel.AreaOrder.ToList();
            _flag?.Wave(order.IndexOf(area) >= order.IndexOf(_area) ? 1 : -1);
            _area = area;
        }
        if (_page is { } page)
        {
            Controls.Motion.EnterPage(page, 0, PageRise);
        }
    }

    /// <summary>A key that does what a button of the controller does, while no text is being typed: Q and E step
    /// through the areas (L and R), Escape and Backspace go back (B), Y the page's second action, X the chat, S
    /// presses its main action (Start), Page Up and Page Down scroll the list round the focus. The window gives the
    /// keys nothing else took. True: the key did something.</summary>
    public bool Shortcut(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_main is not { } main || e.KeyModifiers != KeyModifiers.None || Typing(e.Source))
        {
            return false;
        }
        KeysInUse();
        switch (e.Key)
        {
            case Key.Escape or Key.Back:
                // as B: what is open closes, else the focus goes back to the page's own list, else out of the page
                return (!main.Shell.OverPage && Pad?.FocusHome() == true) || main.Shell.Back();
            case Key.PageUp or Key.PageDown:
                return Pad?.ScrollPage(e.Key == Key.PageUp ? -1 : 1) == true;
            case Key.Q:
                return main.Shell.StepArea(-1);
            case Key.E:
                return main.Shell.StepArea(1);
            case Key.Y:
                return main.Shell.Context();
            case Key.X:
                return main.Shell.ToggleChat();
            case Key.S when !main.Shell.OverPage:
                return (TopLevel.GetTopLevel(this) as MainWindow)?.Pad?.PressMain() == true;
            default:
                return false;
        }
    }

    /// <summary>The navigator of the window's pad (the keyboard's arrows go its way).</summary>
    private PadNavigator? Pad => (TopLevel.GetTopLevel(this) as MainWindow)?.Pad;

    /// <summary>Before anything else takes them, while no text is typed: the arrow keys move the focus as the pad's
    /// D-pad does, by where the controls are on the screen (a row's value steps left and right; a key that moved
    /// nothing goes on, and a list scrolls); Ctrl+Tab and Ctrl+Shift+Tab step through the areas.</summary>
    private void OnNavigationKey(object? sender, KeyEventArgs e)
    {
        if (_main is not { } main || Typing(e.Source))
        {
            return;
        }
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            KeysInUse();
            e.Handled = main.Shell.StepArea(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            return;
        }
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.Control && Pad is { } tabs)
        {
            KeysInUse();
            e.Handled = tabs.StepTab(e.Key == Key.Right ? 1 : -1, heard: false);   // (the page's sub-tabs, as R1)
            return;
        }
        PadMove? move = e.Key switch
        {
            Key.Up => PadMove.Up,
            Key.Down => PadMove.Down,
            Key.Left => PadMove.Left,
            Key.Right => PadMove.Right,
            _ => null,
        };
        if (move is { } way && e.KeyModifiers == KeyModifiers.None && Pad is { } pad)
        {
            KeysInUse();
            e.Handled = pad.Arrow(way);
        }
    }

    /// <summary>The mouse's side buttons: back (as the top bar's Back does: what is open closes first, else out of the
    /// page, never only to the page's list as B goes) and forward (the page Back left, again).</summary>
    private void OnSideButton(object? sender, PointerPressedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (_main is not { } main || kind is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed))
        {
            return;
        }
        e.Handled = kind == PointerUpdateKind.XButton1Pressed ? main.Shell.Back() : main.Shell.Forward();
    }

    /// <summary>The wheel over a list that scrolls only to the side (and none round it up and down first): it scrolls
    /// that list sideways.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || e.Delta.X != 0)
        {
            return;
        }
        for (var at = e.Source as Visual; at is not null && at != this; at = at.GetVisualParent())
        {
            if (at is not ScrollViewer scroll)
            {
                continue;
            }
            if (scroll.Extent.Height > scroll.Viewport.Height + 1)
            {
                return;   // it scrolls up and down: its own way
            }
            if (scroll.Extent.Width > scroll.Viewport.Width + 1)
            {
                scroll.Offset = scroll.Offset.WithX(Math.Clamp(scroll.Offset.X - (e.Delta.Y * WheelStep), 0, scroll.Extent.Width - scroll.Viewport.Width));
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>How far a notch of the wheel scrolls a list sideways.</summary>
    private const double WheelStep = 120;

    /// <summary>Whether the key was typed into something that takes text.</summary>
    private static bool Typing(object? source) =>
        source is TextBox || (source as Visual)?.FindAncestorOfType<TextBox>() is not null;

    /// <summary>The mouse or the keyboard is in use: the hints show the keys.</summary>
    private void KeysInUse()
    {
        if (_main is { Shell.PadInUse: true } main)
        {
            main.Shell.PadInUse = false;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_pointer is { } before && Math.Abs(at.X - before.X) + Math.Abs(at.Y - before.Y) > 3)
        {
            KeysInUse();
        }
        _pointer = at;
    }

    /// <summary>A question shows: the focus is on its first answer, the one that changes nothing; gone, the focus is
    /// back where it was.</summary>
    private void OnQuestionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || sender is not Panel question)
        {
            return;
        }
        if (question.IsVisible)
        {
            if (question.Name == "SheetLayer" && this.FindControl<Control>("SheetPlate") is { } plate)
            {
                Controls.Motion.Enter(plate, 48, 0);   // a panel comes in from the edge
            }
            _focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            Dispatcher.UIThread.Post(() =>
            {
                if (question.IsEffectivelyVisible)
                {
                    question.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
        }
        else if (_focusBefore is { IsEffectivelyVisible: true } before && this.IsVisualAncestorOf(before))
        {
            before.Focus(NavigationMethod.Directional);
            _focusBefore = null;
        }
    }

    /// <summary>The window this shows in (the window's own buttons act on it).</summary>
    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}
