using SharpCompress.Archives;

namespace Launcher.Core;

/// <summary>One file of a mod as it came: its path in the archive or folder (forward slashes) and its size.</summary>
public sealed record ModEntry(string Path, long Size);

/// <summary>
/// A mod as it was handed over: a .zip, a .7z or a folder. Somebody else's, so it can be anything: its paths are taken
/// only when they stay inside it (no "..", no root, no drive), and its sizes are held to limits before anything is read.
/// Nothing in it is run. The archives are read with SharpCompress, which the launcher already carries (the replays'
/// xz reader); a .7z is read from start to end in one pass, as a solid archive must be.
/// </summary>
public static class ModArchive
{
    /// <summary>The most files a mod may have, the largest one, and all of them together, unpacked.</summary>
    public const int MostFiles = 50_000;

    public const long LargestFile = 512L << 20, MostBytes = 8L << 30;

    /// <summary>The kinds of file the importer takes.</summary>
    public static bool IsArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every file of the mod; or why it cannot be read.</summary>
    public static (IReadOnlyList<ModEntry>? Entries, string? Refusal) List(string source)
    {
        try
        {
            var entries = new List<ModEntry>();
            if (Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    entries.Add(new ModEntry(Path.GetRelativePath(source, file).Replace('\\', '/'), new FileInfo(file).Length));
                    if (entries.Count > MostFiles)
                    {
                        return (null, "Too many files");
                    }
                }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(source);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory || Safe(entry.Key) is not { } path)
                    {
                        continue;
                    }
                    entries.Add(new ModEntry(path, entry.Size));
                    if (entries.Count > MostFiles)
                    {
                        return (null, "Too many files");
                    }
                }
            }
            if (entries.Exists(e => e.Size > LargestFile) || entries.Sum(e => e.Size) > MostBytes)
            {
                return (null, "Too large");
            }
            return entries.Count == 0 ? (null, "Empty") : (entries, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                      or NotSupportedException or InvalidDataException or SharpCompress.Common.ArchiveException)
        {
            return (null, "Not readable");
        }
    }

    /// <summary>Reads the wanted files in the mod's own order, each handed over whole (at most
    /// <see cref="LargestFile"/>), and stops when <paramref name="cancel"/> is.</summary>
    public static void Each(string source, Func<string, bool> wanted, Action<string, byte[]> take, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(take);
        if (Directory.Exists(source))
        {
            foreach (var (entry, file) in List(source).Entries!.Select(e => (e, Path.Combine(source, e.Path))))
            {
                cancel.ThrowIfCancellationRequested();
                if (wanted(entry.Path))
                {
                    take(entry.Path, File.ReadAllBytes(file));
                }
            }
            return;
        }
        using var archive = ArchiveFactory.OpenArchive(source);
        if (!archive.IsSolid && archive.Type != SharpCompress.Common.ArchiveType.SevenZip)
        {
            // a zip: each wanted entry opened on its own
            foreach (var entry in archive.Entries)
            {
                cancel.ThrowIfCancellationRequested();
                if (!entry.IsDirectory && Safe(entry.Key) is { } path && wanted(path) && entry.Size <= LargestFile)
                {
                    using var stream = entry.OpenEntryStream();
                    if (Whole(stream, entry.Size) is { } bytes)
                    {
                        take(path, bytes);
                    }
                }
            }
            return;
        }
        // a 7z or another solid archive: one pass from start to end
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            cancel.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (!entry.IsDirectory && Safe(entry.Key) is { } path && wanted(path) && entry.Size <= LargestFile)
            {
                using var stream = reader.OpenEntryStream();
                if (Whole(stream, entry.Size) is { } bytes)
                {
                    take(path, bytes);
                }
            }
        }
    }

    /// <summary>An entry's bytes, read to at most <see cref="LargestFile"/>; null when it holds more.</summary>
    private static byte[]? Whole(Stream stream, long size)
    {
        using var bytes = new MemoryStream((int)Math.Clamp(size, 0, LargestFile));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            bytes.Write(buffer, 0, read);
            if (bytes.Length > LargestFile)
            {
                return null;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>An archive's path as a relative path with forward slashes, or null when it would leave the folder it
    /// is unpacked into.</summary>
    public static string? Safe(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }
        var path = key.Replace('\\', '/').Trim('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." || p.Contains(':', StringComparison.Ordinal) || p.Any(char.IsControl)) ||
            key.StartsWith('/') || key.StartsWith('\\'))
        {
            return null;
        }
        return string.Join('/', parts);
    }
}
