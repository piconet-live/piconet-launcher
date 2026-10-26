using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>Where a made-up image has its parts, and how its file table is written: two images of the same files in
/// two of these differ in the header's layout words, in the table's bytes and in where every file lies.</summary>
/// <param name="DolAt">The executable's place.</param>
/// <param name="TableAt">The file table's place; null: after the last file.</param>
/// <param name="FilesAt">The first file's place.</param>
/// <param name="Step">Each file's place is rounded up to this.</param>
/// <param name="BySize">The files lie in the image by size, smallest first; else in the reverse of their order by name.</param>
/// <param name="Rewritten">The table as another tool writes it: the entries of each folder in the reverse order, and
/// the names in the reverse order with filler between them.</param>
internal sealed record SyntheticLayout(int DolAt, int? TableAt, int FilesAt, int Step, bool BySize, bool Rewritten)
{
    /// <summary>The made-up original pressing: its header's layout words are the pinned ones.</summary>
    public static SyntheticLayout Original { get; } = new(0x3000, 0x4000, 0x5000, 0x20, BySize: false, Rewritten: false);

    /// <summary>A rebuilt, closer-packed copy: the executable elsewhere, the files in another order with 4-byte
    /// steps, and a rewritten table after them.</summary>
    public static SyntheticLayout Rebuilt { get; } = new(0x2600, null, 0x2C00, 4, BySize: true, Rewritten: true);
}

/// <summary>A made-up GameCube disc image for the preparation's tests: a header, a system area, an executable with
/// two sections, a file table with folders (one of them empty) and a dozen files of made-up bytes. Nothing in it
/// comes from a game. The pins it gives are worked out here from the made-up bytes, not by the code under test.</summary>
internal sealed class SyntheticDisc
{
    public const uint TextAddress = 0x80003100;
    public const int TextOffset = 0x100;

    /// <summary>The executable's pinned word: at <see cref="TextAddress"/> + 0x40.</summary>
    public const int DolPin = TextOffset + 0x40;

    /// <summary>The sample module's pinned word.</summary>
    public const int SamplePin = 0x104;

    /// <summary>What the one form of the file table rounds each file's place up to.</summary>
    private const int CanonicalStep = 0x8000;

    /// <summary>The address the made-up original's header says its table loads at.</summary>
    private const uint TableAddress = 0x803FF000;

    public string Id { get; init; } = "GFZE01";

    public byte Disc { get; init; }

    public byte Revision { get; init; }

    public string Title { get; init; } = "MADE-UP TEST DISC";

    /// <summary>Where this image has its parts.</summary>
    public SyntheticLayout Layout { get; init; } = SyntheticLayout.Original;

    public byte[] Dol { get; } = MakeDol();

    /// <summary>The game's files by path; a null value is an empty folder.</summary>
    public SortedDictionary<string, byte[]?> Files { get; } = new(StringComparer.Ordinal)
    {
        ["fze.sample.rel"] = Bytes(1, 0x240),
        ["fze.title.rel"] = Bytes(2, 0x300),
        ["enemy_line/line__.bin"] = Bytes(3, 0x1234),
        ["stage/st01.gma"] = Bytes(4, 70_000),
        ["stage/st01.tpl"] = Bytes(5, 1),
        ["stage/deep/COLI_COURSE01"] = Bytes(6, 5000),
        ["stage/ST00.lz"] = Bytes(12, 3),                   // before "deep" by its bytes, after it in upper case
        ["motion/empty.skl"] = [],
        ["movie/big.sfd"] = Bytes(7, 1_500_000),
        ["opening.bnr"] = Bytes(8, 6496),
        ["test_model"] = null,
    };

    /// <summary>Made-up bytes that are the same in every run.</summary>
    public static byte[] Bytes(int seed, int count)
    {
        var bytes = new byte[count];
#pragma warning disable CA5394 // made-up test data, not a secret
        new Random(seed).NextBytes(bytes);
#pragma warning restore CA5394
        return bytes;
    }

    private static byte[] MakeDol()
    {
        var dol = Bytes(9, 0x400);
        dol.AsSpan(0, 0x100).Clear();
        // text 0: file 0x100, address 0x80003100, 0x200 bytes; data 0 (section 7): file 0x300, address 0x80004000, 0x100
        BinaryPrimitives.WriteUInt32BigEndian(dol, TextOffset);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x48), TextAddress);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x90), 0x200);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(7 * 4), 0x300);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x48 + (7 * 4)), 0x80004000);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x90 + (7 * 4)), 0x100);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0xE0), TextAddress);
        return dol;
    }

    /// <summary>The image's bytes, laid out as <see cref="Layout"/> says. In no layout do the files lie in their
    /// order by name, so the order they are copied in is not the order they are hashed in.</summary>
    public byte[] Build()
    {
        var (table, places, tableAt) = MakeTable(Layout);
        var end = Math.Max(tableAt + table.Length, places.Count == 0 ? 0 : places.Values.Max(p => p.Offset + p.Length));
        var image = new byte[end + 0x40];
        WriteHeader(image, Layout, tableAt, table.Length, end + 0x40);
        Bytes(10, 0x2000).CopyTo(image, 0x440);
        // the loader: a 0x20-byte header with its size (0x14) and its trailer's (0x18), then both
        var loader = Bytes(11, 0x20 + 0x60 + 0x20);
        BinaryPrimitives.WriteUInt32BigEndian(loader.AsSpan(0x14), 0x60);
        BinaryPrimitives.WriteUInt32BigEndian(loader.AsSpan(0x18), 0x20);
        loader.CopyTo(image, 0x2440);
        Dol.CopyTo(image, Layout.DolAt);
        table.CopyTo(image, tableAt);
        foreach (var (path, (offset, _)) in places)
        {
            Files[path]!.CopyTo(image, offset);
        }
        return image;
    }

    /// <summary>Writes the image as a file and returns its path.</summary>
    public string Write(string folder, string name = "made-up.iso")
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Build());
        return path;
    }

    /// <summary>The header: the id, the title, and the eight layout words at 0x420 (the executable's place, the
    /// table's place, size and greatest size, the address it loads at, where the files start, how far they run, and
    /// a word without a use). A rebuilt copy has another of each but the last.</summary>
    private void WriteHeader(Span<byte> image, SyntheticLayout layout, int tableAt, int tableSize, int imageSize)
    {
        Encoding.ASCII.GetBytes(Id).CopyTo(image);
        image[6] = Disc;
        image[7] = Revision;
        BinaryPrimitives.WriteUInt32BigEndian(image[0x1C..], 0xC2339F3D);
        Encoding.ASCII.GetBytes(Title).CopyTo(image[0x20..]);
        var original = layout == SyntheticLayout.Original;
        uint[] words =
        [
            (uint)layout.DolAt, (uint)tableAt, (uint)tableSize, (uint)(original ? tableSize : tableSize + 0x100),
            original ? TableAddress : TableAddress - 0x100, (uint)layout.FilesAt, (uint)(imageSize - layout.FilesAt), 0,
        ];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(image[(0x420 + (i * 4))..], words[i]);
        }
    }

    /// <summary>The header's eight layout words as the preparation writes them: the made-up original's.</summary>
    public uint[] LayoutWords()
    {
        var header = OriginalHeader();
        return [.. Enumerable.Range(0, 8).Select(i => BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x420 + (i * 4))))];
    }

    /// <summary>The file table in the one form the preparation writes, worked out here: the entries of each folder
    /// by name in upper case, the names in the entries' order, the first file where the original's header says the
    /// files start and each next one 32 KiB-rounded after the one before, in the entries' order.</summary>
    public byte[] CanonicalTable() => MakeTable(null).Table;

    /// <summary>The system area's five files as a preparation must write them (before any word), whatever this
    /// image's layout: the header with the original's layout words, and the table in its one form.</summary>
    public Dictionary<string, byte[]> SystemFiles()
    {
        var image = Build();
        return new Dictionary<string, byte[]>
        {
            ["sys/boot.bin"] = OriginalHeader(),
            ["sys/bi2.bin"] = image[0x440..0x2440],
            ["sys/apploader.img"] = image[0x2440..(0x2440 + 0xA0)],
            ["sys/main.dol"] = Dol,
            ["sys/fst.bin"] = CanonicalTable(),
        };
    }

    private byte[] OriginalHeader()
    {
        var (table, places, tableAt) = MakeTable(SyntheticLayout.Original);
        var end = Math.Max(tableAt + table.Length, places.Count == 0 ? 0 : places.Values.Max(p => p.Offset + p.Length));
        var header = new byte[0x440];
        WriteHeader(header, SyntheticLayout.Original, tableAt, table.Length, end + 0x40);
        return header;
    }

    /// <summary>The pins of this disc: the system files and three of the game's files by name, the digest over every
    /// file (computed here, by the rule <see cref="DiscPrep.ContentDigest"/> documents), and one word in the
    /// executable and one in the sample module.</summary>
    public DiscPins Pins()
    {
        var named = SystemFiles().ToDictionary(f => f.Key, f => new NamedFile(f.Value.Length, Sha(f.Value)));
        foreach (var path in new[] { "fze.sample.rel", "fze.title.rel", "enemy_line/line__.bin" })
        {
            named["files/" + path] = new NamedFile(Files[path]!.Length, Sha(Files[path]!));
        }
        var real = Files.Where(f => f.Value is not null).ToList();
        return new DiscPins(Id, Disc, Revision, "the made-up game", real.Count, FolderCount(), real.Sum(f => (long)f.Value!.Length), Digest(), named,
        [
            new PatchWord("sys/main.dol", DolPin, Word(Dol, DolPin), 0x38600000, "sample-alloc-pad", TextAddress + 0x40),
            new PatchWord("files/fze.sample.rel", SamplePin, Word(Files["fze.sample.rel"]!, SamplePin), 0x38A00000, "mainrel-pad"),
        ], LayoutWords());
    }

    /// <summary>The digest over every file, worked out the long way: records sorted by path, each the path's length
    /// (8 bytes, big-endian), the path, the size (8 bytes), the file's SHA-256 as lower-case hex text.</summary>
    public string Digest()
    {
        using var all = new MemoryStream();
        Span<byte> number = stackalloc byte[8];
        foreach (var (path, data) in Files.Where(f => f.Value is not null).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            BinaryPrimitives.WriteUInt64BigEndian(number, (ulong)path.Length);
            all.Write(number);
            all.Write(Encoding.ASCII.GetBytes(path));
            BinaryPrimitives.WriteUInt64BigEndian(number, (ulong)data!.Length);
            all.Write(number);
            all.Write(Encoding.ASCII.GetBytes(Sha(data)));
        }
        return Sha(all.ToArray());
    }

    public static uint Word(byte[] file, int offset) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset));

    public static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private int FolderCount() =>
        Files.SelectMany(f => Parents(f.Key).Concat(f.Value is null ? [f.Key] : [])).Distinct(StringComparer.Ordinal).Count();

    private static IEnumerable<string> Parents(string path)
    {
        for (var at = path.IndexOf('/', StringComparison.Ordinal); at >= 0; at = path.IndexOf('/', at + 1))
        {
            yield return path[..at];
        }
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public string? Path { get; set; }

        public bool Folder { get; set; }

        public SortedDictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The file table (12-byte entries, then the names), where each file's bytes go, and where the table
    /// goes. With no layout: the table in its one form (<see cref="CanonicalTable"/>).</summary>
    private (byte[] Table, Dictionary<string, (int Offset, int Length)> Places, int TableAt) MakeTable(SyntheticLayout? layout)
    {
        var root = new Node("") { Folder = true };
        foreach (var (path, data) in Files)
        {
            var at = root;
            var parts = path.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                if (!at.Children.TryGetValue(parts[i], out var child))
                {
                    at.Children[parts[i]] = child = new Node(parts[i]);
                }
                child.Folder = i < parts.Length - 1 || data is null;
                at = child;
            }
            at.Path = data is null ? null : path;
        }
        IEnumerable<Node> Ordered(Node folder) => layout switch
        {
            null => folder.Children.Values.OrderBy(c => c.Name.ToUpperInvariant(), StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal),
            { Rewritten: true } => folder.Children.Values.Reverse(),
            _ => folder.Children.Values,
        };

        // the entries first (which fixes the one form's order of files), then the places, then the names
        var walk = new List<(Node Node, int Parent, int Next)>();
        void Walk(Node folder, int parent)
        {
            foreach (var child in Ordered(folder))
            {
                var index = walk.Count + 1;
                walk.Add((child, parent, 0));
                if (child.Folder)
                {
                    Walk(child, index);
                    walk[index - 1] = (child, parent, walk.Count + 1);
                }
            }
        }
        Walk(root, 0);
        var files = walk.Where(e => !e.Node.Folder).Select(e => e.Node.Path!).ToList();
        var lie = layout switch
        {
            null => files,
            { BySize: true } => [.. files.OrderBy(f => Files[f]!.Length).ThenBy(f => f, StringComparer.Ordinal)],
            _ => [.. files.Order(StringComparer.Ordinal).Reverse()],
        };
        var step = layout?.Step ?? CanonicalStep;
        var places = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        var next = layout?.FilesAt ?? SyntheticLayout.Original.FilesAt;
        foreach (var path in lie)
        {
            places[path] = (next, Files[path]!.Length);
            next = (next + Files[path]!.Length + step - 1) / step * step;
        }
        var names = new MemoryStream();
        var nameAt = new Dictionary<Node, int>();
        foreach (var (node, _, _) in layout is { Rewritten: true } ? walk.AsEnumerable().Reverse() : walk)
        {
            if (layout is { Rewritten: true })
            {
                names.Write("#\0#"u8);
            }
            nameAt[node] = (int)names.Length;
            names.Write(Encoding.ASCII.GetBytes(node.Name));
            names.WriteByte(0);
        }
        var table = new byte[((walk.Count + 1) * 12) + (int)names.Length];
        void Entry(int index, byte flag, int name, uint first, uint second)
        {
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(index * 12), ((uint)flag << 24) | (uint)name);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan((index * 12) + 4), first);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan((index * 12) + 8), second);
        }
        Entry(0, 1, 0, 0, (uint)walk.Count + 1);
        for (var i = 0; i < walk.Count; i++)
        {
            var (node, parent, last) = walk[i];
            if (node.Folder)
            {
                Entry(i + 1, 1, nameAt[node], (uint)parent, (uint)last);
            }
            else
            {
                Entry(i + 1, 0, nameAt[node], (uint)places[node.Path!].Offset, (uint)places[node.Path!].Length);
            }
        }
        names.ToArray().CopyTo(table, (walk.Count + 1) * 12);
        var tableAt = layout?.TableAt ?? ((next + 0xFF) & ~0xFF);
        if (layout?.TableAt is not null && tableAt + table.Length > layout.FilesAt)
        {
            throw new InvalidOperationException("the made-up file table outgrew its room");
        }
        return (table, places, tableAt);
    }
}
