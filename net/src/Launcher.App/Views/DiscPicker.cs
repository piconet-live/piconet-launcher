using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Launcher.App.Views;

/// <summary>The system's file picker for the player's disc image (the first screen and the Settings page).</summary>
internal static class DiscPicker
{
    /// <summary>The file picked, or null when none was (or the system has no picker to show).</summary>
    public static async Task<string?> PickAsync(Control from)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Your F-Zero GX (USA) disc image",
            FileTypeFilter = [new FilePickerFileType("Disc images") { Patterns = ["*.iso", "*.gcm"] }],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
