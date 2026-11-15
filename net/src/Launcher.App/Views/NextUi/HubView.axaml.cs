using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned interface's hub (<see cref="HubViewModel"/>). One of its entries always has the focus, the
/// big one at first: the line under them is that entry's. The pointer over an entry moves the focus there as over any
/// control (<see cref="NextFocus.Hover"/>, with the menu's cursor sound).</summary>
public sealed partial class HubView : UserControl
{
    public HubView() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // the page opens on its main action, unless the pad or the keyboard already stands on something in it
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused || !this.IsVisualAncestorOf(focused))
        {
            this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("entry"))?.Focus();
        }
    }

    private void OnEntryFocus(object? sender, RoutedEventArgs e) => Say(sender);

    private void Say(object? sender)
    {
        if (sender is Button { DataContext: HubEntry entry } && DataContext is HubViewModel hub)
        {
            hub.On(entry);
        }
    }
}
