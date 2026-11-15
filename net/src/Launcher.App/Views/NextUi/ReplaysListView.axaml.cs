using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Replays: the list, and a shared replay opened from a file.</summary>
public sealed partial class ReplaysListView : UserControl
{
    public ReplaysListView() => AvaloniaXamlLoader.Load(this);

    /// <summary>"Open a file…": the system's file picker (a view's concern), then the replay plays, as on the classic
    /// page.</summary>
    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage || DataContext is not ReplaysViewModel vm)
        {
            return;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a replay",
            FileTypeFilter = [new FilePickerFileType("Replays") { Patterns = ["*.fzrp"] }],
        }).ConfigureAwait(true);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && vm.PlayCommand.CanExecute(path))
        {
            vm.PlayCommand.Execute(path);
        }
    }
}
