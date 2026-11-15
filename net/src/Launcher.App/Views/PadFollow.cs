using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.Core.Pads;

namespace Launcher.App.Views;

/// <summary>
/// The next step after a press (<c>PadNav.Next</c>): a control that picks something says where the player goes from
/// it, and the focus goes there once the press has done its work: a course picked, START PRACTICE; a machine shown,
/// Use this machine (in a lobby, READY); a shelf or a filter chosen, its list. A step that cannot be taken now (a
/// button that is off or not shown) leaves the focus where it is. The same for the pad, the keyboard and the mouse,
/// so that the next press of A is the one that goes on.
/// </summary>
public static class PadFollow
{
    /// <summary>A control pressed (its click): the focus to its next step, once the press's work is laid out.</summary>
    public static void Pressed(Control? pressed)
    {
        if (pressed is null || PadNav.GetNext(pressed) is not { Length: > 0 } name)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var to = Find(pressed, name) is { } target ? PadNav.IsTarget(target) ? target : Inside(target) : null;
            if (to is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
            {
                to.Focus(NavigationMethod.Directional);
                to.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The control of that name round the pressed one: in its view, else in the views round it.</summary>
    private static Control? Find(Control from, string name)
    {
        for (var view = from.FindAncestorOfType<UserControl>(); view is not null; view = view.FindAncestorOfType<UserControl>())
        {
            if (view.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>A list as the next step: the item its page lands on there (the machine on the stage), else the one
    /// picked, else its first.</summary>
    private static Control? Inside(Control list)
    {
        var landing = list.GetSelfAndVisualAncestors().OfType<IPadLanding>().FirstOrDefault()?.Landing();
        if (landing is not null && list.IsVisualAncestorOf(landing))
        {
            return landing;
        }
        var targets = PadNavigator.TargetsIn(list);
        return targets.FirstOrDefault(t => t.Classes.Contains("picked") || t.Classes.Contains("on"))
            ?? (FocusMap.First([.. targets.Select(t => PadNavigator.BoxOf(t, list))]) is { } first ? targets[first] : null);
    }
}
