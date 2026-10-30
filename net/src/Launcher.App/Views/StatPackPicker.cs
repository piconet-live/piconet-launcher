using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Online.Protocol;

namespace Launcher.App.Views;

/// <summary>The system's file picker for a stat pack file (a custom lobby's rules: the host's own pack).</summary>
internal static class StatPackPicker
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
            Title = "Choose a stat pack",
            FileTypeFilter = [new FilePickerFileType("Stat packs") { Patterns = ["*" + StatPackText.Extension] }],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
