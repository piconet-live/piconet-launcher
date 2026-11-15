using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Controls.NextUi;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's option rows: <see cref="Groups"/> in, the row the focus is on out
/// (<see cref="Focused"/>). When the groups are made again (another mode chosen brings its own rows), the focus stays
/// on the row it was on.</summary>
public sealed partial class OptionList : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<OptionGroup>?> GroupsProperty =
        AvaloniaProperty.Register<OptionList, IReadOnlyList<OptionGroup>?>(nameof(Groups));

    public static readonly StyledProperty<OptionItem?> FocusedProperty =
        AvaloniaProperty.Register<OptionList, OptionItem?>(nameof(Focused), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public OptionList()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(GotFocusEvent, OnGotFocus, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    public IReadOnlyList<OptionGroup>? Groups
    {
        get => GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    public OptionItem? Focused
    {
        get => GetValue(FocusedProperty);
        set => SetValue(FocusedProperty, value);
    }

    /// <summary>The button of a row, by its rule row (null: none shows).</summary>
    public Control? ButtonOf(ViewModels.RuleRow row) =>
        this.GetVisualDescendants().OfType<OptionButton>().FirstOrDefault(b => b.IsEffectivelyVisible && b.DataContext is OptionItem item && item.Row == row);

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control { DataContext: OptionItem item })
        {
            Focused = item;
        }
    }

    private void OnTextFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Control { DataContext: OptionItem item })
        {
            Focused = item;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != GroupsProperty || Focused is not { } was)
        {
            return;
        }
        // the rows were made again: the help panel follows the same row, and a focus that went with the old rows
        // comes back to it (a focus elsewhere stays where it is)
        Dispatcher.UIThread.Post(() =>
        {
            if (Groups?.SelectMany(g => g.Items).FirstOrDefault(i => i.Row == was.Row) is { } same)
            {
                Focused = same;
            }
            var top = TopLevel.GetTopLevel(this);
            if (top?.FocusManager?.GetFocusedElement() is not Control focus || focus == top || !focus.IsAttachedToVisualTree())
            {
                ButtonOf(was.Row)?.Focus(NavigationMethod.Directional);
            }
        }, DispatcherPriority.Loaded);
    }
}
