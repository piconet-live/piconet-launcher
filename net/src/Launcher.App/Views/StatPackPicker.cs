using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Online.Protocol;

namespace Launcher.App.Views;

/// <summary>The system's file picker for a stat pack file or a mode file (a custom lobby's rules: the host's own).</summary>
internal static class StatPackPicker
{
    /// <summary>The file picked, or null when none was (or the system has no picker to show).</summary>
    public static Task<string?> PickAsync(Control from) =>
        PickAsync(from, "Choose a stat pack", "Stat packs", StatPackText.Extension);

    /// <summary>A mode file picked (<c>.fzmode</c>), or null.</summary>
    public static Task<string?> PickModeAsync(Control from) =>
        PickAsync(from, "Choose a mode", "Modes", Launcher.Core.HostModeFile.Extension);

    private static async Task<string?> PickAsync(Control from, string title, string kind, string extension)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = ["*" + extension] }],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
