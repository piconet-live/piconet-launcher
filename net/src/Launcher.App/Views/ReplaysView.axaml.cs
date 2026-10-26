using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The Replays page. The view's own part: the system's file picker for a shared replay, and which columns
/// a narrow window keeps.</summary>
public sealed partial class ReplaysView : UserControl
{
    /// <summary>Narrower than this the table has no room for the courses' pictures.</summary>
    private const double Narrow = 760;

    public ReplaysView()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, e) => Classes.Set("narrow", e.NewSize.Width < Narrow);
    }

    /// <summary>A shared replay: the system's file picker (a view concern), then the view model plays it.</summary>
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
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.PlayCommand.Execute(path);
        }
    }
}
