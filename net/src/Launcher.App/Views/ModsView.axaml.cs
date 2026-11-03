using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The Mods page. The view's own part: the system's pickers for a mod's archive or folder.</summary>
public sealed partial class ModsView : UserControl
{
    public ModsView() => AvaloniaXamlLoader.Load(this);

    private async void OnImportFile(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage || DataContext is not ModsViewModel vm)
        {
            return;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a mod",
            FileTypeFilter = [new FilePickerFileType("Mods") { Patterns = ["*.zip", "*.7z"] }],
        }).ConfigureAwait(true);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await vm.PickedAsync(path).ConfigureAwait(true);
        }
    }

    private async void OnImportFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ModsViewModel vm && await FolderPicker.PickAsync(this, "Import a mod's folder").ConfigureAwait(true) is { } folder)
        {
            await vm.PickedAsync(folder).ConfigureAwait(true);
        }
    }
}
