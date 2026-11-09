using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public sealed partial class LobbyChat : UserControl
{
    private readonly ScrollViewer _lines;

    private readonly Button _moreBelow;

    private LobbyViewModel? _lobby;

    /// <summary>The list follows its newest line: the player is at its foot (or was, when the last line came).</summary>
    private bool _following = true;

    public LobbyChat()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Watch();
        // a chat that comes onto the page opens at its newest line. Each stage of the lobby has a chat of its own,
        // and one would otherwise open at its oldest, until somebody writes
        Loaded += (_, _) => Dispatcher.UIThread.Post(ToFoot, DispatcherPriority.Loaded);
        _lines = this.FindControl<ScrollViewer>("Lines")!;
        _moreBelow = this.FindControl<Button>("MoreBelow")!;
        _lines.ScrollChanged += OnScrolled;
    }

    /// <summary>Within a few pixels of the list's foot.</summary>
    private static bool AtFoot(ScrollViewer lines) => lines.Offset.Y >= lines.Extent.Height - lines.Viewport.Height - 4;

    /// <summary>The list grew (a line came, a long one wrapped) or shrank: one that follows goes to its foot. The
    /// player moved it: it follows from the foot on, and More messages below goes once they are there.</summary>
    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer lines)
        {
            return;
        }
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (_following && !Selecting)
            {
                lines.ScrollToEnd();
            }
            return;
        }
        _following = AtFoot(lines);
        if (_following)
        {
            _moreBelow.IsVisible = false;
        }
    }

    /// <summary>More messages below: to the newest line, following again; a selection that held the list goes.</summary>
    private void OnMoreBelow(object? sender, RoutedEventArgs e)
    {
        foreach (var line in Blocks())
        {
            line.ClearSelection();
        }
        ToFoot();
    }

    private void ToFoot()
    {
        _following = true;
        _lines.ScrollToEnd();
        _moreBelow.IsVisible = false;
    }

    /// <summary>The line the menu is open on (the right button, the menu key). The menu is the line's own (its
    /// ContextFlyout), which is why the line keeps its selection while the menu has the keyboard.</summary>
    private SelectableTextBlock? Asked => (Resources["LineMenu"] as FlyoutBase)?.Target as SelectableTextBlock;

    private void Watch()
    {
        _lobby?.Chat.CollectionChanged -= OnChat;
        _lobby = DataContext as LobbyViewModel;
        _lobby?.Chat.CollectionChanged += OnChat;
    }

    /// <summary>Enter sends the message, as the Send button.</summary>
    private void OnMessageKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && e.KeyModifiers == KeyModifiers.None && DataContext is LobbyViewModel lobby)
        {
            lobby.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The lines as they stand on the page.</summary>
    private IEnumerable<SelectableTextBlock> Blocks() =>
        this.FindControl<ItemsControl>("List")?.GetVisualDescendants().OfType<SelectableTextBlock>() ?? [];

    /// <summary>A line has text selected: the player is reading or copying, and the list stays where it is.</summary>
    public bool Selecting => Blocks().Any(l => l.SelectionStart != l.SelectionEnd);

    /// <summary>A new line: a list that follows shows it once it is laid out (<see cref="OnScrolled"/>, and here for a
    /// line that did not change the list's height). A list the player scrolled up, or one held by a selection, stays
    /// where it is (one that stands at its end follows its end by itself: it is put back) and offers More messages
    /// below, as a stream's chat does.</summary>
    private void OnChat(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace))
        {
            return;
        }
        var stood = _lines.Offset;
        var holds = !_following || Selecting;
        Dispatcher.UIThread.Post(() =>
        {
            if (!holds)
            {
                _lines.ScrollToEnd();
                return;
            }
            if (_lines.Offset != stood)
            {
                _lines.Offset = stood;
            }
            if (!AtFoot(_lines))
            {
                _moreBelow.IsVisible = true;
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>What a line says, as it is copied whole: "NAME #4F2A: the text" ("NAME: the text" without a tag).</summary>
    public static string Whole(ChatLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return $"{line.Name}{line.TagText}: {line.Text}";
    }

    /// <summary>Copy: the selection of the line the menu was opened on, or that line whole. The work is done
    /// here, then the menu closed (a menu closed first takes its item out of the tree, and the work would not run).</summary>
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var text = Asked is { SelectedText: { Length: > 0 } selected } ? selected
            : Asked?.DataContext is ChatLine line ? Whole(line) : null;
        await CopyAsync(text).ConfigureAwait(true);
        Close(sender);
    }

    /// <summary>Copy all: every line shown, a line each.</summary>
    private async void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        await CopyAsync(_lobby is { Chat.Count: > 0 } lobby ? string.Join(Environment.NewLine, lobby.Chat.Select(Whole)) : null).ConfigureAwait(true);
        Close(sender);
    }

    private async Task CopyAsync(string? text)
    {
        if (text is { Length: > 0 } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    private void Close(object? sender)
    {
        if (Resources["LineMenu"] is FlyoutBase menu)
        {
            menu.Hide();
        }
    }
}
