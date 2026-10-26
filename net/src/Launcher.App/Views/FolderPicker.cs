using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Launcher.App.Views;

/// <summary>The system's folder picker (an older copy's folder: the first screen and the Settings page).</summary>
internal static class FolderPicker
{
    public const string OlderCopyTitle = "The folder of your older copy (or its \"data\" folder)";

    /// <summary>The folder picked, or null when none was (or the system has no picker to show).</summary>
    public static async Task<string?> PickAsync(Control from, string title)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return null;
        }
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false })
            .ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
