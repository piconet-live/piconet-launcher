using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Launcher.App.Views.NextUi;

/// <summary>
/// The focus in the redesigned interface (the owner, 2026-11-15). The pointer and the pad work alike: the control the
/// pointer moves onto takes the focus, as a move of the pad gives it, so that whatever follows the focus follows the
/// pointer too (the Garage's machine on the stage, the hub's line, an option's help, a setting's panel), and the two
/// look the same (<see cref="Hover"/>), and sound the same: the menu's cursor as the focus moves. And a list scrolled to the focus keeps room round it (<see cref="Room"/>), so
/// that the focus's ring and a card's lift are never cut off at the list's edge.
/// </summary>
public static class NextFocus
{
    /// <summary>The room kept round a control a list scrolls to: its ring (4 px out), a card's lift (3 px) and a little
    /// air.</summary>
    public const double Room = 12;

    private static bool _installed;

    /// <summary>Once for the application: every scroll to a control of the redesigned interface takes its room with
    /// it.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }
        _installed = true;
        Control.RequestBringIntoViewEvent.AddClassHandler<Control>(KeepRoom);
    }

    private static void KeepRoom(Control sender, RequestBringIntoViewEventArgs e)
    {
        if (!ReferenceEquals(sender, e.TargetObject) || e.TargetObject is not Visual target
            || !target.GetSelfAndVisualAncestors().OfType<StyledElement>().Any(v => v.Classes.Contains("nextui")))
        {
            return;   // (once, at the target itself; the classic interface as it was)
        }
        e.TargetRect = e.TargetRect.Inflate(Room);
    }

    /// <summary>The pointer moved over <paramref name="root"/>: the button under it takes the focus (not while a button
    /// is held: a drag, the 3D view, a slider; nor away from text being typed). The list is not scrolled to it: the
    /// pointer is on it already. Called when the pointer really moved, not when only the page moved under a still
    /// pointer (a scroll). True: the focus moved to another button (the menu's cursor sounds, as for a move of the pad).</summary>
    public static bool Hover(Visual root, PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(e);
        if (e.Source is not Visual source || e.GetCurrentPoint(root).Properties is { IsLeftButtonPressed: true } or { IsRightButtonPressed: true }
            || TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is TextBox)
        {
            return false;
        }
        var target = source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        if (target is null || target.IsFocused || !target.IsEffectivelyEnabled || !target.Focusable || !root.IsVisualAncestorOf(target))
        {
            return false;
        }
        target.AddHandler(Control.RequestBringIntoViewEvent, StayInView);
        try
        {
            return target.Focus(NavigationMethod.Pointer) && target.IsFocused;
        }
        finally
        {
            target.RemoveHandler(Control.RequestBringIntoViewEvent, StayInView);
        }
    }

    private static void StayInView(object? sender, RequestBringIntoViewEventArgs e) => e.Handled = true;
}
