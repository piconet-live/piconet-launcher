using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Launcher.Core.Pads;

namespace Launcher.App.Controls.NextUi;

/// <summary>A page view that is told when the deck shows it and hides it (the page shown and left, as a view that is
/// put in the window and taken out of it would know it by itself).</summary>
public interface IDeckPage
{
    void Showing(bool shown);
}

/// <summary>
/// The redesigned shell's place for the page (<see cref="Page"/>, the page view model): the page showing, and every
/// page shown before it, kept in the window and hidden. A page shown again is its view as it was, styled and laid out
/// already: a change of page shows a view instead of building, styling and laying out its whole tree anew, which made
/// the window stand still for a moment on every change to a large page (the Garage's grid of machines, Practice's
/// courses and rows). A view is made the first time its page shows, by the data templates round the deck (the
/// shell's view locator). The first start's page is not kept: it shows once. A view hidden with the focus in it gives
/// the focus up (no key reaches a page that does not show), and keeps where it was: shown again, the pad lands there,
/// as a game's menu keeps its cursor (<see cref="Landing"/>); a page seen for the first time lands where it says, else
/// on its own list (<c>PadNav.Home</c>: the item picked there, else its first).
/// </summary>
public sealed class PageDeck : Panel, Views.IPadLanding
{
    public static readonly StyledProperty<object?> PageProperty = AvaloniaProperty.Register<PageDeck, object?>(nameof(Page));

    private readonly Dictionary<object, Control> _views = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where the focus was in each view when it was hidden.</summary>
    private readonly Dictionary<Control, Control> _cursor = [];

    public PageDeck()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, false);
        // a press on a page: the focus to its next step, where the page says one (PadNav.Next)
        AddHandler(Button.ClickEvent, (_, e) => Views.PadFollow.Pressed(e.Source as Control), Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>The view of the page showing; null: none.</summary>
    public Control? Shown { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            Show(change.NewValue);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Shown is null && Page is { } page)
        {
            Show(page);   // (the templates that make the views are found once the deck is in the window)
        }
    }

    private void Show(object? page)
    {
        var view = page is null ? null : ViewOf(page);
        if (view is null && page is not null && VisualRoot is null)
        {
            return;   // not in the window yet: shown once it is
        }
        foreach (var other in Children.ToList())
        {
            if (ReferenceEquals(other, view))
            {
                continue;
            }
            if (other.IsVisible)
            {
                if (other.IsKeyboardFocusWithin)
                {
                    if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control was && PadNav.IsTarget(was))
                    {
                        _cursor[other] = was;   // (where the cursor stood: back there when the page shows again)
                    }
                    Focus();   // (the deck holds the focus, which no key presses anything on)
                }
                other.IsVisible = false;
                (other as IDeckPage)?.Showing(false);
            }
            if (!_views.ContainsValue(other))
            {
                Children.Remove(other);   // (a page that is not kept: let go)
            }
        }
        Shown = view;
        if (view is not null && !view.IsVisible)
        {
            view.IsVisible = true;
            (view as IDeckPage)?.Showing(true);
        }
    }

    /// <summary>A page's view made and laid out ahead, hidden, so that the first change to that page is at once as
    /// well (the shell asks for its large pages while the window is idle after its start): made as when the page shows,
    /// see-through and laid out over the page showing, then hidden. Nothing for a page made already or showing.</summary>
    public void Prepare(object page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_views.ContainsKey(page) || ReferenceEquals(page, Page) || VisualRoot is null || page is ViewModels.DiscViewModel)
        {
            return;
        }
        if (ViewOf(page) is not { } view)
        {
            return;
        }
        (view.Opacity, view.IsHitTestVisible) = (0, false);
        view.Measure(Bounds.Size);   // (its templates, styles and layout: now, not when it first shows)
        view.Arrange(new Rect(Bounds.Size));
        view.IsVisible = false;
        (view as IDeckPage)?.Showing(false);
        (view.Opacity, view.IsHitTestVisible) = (1, true);
    }

    /// <summary>Where the pad lands on the page showing: where the cursor stood when it was left; else where the page
    /// says (<see cref="Views.IPadLanding"/>); else its own list's item picked, else that list's first. Null: none of
    /// these (the pad takes the page's first control).</summary>
    public Control? Landing()
    {
        if (Shown is not { } view)
        {
            return null;
        }
        if (_cursor.TryGetValue(view, out var was) && was.IsEffectivelyVisible && was.IsEffectivelyEnabled && view.IsVisualAncestorOf(was))
        {
            return was;
        }
        var said = (view as Views.IPadLanding ?? view.GetVisualDescendants().OfType<Views.IPadLanding>()
            .FirstOrDefault(l => l is Visual { IsEffectivelyVisible: true }))?.Landing();
        if (said is not null)
        {
            return said;
        }
        if (view.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => PadNav.GetHome(c) && c.IsEffectivelyVisible) is not { } home)
        {
            return null;
        }
        var targets = Views.PadNavigator.TargetsIn(home);
        return targets.FirstOrDefault(t => t.Classes.Contains("picked") || t.Classes.Contains("on"))
            ?? (FocusMap.First([.. targets.Select(t => Views.PadNavigator.BoxOf(t, view))]) is { } first ? targets[first] : null);
    }

    /// <summary>The page's view: the one kept, else one made (and kept, unless the page is the first start's).</summary>
    private Control? ViewOf(object page)
    {
        if (_views.TryGetValue(page, out var kept))
        {
            return kept;
        }
        if (this.FindDataTemplate(page)?.Build(page) is not { } made)
        {
            return null;
        }
        made.DataContext = page;
        made.ClipToBounds = false;   // (a focus ring round a card at the page's edge stands outside it; the shell's margin holds it)
        if (page is not ViewModels.DiscViewModel)
        {
            _views[page] = made;
        }
        Children.Add(made);
        return made;
    }
}
