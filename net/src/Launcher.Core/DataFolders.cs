namespace Launcher.Core;

/// <summary>
/// The two folders the launcher keeps in its data folder unless the player says otherwise: the replays
/// (<see cref="LauncherSettings.ReplayFolder"/>, which the Settings page can set) and the retired fork's user folder
/// (<see cref="LauncherSettings.UserFolder"/>, which no page sets). While the launcher runs both are full paths; in
/// <c>settings.json</c> a folder that is the data folder's own is **not written** (<see cref="ForTheFile"/>), so a
/// data folder that is moved, or a copy that is renamed, takes its replays along: "everything a copy writes stays
/// in the copy" would not hold otherwise.
/// <para>The first build wrote both as full paths at its first start. Such a file, moved into another copy with its
/// data folder (as the read-me says to), names folders of the copy it came from: the Replays page would be empty
/// and new replays would be written into the old copy. <see cref="AtHome"/> tells those paths from a folder the
/// player chose: the user folder was never the player's to set, so a stored one of the default's name
/// (<see cref="UserName"/>) is some data folder's own, and that data folder is the one above it, when the stored
/// replay folder says the same (it is that folder's <see cref="ReplaysName"/>, as a first start wrote the two) or
/// the folder is named as a data folder is (<see cref="Install.DataName"/>, or as this one). A replay folder in
/// it is then this data folder's; any other replay folder is the player's choice and is kept.</para>
/// <para>Paths are compared as text, with either separator and without regard to case: the file may come from
/// another system than the one the test runs on, and nothing here asks the disk.</para>
/// </summary>
public static class DataFolders
{
    /// <summary>The replays' folder in a data folder.</summary>
    public const string ReplaysName = "replays";

    /// <summary>The retired fork's user folder in a data folder (the game takes its controller from there once).</summary>
    public const string UserName = "dolphin-user";

    /// <summary>The settings as read from the file, with both folders as this data folder has them: the data
    /// folder's own where the file names none or names another data folder's own, the player's choice where the
    /// file names one.</summary>
    public static LauncherSettings AtHome(LauncherSettings read, string dataFolder)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        var (user, replays) = (Path.Combine(dataFolder, UserName), Path.Combine(dataFolder, ReplaysName));
        var was = WrittenFor(read, dataFolder);
        return read with
        {
            UserFolder = read.UserFolder is null || was is not null ? user : read.UserFolder,
            ReplayFolder = read.ReplayFolder is null || (was is not null && Same(read.ReplayFolder, was + "/" + ReplaysName))
                ? replays : read.ReplayFolder,
        };
    }

    /// <summary>The settings as they are written: a folder that is this data folder's own is not named.</summary>
    public static LauncherSettings ForTheFile(LauncherSettings settings, string dataFolder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        return settings with
        {
            UserFolder = settings.UserFolder is { } user && Same(user, Path.Combine(dataFolder, UserName)) ? null : settings.UserFolder,
            ReplayFolder = settings.ReplayFolder is { } replays && Same(replays, Path.Combine(dataFolder, ReplaysName)) ? null : settings.ReplayFolder,
        };
    }

    /// <summary>The data folder the file's full paths were written for, when it is another than this one; null: the
    /// file names no data folder's own user folder, or this one's.</summary>
    private static string? WrittenFor(LauncherSettings read, string dataFolder)
    {
        if (read.UserFolder is not { } user || !Name(user).Equals(UserName, StringComparison.OrdinalIgnoreCase) || Above(user) is not { } was)
        {
            return null;
        }
        var itsOwn = read.ReplayFolder is null || Same(read.ReplayFolder, was + "/" + ReplaysName) ||
            Name(was).Equals(Install.DataName, StringComparison.OrdinalIgnoreCase) || Name(was).Equals(Name(dataFolder), StringComparison.OrdinalIgnoreCase);
        return itsOwn && !Same(was, dataFolder) ? was : null;
    }

    /// <summary>A path as it is compared: one separator, none at the end.</summary>
    private static string Plain(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');

    private static bool Same(string a, string b) => Plain(a).Equals(Plain(b), StringComparison.OrdinalIgnoreCase);

    private static string Name(string path) => Plain(path) is var plain && plain.LastIndexOf('/') is >= 0 and var at ? plain[(at + 1)..] : plain;

    private static string? Above(string path) => Plain(path) is var plain && plain.LastIndexOf('/') is > 0 and var at ? plain[..at] : null;
}
