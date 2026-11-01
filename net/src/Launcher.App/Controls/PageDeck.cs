using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Gpu;

namespace Launcher.App.Controls;

/// <summary>
/// The window's pages, all kept in the window: the one current shows, the others stand hidden with their styles, their
/// templates and their layout already done. Going to a page is then showing it, with nothing to attach or style, and
/// leaving it takes nothing apart. The window opens on the current page alone; the others come into the window behind
/// it after that, in small steps with the thread free between them (<see cref="Continue"/>), and with them the parts
/// of each page that are hidden at that moment (a lobby's stages, the Garage's other phase), so that showing a part
/// finds its styles and templates in place. A window that changes width lays the hidden pages out again, one at a
/// time, once it has stopped.
/// </summary>
public sealed class PageDeck : Panel
{
    public static readonly StyledProperty<IEnumerable?> PagesProperty = AvaloniaProperty.Register<PageDeck, IEnumerable?>(nameof(Pages));

    public static readonly StyledProperty<object?> CurrentProperty = AvaloniaProperty.Register<PageDeck, object?>(nameof(Current));

    private static readonly TimeSpan RefitAfter = TimeSpan.FromMilliseconds(300);

    /// <summary>How long one go of the work behind the page may run before the thread is let go.</summary>
    private const double SliceMs = 4;

    /// <summary>The window's first page has its entrance before the work behind it starts.</summary>
    private static readonly TimeSpan BehindAfter = TimeSpan.FromMilliseconds(450);

    private readonly Dictionary<object, Control> _views = [];
    private readonly ViewLocator _locator = new();
    private Control? _shown;
    private bool _opened;
    private readonly Queue<object> _waiting = [];
    private readonly Stack<(Layoutable At, bool HiddenAbove)> _templates = [];
    private readonly Queue<Layoutable> _layouts = [];
    private bool _continuing;
    private double _width = double.NaN;
    private DispatcherTimer? _refit;

    /// <summary>Every page is in the window with its templates and layout done.</summary>
    public bool Ready => _opened && !_continuing;

    /// <summary>All the pages have become ready.</summary>
    public event Action? BecameReady;

    /// <summary>A page came into view.</summary>
    public event Action<Control>? PageShown;

    public IEnumerable? Pages
    {
        get => GetValue(PagesProperty);
        set => SetValue(PagesProperty, value);
    }

    public object? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PagesProperty && change.NewValue is IEnumerable pages)
        {
            foreach (var page in pages)
            {
                _waiting.Enqueue(page);
            }
        }
        else if (change.Property == CurrentProperty)
        {
            Show(change.NewValue);
        }
    }

    private Control? Add(object? page)
    {
        if (page is null)
        {
            return null;
        }
        if (_views.TryGetValue(page, out var known))
        {
            return known;
        }
        if (_locator.Build(page) is not { } view)
        {
            return null;
        }
        _views[page] = view;
        view.DataContext = page;
        view.IsVisible = page == Current;
        Children.Add(view);
        return view;
    }

    private void Show(object? page)
    {
        var view = Add(page);
        if (view == _shown)
        {
            return;
        }
        if (_shown is { } before)
        {
            Leave(before);
        }
        _shown = view;
        if (view is null)
        {
            return;
        }
        view.IsVisible = true;
        if (_opened)
        {
            Motion.PageShown(view);
            GpuView.Wake(view);
            PageShown?.Invoke(view);
        }
    }

    /// <summary>A hidden page holds neither the focus nor an open drop-down.</summary>
    private void Leave(Control page)
    {
        page.IsVisible = false;
        if (TopLevel.GetTopLevel(this)?.FocusManager is { } focus && focus.GetFocusedElement() is Visual on && page.IsVisualAncestorOf(on))
        {
            focus.Focus(null);
        }
        foreach (var box in page.GetVisualDescendants().OfType<ComboBox>().Where(b => b.IsDropDownOpen))
        {
            box.IsDropDownOpen = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (!_opened)
        {
            _opened = true;
            _width = finalSize.Width;
            _continuing = true;
            var thread = Dispatcher.UIThread;
            _ = Task.Delay(BehindAfter).ContinueWith(_ => thread.Post(Continue, DispatcherPriority.Background), TaskScheduler.Default);
        }
        else if (finalSize.Width != _width)
        {
            _width = finalSize.Width;
            RefitSoon();
        }
        return arranged;
    }

    private void ContinueSoon() => Dispatcher.UIThread.Post(Continue, DispatcherPriority.Background);

    /// <summary>A little of the work behind the page, then the thread is free: the next page into the window (its
    /// styles come with that), the templates of everything on it, what is hidden on it included, a control at a time,
    /// then the layout of what stands under something hidden, which gets none from the window.</summary>
    private void Continue()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        do
        {
            if (_templates.TryPop(out var next))
            {
                Template(next.At, next.HiddenAbove);
            }
            else if (_layouts.TryDequeue(out var part))
            {
                part.Measure(new Size(_width, double.PositiveInfinity));
                part.Arrange(new Rect(part.DesiredSize));
            }
            else if (_waiting.TryDequeue(out var page))
            {
                if (Add(page) is { } view)
                {
                    _templates.Push((view, false));
                }
                break;   // (a page's styles are the biggest single step: nothing more in this go)
            }
            else
            {
                _continuing = false;
                BecameReady?.Invoke();
                return;
            }
        }
        while (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < SliceMs);
        ContinueSoon();
    }

    private void Template(Layoutable at, bool hiddenAbove)
    {
        var hidden = !at.IsVisible;
        at.ApplyTemplate();
        if (!hidden && hiddenAbove)
        {
            _layouts.Enqueue(at);
        }
        foreach (var child in at.GetVisualChildren().OfType<Layoutable>().Reverse())
        {
            _templates.Push((child, hidden));
        }
    }

    /// <summary>The window is another width: once it has stopped, each hidden page has its layout for this width, one
    /// a turn, so that going to one finds it done.</summary>
    private void RefitSoon()
    {
        if (_refit is null)
        {
            _refit = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefitAfter };
            _refit.Tick += (_, _) =>
            {
                _refit.Stop();
                RefitFrom(0);
            };
        }
        _refit.Stop();
        _refit.Start();
    }

    private void RefitFrom(int index)
    {
        var width = _width;
        var room = new Size(width, double.PositiveInfinity);
        for (; index < Children.Count; index++)
        {
            if (Children[index] is { IsVisible: false } page)
            {
                foreach (var part in page.GetVisualChildren().OfType<Layoutable>())
                {
                    part.Measure(room);
                    part.Arrange(new Rect(part.DesiredSize));
                }
                var next = index + 1;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_width == width)
                    {
                        RefitFrom(next);
                    }
                }, DispatcherPriority.Background);
                return;
            }
        }
    }
}
