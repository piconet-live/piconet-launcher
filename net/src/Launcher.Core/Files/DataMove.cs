using System.Security.Cryptography;

namespace Launcher.Core;

/// <summary>How far a data move is (<see cref="DataMove.Copy"/>): bytes and files of what is to copy.</summary>
public sealed record DataMoveProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal);

/// <summary>What a data move did.</summary>
/// <param name="Done">Everything that was to copy is in the data folder now, checked; false: <paramref name="Message"/>
/// says what stopped it (nothing of the older copy is changed either way).</param>
/// <param name="Message">One plain line for the player.</param>
/// <param name="Copied">Files copied and checked.</param>
/// <param name="Same">Files the data folder had already, the same to the byte: left as they were.</param>
/// <param name="Kept">Files the data folder had already with other contents: the data folder's own are kept (all but
/// the settings, which are the older copy's: <paramref name="SettingsBackup"/>).</param>
/// <param name="Links">Links (junctions, symbolic links) in the older copy's data, never followed: left out.</param>
/// <param name="SettingsBackup">The data folder's own settings file before the older copy's took its place; null:
/// it had none.</param>
public sealed record DataMoveResult(bool Done, string Message, int Copied = 0, int Same = 0, int Kept = 0, int Links = 0,
    string? SettingsBackup = null);

/// <summary>
/// An older player copy's data taken into this launcher's data folder, once, on the player's word (the
/// installed launcher keeps its data in the player's own folder, because an update replaces the
/// program's folder whole; a player who played from an unzipped copy brings its data along). What is taken is what
/// is the player's and costs time to make again: the settings (the player's secret, from which their tag comes, their
/// racer, disc and choices), the replays, the game's own folders (memory card, controller, the fork's), the game files
/// prepared from the disc, and the server's notices. Logs, caches, the status boards and a race's working files are
/// left: the launcher makes them again.
/// <para>It **copies**: the older copy is never changed, moved or deleted, and the player is told it is still there.
/// Every file is copied beside its place, read back and compared with the original (size and SHA-256) before it takes
/// its name, so a file in the data folder is whole or not there. A link of any kind in the older copy (a junction, a
/// symbolic link: older copies had junctions into the disc's files) is never followed nor copied: it is counted and
/// left out. A file the data folder already has is kept as it is, except the settings: those are the older copy's,
/// and the data folder's own are kept beside them (<see cref="SettingsBackupName"/>).</para>
/// <para>The settings name their folders as <see cref="DataFolders"/> reads them, so a settings file of the older
/// copy's data folder finds its replays here when the launcher next starts. The launcher reads its settings once, at
/// its start: after a move it is started again.</para>
/// </summary>
public static class DataMove
{
    /// <summary>What is taken from an older copy's data folder, by name at its top.</summary>
    public static readonly IReadOnlyList<string> Taken =
        [SettingsName, DataFolders.ReplaysName, "recomp-user", DataFolders.UserName, DiscPrep.FolderName, "notices.json"];

    /// <summary>The settings file's name in a data folder.</summary>
    public const string SettingsName = "settings.json";

    /// <summary>The data folder's own settings, kept beside the older copy's that took their place.</summary>
    public const string SettingsBackupName = "settings.before-older-copy.json";

    /// <summary>Of the prepared game's folder: a preparation's lock and its unfinished or retired copies.</summary>
    private static readonly string[] NotOfThePreparedGame = [DiscPrep.LockName, ".preparing", ".old"];

    /// <summary>After a file's name while it is copied and checked.</summary>
    private const string Partial = ".copying";

    public const string NoDataText = "No older copy's data there · choose the folder of the copy, or its \"data\" folder";

    public const string SameFolderText = "That is this launcher's own data folder";

    /// <summary>The older copy's data folder the player's choice names: the copy's folder (its <see cref="Install.DataName"/>
    /// in it) or that data folder itself; one of them must hold a settings file. Null: neither.</summary>
    public static string? DataOf(string chosen)
    {
        ArgumentException.ThrowIfNullOrEmpty(chosen);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        foreach (var folder in (string[])[Path.Combine(full, Install.DataName), full])
        {
            if (File.Exists(Path.Combine(folder, SettingsName)) && !IsLink(folder))
            {
                return folder;
            }
        }
        return null;
    }

    /// <summary>Copies the older copy's data (<paramref name="from"/>, a data folder: <see cref="DataOf"/>) into
    /// <paramref name="to"/>, this launcher's data folder, as the class says. Never throws for the disk's sake: what
    /// went wrong is the result's message. The settings file is copied last, so a move that stopped leaves this
    /// launcher on its own settings.</summary>
    public static DataMoveResult Copy(string from, string to, IProgress<DataMoveProgress>? progress = null, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);
        var (source, target) = (Path.TrimEndingDirectorySeparator(Path.GetFullPath(from)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(to)));
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return new DataMoveResult(false, SameFolderText);
        }
        if (!File.Exists(Path.Combine(source, SettingsName)))
        {
            return new DataMoveResult(false, NoDataText);
        }
        try
        {
            var (files, links) = Listed(source);
            // the settings last: until they are in, the launcher's own are the ones it starts on
            files = [.. files.OrderBy(f => string.Equals(f.Relative, SettingsName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)];
            var total = files.Sum(f => f.Bytes);
            long done = 0;
            int copied = 0, same = 0, kept = 0, count = 0;
            string? backup = null;
            foreach (var (relative, bytes) in files)
            {
                cancel.ThrowIfCancellationRequested();
                var (original, place) = (Path.Combine(source, relative), Path.Combine(target, relative));
                var settings = string.Equals(relative, SettingsName, StringComparison.OrdinalIgnoreCase);
                if (File.Exists(place))
                {
                    if (new FileInfo(place).Length == bytes && Digest(place, cancel).SequenceEqual(Digest(original, cancel)))
                    {
                        same++;
                    }
                    else if (settings)
                    {
                        backup = Path.Combine(target, SettingsBackupName);
                        File.Copy(place, backup, overwrite: true);
                        CopyChecked(original, place, cancel);
                        copied++;
                    }
                    else
                    {
                        kept++;
                    }
                }
                else
                {
                    CopyChecked(original, place, cancel);
                    copied++;
                }
                done += bytes;
                progress?.Report(new DataMoveProgress(done, total, ++count, files.Count));
            }
            var linksLine = links == 0 ? "" : $" · {links} link{(links == 1 ? "" : "s")} left out";
            return new DataMoveResult(true,
                $"Copied {copied} file{(copied == 1 ? "" : "s")}{linksLine} · the older copy is still there: delete it when you like",
                copied, same, kept, links, backup);
        }
        catch (OperationCanceledException)
        {
            return new DataMoveResult(false, "Copy stopped · your data here is as it was, the older copy too");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new DataMoveResult(false, $"Copy stopped · {e.Message} · the older copy is as it was");
        }
    }

    /// <summary>The files to copy, relative to the data folder, with their sizes; and how many links were left out.
    /// Nothing is followed through a link: a folder that is one is not entered.</summary>
    private static (List<(string Relative, long Bytes)> Files, int Links) Listed(string source)
    {
        var files = new List<(string, long)>();
        var links = 0;
        void Walk(DirectoryInfo folder, string relative, bool preparedTop)
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", new EnumerationOptions
                     {
                         RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false,
                     }))
            {
                var name = Path.Combine(relative, entry.Name);
                if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    links++;
                }
                else if (preparedTop && NotOfThePreparedGame.Any(n => entry.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                                                                         entry.Name.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
                {
                    // (at the prepared folder's top) a preparation's lock, or a copy it had not finished or had retired
                }
                else if (entry is DirectoryInfo sub)
                {
                    Walk(sub, name, false);
                }
                else if (entry is FileInfo file && !file.Name.EndsWith(Partial, StringComparison.OrdinalIgnoreCase))
                {
                    files.Add((name, file.Length));
                }
            }
        }
        foreach (var name in Taken)
        {
            var path = Path.Combine(source, name);
            var info = Directory.Exists(path) ? new DirectoryInfo(path) : (FileSystemInfo)new FileInfo(path);
            if (!info.Exists)
            {
                continue;
            }
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                links++;
            }
            else if (info is DirectoryInfo folder)
            {
                Walk(folder, name, name == DiscPrep.FolderName);
            }
            else
            {
                files.Add((name, ((FileInfo)info).Length));
            }
        }
        return (files, links);
    }

    /// <summary>One file copied beside its place, compared with the original, then given its name.</summary>
    private static void CopyChecked(string original, string place, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(place)!);
        var partial = place + Partial;
        try
        {
            File.Copy(original, partial, overwrite: true);
            if (new FileInfo(partial).Length != new FileInfo(original).Length ||
                !Digest(partial, cancel).SequenceEqual(Digest(original, cancel)))
            {
                throw new InvalidDataException($"{Path.GetFileName(original)} did not copy whole");
            }
            File.Move(partial, place, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private static byte[] Digest(string path, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return SHA256.HashData(stream);
    }

    private static bool IsLink(string folder)
    {
        var info = new DirectoryInfo(folder);
        return info.Exists && (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}
