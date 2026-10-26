using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Fzgx.GameCube.Disc;

namespace Launcher.Core;

/// <summary>An image holds what no GameCube disc holds: one of <see cref="DiscImage"/>'s limits is passed. The
/// message is a sentence for the player. An <see cref="IOException"/>, so that whoever reads an image and catches
/// what reading can throw has caught this too.</summary>
public sealed class DiscLimitException(string message) : IOException(message);

// The file table: read, and written in one form.
public sealed partial class DiscImage
{
    /// <summary>The largest file table that is read: 4 MiB. The console loads the table whole into its memory
    /// (<see cref="ConsoleMemory"/>, 24 MiB, with the game beside it); the game's own is 129,405 bytes.</summary>
    public const int LargestTable = 4 << 20;

    /// <summary>The most entries (files and folders, with the root) a table may have: 65,536. The format has no
    /// rule; the game has 4,682, and with <see cref="LongestPath"/> this number bounds what a table costs to read
    /// (about 40 MB at the worst).</summary>
    public const int MostEntries = 1 << 16;

    /// <summary>The longest name of a file or folder, in bytes: 255, the longest name the file systems the game's
    /// files are written to can hold (NTFS, ext4, APFS). The game's longest is 29.</summary>
    public const int LongestName = 255;

    /// <summary>The longest path from the disc's root, in characters: 255. Windows opens no path longer than 260
    /// without special care, and the folder the files are written under comes before this. The game's longest is 37.</summary>
    public const int LongestPath = 255;

    /// <summary>Folders inside folders: 32 at the most. The format has no rule; the game goes 2 deep.</summary>
    public const int DeepestFolder = 32;

    /// <summary>The files and folders a file table (<c>sys/fst.bin</c>) names: the launcher's one reader of a
    /// table, for an image's own and for a prepared folder's. The layout: entries of 12 bytes, a flag (1: a folder),
    /// a 24-bit offset into the names after the entries; for a file its place and length, for a folder its parent
    /// and the entry just past its last child. The limits (<see cref="LargestTable"/>, <see cref="MostEntries"/>,
    /// <see cref="LongestName"/>, <see cref="LongestPath"/>, <see cref="DeepestFolder"/>) are checked on the bytes,
    /// before a name is made into text or a list grows, so a table costs a bounded amount whatever it says.</summary>
    /// <param name="imageLength">The image's length: every file must lie inside it. Left out for a table without
    /// its image (a prepared folder's).</param>
    /// <exception cref="DiscLimitException">The table passes a limit.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a file table, or a file lies outside the image.</exception>
    public static (IReadOnlyList<DiscFile> Files, IReadOnlyList<string> Folders) TableOf(ReadOnlySpan<byte> table, long imageLength = long.MaxValue)
    {
        if (table.Length < 12)
        {
            throw new InvalidDataException("The file table is too short.");
        }
        if (table.Length > LargestTable)
        {
            throw new DiscLimitException(TooLarge(table.Length));
        }
        var count = BinaryPrimitives.ReadUInt32BigEndian(table[8..]);
        if (count == 0 || count * 12L > table.Length)
        {
            throw new InvalidDataException("The file table's entry count does not fit it.");
        }
        if (count > MostEntries)
        {
            throw new DiscLimitException(string.Create(CultureInfo.InvariantCulture,
                $"Its file table names {count:N0} files and folders, and no GameCube disc is read with more than {MostEntries:N0}."));
        }
        var names = table[((int)count * 12)..];
        var files = new List<DiscFile>();
        var folders = new List<string>();
        var open = new Stack<(string Path, uint End)>();
        open.Push(("", count));
        for (uint i = 1; i < count; i++)
        {
            while (i >= open.Peek().End)
            {
                open.Pop();
            }
            var entry = table.Slice((int)i * 12, 12);
            var nameAt = (int)(BinaryPrimitives.ReadUInt32BigEndian(entry) & 0xFFFFFF);
            // the name's end is looked for in the bytes a name may have and no further
            var room = nameAt < names.Length ? names.Slice(nameAt, Math.Min(LongestName + 1, names.Length - nameAt)) : default;
            var length = room.IndexOf((byte)0);
            if (length < 0)
            {
                throw room.Length > LongestName
                    ? new DiscLimitException(string.Create(CultureInfo.InvariantCulture, $"Its file table has a name longer than {LongestName} characters."))
                    : new InvalidDataException("The file table has a name without an end.");
            }
            var name = room[..length];
            if (name.Contains((byte)'/'))
            {
                // a name is one part of a path: with a '/' in it, it would be folders the depth limit never counted
                throw new DiscLimitException("Its file table has a name with a '/' in it.");
            }
            var parent = open.Peek().Path;
            if (parent.Length + (parent.Length == 0 ? 0 : 1) + length > LongestPath)
            {
                throw new DiscLimitException(string.Create(CultureInfo.InvariantCulture, $"Its file table has a path longer than {LongestPath} characters, under {parent}."));
            }
            var path = (parent.Length == 0 ? "" : parent + "/") + Encoding.ASCII.GetString(name);
            var first = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var second = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            if (entry[0] == 0)
            {
                if (second > int.MaxValue)
                {
                    throw new InvalidDataException($"The file table's file {path} has no valid length.");
                }
                if (first + (long)second > imageLength)
                {
                    throw new InvalidDataException($"The image ends inside {path}.");
                }
                files.Add(new DiscFile(path, first, (int)second));
                continue;
            }
            if (second <= i || second > count)
            {
                throw new InvalidDataException($"The file table's folder {path} has no valid end.");
            }
            if (open.Count > DeepestFolder)
            {
                throw new DiscLimitException(string.Create(CultureInfo.InvariantCulture, $"Its file table has folders more than {DeepestFolder} deep, under {parent}."));
            }
            folders.Add(path);
            open.Push((path, second));
        }
        return (files, folders);
    }

    /// <summary>The file table of these files and folders in one form, whatever image they came from: what
    /// <see cref="DiscPrep"/> writes as <c>sys/fst.bin</c>, so that two images of the same files give the same
    /// prepared folder. The table of a disc that holds the files as the runner lays them out when it builds its disc
    /// from a folder (Dolphin's DirectoryBlob): inside each folder the entries sorted by name in upper case (two
    /// names that differ in case only: by the name itself), a folder followed by what is in it; each name once, in
    /// the entries' order; the first file at <paramref name="filesAt"/> and each next one at the end of the one
    /// before, rounded up to <see cref="CanonicalAlignment"/>. Only names, sizes and folders go in: nothing of where
    /// the image had its files, in what order, or how its own table was written.</summary>
    /// <param name="files">Every file: its path from the disc's root with '/', and its length.</param>
    /// <param name="folders">Every folder's path. One that holds a file need not be named; an empty one must be.</param>
    /// <param name="filesAt">Where the first file lies (the header's word <see cref="LayoutFilesAt"/>).</param>
    /// <exception cref="InvalidDataException">A path is there twice (a file and a folder of one name too), or the
    /// files do not fit a disc's 32-bit places.</exception>
    public static byte[] CanonicalTable(IEnumerable<(string Path, int Length)> files, IEnumerable<string> folders, uint filesAt)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folders);
        var root = new Entry("", null);
        Entry Folder(string path)
        {
            var at = root;
            foreach (var part in path.Split('/'))
            {
                if (!at.Children!.TryGetValue(part, out var child))
                {
                    at.Children[part] = child = new Entry(part, null);
                }
                at = child.Children is null ? throw new InvalidDataException($"The file table names {path} as a file and as a folder.") : child;
            }
            return at;
        }
        foreach (var folder in folders)
        {
            Folder(folder);
        }
        foreach (var (path, length) in files)
        {
            var cut = path.LastIndexOf('/');
            var parent = cut < 0 ? root : Folder(path[..cut]);
            if (!parent.Children!.TryAdd(path[(cut + 1)..], new Entry(path[(cut + 1)..], length)))
            {
                throw new InvalidDataException($"The file table names {path} twice.");
            }
        }

        var entries = new List<(uint Flag, uint Name, uint First, uint Second)> { default };
        using var names = new MemoryStream();
        long place = filesAt;
        static Entry[] Sorted(Entry folder) =>
            [.. folder.Children!.Values.OrderBy(c => c.Name.ToUpperInvariant(), StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal)];

        // the folders that are open, the innermost on top, each with the child that comes next: a list of its own
        // and no call inside a call, so that a path of any depth costs memory and never the stack
        var open = new Stack<(Entry[] Children, int Next, uint Index, uint Name, uint Parent)>();
        open.Push((Sorted(root), 0, 0, 0, 0));
        while (open.Count > 0)
        {
            var (children, next, index, folderName, parent) = open.Pop();
            if (next == children.Length)
            {
                entries[(int)index] = (1, folderName, parent, (uint)entries.Count);
                continue;
            }
            open.Push((children, next + 1, index, folderName, parent));
            var child = children[next];
            var name = (uint)names.Length;
            names.Write(Encoding.ASCII.GetBytes(child.Name));
            names.WriteByte(0);
            var own = (uint)entries.Count;
            if (child.Children is not null)
            {
                entries.Add(default);
                open.Push((Sorted(child), 0, own, name, index));
                continue;
            }
            if (place > uint.MaxValue)
            {
                throw new InvalidDataException("The files do not fit a GameCube disc.");
            }
            entries.Add((0, name, (uint)place, (uint)child.Length!.Value));
            place = (place + child.Length.Value + CanonicalAlignment - 1) / CanonicalAlignment * CanonicalAlignment;
        }
        if (names.Length > 0xFFFFFF)
        {
            throw new InvalidDataException("The file names do not fit a file table.");
        }
        var table = new byte[(entries.Count * 12) + (int)names.Length];
        for (var i = 0; i < entries.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(i * 12), (entries[i].Flag << 24) | entries[i].Name);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan((i * 12) + 4), entries[i].First);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan((i * 12) + 8), entries[i].Second);
        }
        names.ToArray().CopyTo(table, entries.Count * 12);
        return table;
    }

    private static string TooLarge(long size) => string.Create(CultureInfo.InvariantCulture,
        $"Its file table is {size:N0} bytes, and no GameCube disc's is read beyond {LargestTable:N0}.");

    /// <summary>A file (with its length) or a folder (with what is in it) while a table is written.</summary>
    private sealed class Entry(string name, int? length)
    {
        public string Name { get; } = name;

        public int? Length { get; } = length;

        public Dictionary<string, Entry>? Children { get; } = length is null ? new(StringComparer.Ordinal) : null;
    }
}
