using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Launcher.App.Controls;

/// <summary>
/// A list that arrives a few items at a time instead of making all its rows in one pass: set
/// <see cref="ItemsProperty"/> in place of <c>ItemsSource</c> on a long list without virtualising (the Garage's tiles,
/// a lobby's seats, the course cards). Between batches the window's thread does its layout and its input, so opening
/// a page with forty rows holds no frame up. A list out of the window holds no rows; a change to the source
/// changes only the rows that differ. A list on a hidden page (<see cref="PageDeck"/>) gets its rows the same way,
/// each batch with its layout, so that showing it has none left to do.
/// </summary>
public static class Paced
{
    /// <summary>The rows made in one go: a row of the slower lists costs up to 8 ms with its styles, so one stays
    /// inside a frame's share of the thread. A list of light rows may ask for more.</summary>
    public static readonly AttachedProperty<int> BatchProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ItemsControl, int>("Batch", 1);

    public static readonly AttachedProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ItemsControl, IEnumerable?>("Items");

    private static readonly AttachedProperty<Feed?> FeedProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ItemsControl, Feed?>("Feed");

    static Paced() => ItemsProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
    {
        var feed = list.GetValue(FeedProperty);
        if (feed is null)
        {
            feed = new Feed(list);
            list.SetValue(FeedProperty, feed);
        }
        feed.Source(e.NewValue as IEnumerable);
    });

    public static int GetBatch(ItemsControl list) => list.GetValue(BatchProperty);

    public static void SetBatch(ItemsControl list, int rows) => list.SetValue(BatchProperty, rows);

    public static IEnumerable? GetItems(ItemsControl list) => list.GetValue(ItemsProperty);

    public static void SetItems(ItemsControl list, IEnumerable? items) => list.SetValue(ItemsProperty, items);

    private sealed class Feed
    {
        private readonly ItemsControl _list;
        private readonly ObservableCollection<object?> _shown = [];
        private IEnumerable? _source;
        private bool _on;
        private bool _going;

        public Feed(ItemsControl list)
        {
            _list = list;
            _list.ItemsSource = _shown;
            _on = TopLevel.GetTopLevel(list) is not null;
            list.AttachedToVisualTree += (_, _) =>
            {
                _on = true;
                Sync();
            };
            list.DetachedFromVisualTree += (_, _) =>
            {
                _on = false;
                _shown.Clear();
            };
        }

        public void Source(IEnumerable? source)
        {
            if (_source is INotifyCollectionChanged before)
            {
                before.CollectionChanged -= Changed;
            }
            _source = source;
            if (_source is INotifyCollectionChanged after)
            {
                after.CollectionChanged += Changed;
            }
            Sync();
        }

        private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

        /// <summary>The rows shown follow the source; the rest come in batches.</summary>
        private void Sync()
        {
            if (!_on)
            {
                return;
            }
            var wanted = _source?.Cast<object?>().ToList() ?? [];
            while (_shown.Count > wanted.Count)
            {
                _shown.RemoveAt(_shown.Count - 1);
            }
            More();
        }

        private void More()
        {
            if (!_on)
            {
                return;
            }
            var wanted = _source?.Cast<object?>().ToList() ?? [];
            var deck = _list.FindAncestorOfType<PageDeck>();
            // a changed row is a row made again: those go in batches too, the first ones first
            var left = GetBatch(_list);
            for (var i = 0; i < _shown.Count && left > 0; i++)
            {
                if (!Equals(_shown[i], wanted[i]))
                {
                    _shown[i] = wanted[i];
                    left--;
                }
            }
            for (; left > 0 && _shown.Count < wanted.Count; left--)
            {
                _shown.Add(wanted[_shown.Count]);
            }
            if (deck is not null && !_list.IsEffectivelyVisible && _list.IsVisible && _list.Bounds.Width > 0)
            {
                // (a hidden page gets no layout passes)
                _list.Measure(new Size(_list.Bounds.Width, double.PositiveInfinity));
                _list.Arrange(new Rect(_list.Bounds.Position, _list.DesiredSize));
            }
            if (!_going && !_shown.SequenceEqual(wanted))
            {
                _going = true;
                _list.Dispatcher.Post(() =>
                {
                    _going = false;
                    More();
                }, DispatcherPriority.Background);
            }
        }
    }
}
