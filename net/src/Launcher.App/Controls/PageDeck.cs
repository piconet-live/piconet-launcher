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
/// leaving it takes nothing apart. All the pages are laid out once in the window's first layout pass, before its
/// first frame, and with them the parts of each page that are hidden at that moment (a lobby's stages, the Garage's
/// other phase), so that showing a part finds its styles and templates in place. A window that changes width lays the
/// hidden pages out again, one at a time, once it has stopped.
/// </summary>
public sealed class PageDeck : Panel
{
    public static readonly StyledProperty<IEnumerable?> PagesProperty = AvaloniaProperty.Register<PageDeck, IEnumerable?>(nameof(Pages));

    public static readonly StyledProperty<object?> CurrentProperty = AvaloniaProperty.Register<PageDeck, object?>(nameof(Current));

    private static readonly TimeSpan RefitAfter = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<object, Control> _views = [];
    private readonly ViewLocator _locator = new();
    private Control? _shown;
    private bool _laidOutAhead;
    private double _width = double.NaN;
    private DispatcherTimer? _refit;

    /// <summary>The window's first layout pass is over: from here on what comes onto a hidden page is laid out there.</summary>
    public bool LaidOutAhead => _laidOutAhead;

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
                Add(page);
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
        if (_laidOutAhead)
        {
            Motion.PageShown(view);
            GpuView.Wake(view);
            PageShown?.Invoke(view);
        }
    }

    /// <summary>A hidden page holds neither the focus nor an open drop-down.</summary>
    private void Leave(Control page)
    {
        page.IsVisible = !_laidOutAhead;
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
        if (!_laidOutAhead)
        {
            // the first pass laid out every page; from here only the current one shows (the layout passes again
            // before the window's first frame)
            _laidOutAhead = true;
            _width = finalSize.Width;
            var room = new Size(finalSize.Width, double.PositiveInfinity);
            foreach (var child in Children)
            {
                LayOutHiddenParts(child, room, false);
            }
            foreach (var child in Children.Where(c => c != _shown))
            {
                child.IsVisible = false;
            }
        }
        else if (finalSize.Width != _width)
        {
            _width = finalSize.Width;
            RefitSoon();
        }
        return arranged;
    }

    /// <summary>What stands under a hidden control has had no layout: its styles and templates come with a layout of
    /// its own, in about the room it will have. (Only the room may still change when it shows, and that is the cheap
    /// part.)</summary>
    private static void LayOutHiddenParts(Layoutable at, Size room, bool hiddenAbove)
    {
        var hidden = !at.IsVisible;
        if (hidden && at is TemplatedControl templated)
        {
            templated.ApplyTemplate();
        }
        else if (!hidden && hiddenAbove)
        {
            at.Measure(room);
            at.Arrange(new Rect(at.DesiredSize));
        }
        foreach (var child in at.GetVisualChildren().OfType<Layoutable>().ToList())
        {
            LayOutHiddenParts(child, room, hidden);
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
