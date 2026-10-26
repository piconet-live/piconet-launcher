using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public sealed partial class DiscView : UserControl
{
    public DiscView() => AvaloniaXamlLoader.Load(this);

    /// <summary>"Bring over an older copy": its folder by the system's folder picker; the page copies it.</summary>
    private async void OnBringOver(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiscViewModel vm && await FolderPicker.PickAsync(this, FolderPicker.OlderCopyTitle).ConfigureAwait(true) is { } folder)
        {
            await vm.Move.BringOverAsync(folder).ConfigureAwait(true);
        }
    }

    private async void OnChoose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiscViewModel vm && await DiscPicker.PickAsync(this).ConfigureAwait(true) is { } path)
        {
            vm.Path = path;
        }
    }
}
