using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Launcher.App.Controls;

/// <summary>
/// A list that arrives a few items at a time instead of making all its rows in one pass: set
/// <see cref="ItemsProperty"/> in place of <c>ItemsSource</c> on a long list without virtualising (the Garage's tiles,
/// a lobby's seats, the course cards). Between batches the window's thread does its layout and its input, so opening
/// a page with forty rows holds no frame up. While the list is not on screen it holds no rows; a change to the source
/// changes only the rows that differ.
/// </summary>
public static class Paced
{
    /// <summary>The rows made in one go: a row of the slower lists costs 1 to 3 ms.</summary>
    private const int Batch = 4;

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
            for (var i = Math.Min(_shown.Count, wanted.Count) - 1; i >= 0; i--)
            {
                if (!Equals(_shown[i], wanted[i]))
                {
                    _shown[i] = wanted[i];
                }
            }
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
            for (var i = 0; i < Batch && _shown.Count < wanted.Count; i++)
            {
                _shown.Add(wanted[_shown.Count]);
            }
            if (_shown.Count < wanted.Count && !_going)
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
