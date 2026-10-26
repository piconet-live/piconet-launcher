using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.Core.Pads;

namespace Launcher.App.Views;

// Which controls the pad can reach, and where they are on the screen.
public sealed partial class PadNavigator
{
    /// <summary>The controls the pad can stop on under <paramref name="root"/>, in the tree's order: shown,
    /// enabled, of a size, and not inside another (a stepper's own buttons are the stepper's). A text box and what
    /// is hidden are not searched.</summary>
    public static List<Control> TargetsIn(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var found = new List<Control>();
        Collect(root, found);
        return found;
    }

    private static void Collect(Visual visual, List<Control> found)
    {
        foreach (var child in visual.GetVisualChildren())
        {
            if (child is Control control)
            {
                if (PadNav.Closed(control) || (!control.IsEffectivelyEnabled && PadNav.IsTarget(control)))
                {
                    continue;
                }
                if (PadNav.IsTarget(control))
                {
                    if (control.IsEffectivelyVisible && control.Bounds is { Width: > 1, Height: > 1 })
                    {
                        found.Add(control);
                    }
                    continue;
                }
            }
            Collect(child, found);
        }
    }

    /// <summary>Where the pad searches now: a panel over the page that holds the pad while it shows (the question
    /// before leaving a lobby, the notices' list: <c>PadNav.Scope</c>), else the whole window.</summary>
    private Visual Scope()
    {
        Visual? scope = null;
        foreach (var visual in _window.GetVisualDescendants())
        {
            if (visual is Control control && PadNav.GetScope(control) && control.IsEffectivelyVisible)
            {
                scope = control;   // the last shown is the one on top
            }
        }
        return scope ?? _window;
    }

    /// <summary>A control's place in the window, in the window's units (the interface scale applied).</summary>
    public static Box BoxOf(Control control, Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.TransformToVisual(relativeTo) is not { } transform)
        {
            return default;
        }
        var rect = new Rect(control.Bounds.Size).TransformToAABB(transform);
        return new Box(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>The control the pad is on: the one with the keyboard's focus, when the pad can stop on it and it is
    /// still shown in the place searched.</summary>
    private Control? Current(Visual scope)
    {
        var focused = Focused();
        if (_on is { } on && focused == _focusedAtLanding && Usable(on, scope))
        {
            return on;   // the keyboard's focus is where the pad left it (a row of choices keeps it on its item)
        }
        for (var at = focused; at is not null; at = at.GetVisualParent() as Control)
        {
            if (PadNav.IsTarget(at))
            {
                return Usable(at, scope) ? at : null;
            }
        }
        return null;
    }

    private Control? Focused() => TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;

    private static bool Usable(Control control, Visual scope) =>
        control.IsEffectivelyVisible && control.IsEffectivelyEnabled && scope.IsVisualAncestorOf(control);

    /// <summary>The first control of the page showing (its top left), else the first in the place searched.</summary>
    private Control? First(Visual scope)
    {
        var page = scope == _window && _page is { } host ? TargetsIn(host) : [];
        var targets = page.Count > 0 ? page : TargetsIn(scope);
        return FocusMap.First([.. targets.Select(t => BoxOf(t, _window))]) is { } i ? targets[i] : null;
    }

    /// <summary>The page's main action for Start: a control marked <c>PadNav.Main</c>, the page's before the left
    /// column's (RACE); of several, the one in the focus's row (a replay's Play), else the first.</summary>
    private Control? MainAction(Visual scope, Control? focus)
    {
        var all = TargetsIn(scope).Where(PadNav.GetMain).ToList();
        if (all.Count == 0)
        {
            return null;
        }
        var page = _page is { } host ? all.Where(host.IsVisualAncestorOf).ToList() : [];
        var choices = page.Count > 0 ? page : all;
        if (focus is not null)
        {
            var at = BoxOf(focus, _window);
            if (choices.FirstOrDefault(c => BoxOf(c, _window) is var b && b.Y <= at.CenterY && b.Bottom >= at.CenterY) is { } inRow)
            {
                return inRow;
            }
        }
        return FocusMap.First([.. choices.Select(t => BoxOf(t, _window))]) is { } i ? choices[i] : null;
    }
}
