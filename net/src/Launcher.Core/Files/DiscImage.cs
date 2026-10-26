using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Fzgx.GameCube.Disc;

namespace Launcher.Core;

/// <summary>What kind of file a disc image is, by its first bytes.</summary>
public enum DiscImageKind
{
    /// <summary>None the launcher knows.</summary>
    Unknown,

    /// <summary>A plain GameCube image (.iso, .gcm): the one kind the launcher reads.</summary>
    GameCube,

    /// <summary>A plain Wii image.</summary>
    Wii,

    /// <summary>Dolphin's compressed formats.</summary>
    Rvz,
    Wia,
    Gcz,

    /// <summary>Compact ISO (.ciso).</summary>
    Ciso,

    /// <summary>A Wii backup container (.wbfs).</summary>
    Wbfs,

    /// <summary>An NKit image (.nkit.iso): a GameCube image with its filler removed and its files moved.</summary>
    NKit,

    /// <summary>A .zip, .7z or .rar with the image inside.</summary>
    Archive,
}

/// <summary>
/// A plain GameCube disc image as the game's files are prepared from it (<see cref="DiscPrep"/>): the system area
/// (the five files Dolphin writes into <c>sys/</c>), every file with its place in the image, and the folders. The
/// image is a file from outside: every size it states is held against the image's own length and against what a
/// GameCube disc can hold (<see cref="ConsoleMemory"/> and the file table's limits) before anything is allocated
/// for it, and the file table is read by one reader (<see cref="TableOf"/>).
/// <para>Two of the five system files say where things lie in the image and so differ between two images of the
/// same files (a rebuilt or shrunk copy): the header's words at <see cref="LayoutAt"/>, and the file table, which
/// holds every file's place. <see cref="CanonicalTable"/> writes the table in one form for any image.</para>
/// </summary>
public sealed partial class DiscImage : IDisposable
{
    /// <summary>The header: the disc header (0x440 bytes), which holds the places of the executable and the file table.</summary>
    public const int HeaderSize = 0x440;

    /// <summary>Where the header says how the image is laid out: eight words from here to the header's end. In
    /// order: where the executable is, where the file table is, the table's size, its greatest size, the address the
    /// table is loaded at (which follows from its size), where the files start, how far they run, and a word
    /// without a use. Everything before is the same in every image of a game's pressing.</summary>
    public const int LayoutAt = 0x420;

    /// <summary>The number of words at <see cref="LayoutAt"/>.</summary>
    public const int LayoutWords = 8;

    /// <summary>Which of the words at <see cref="LayoutAt"/> says where the files start.</summary>
    public const int LayoutFilesAt = 5;

    /// <summary>What <see cref="CanonicalTable"/> rounds each file's place up to: 32 KiB, the runner's own step when
    /// it builds a disc from a folder.</summary>
    public const int CanonicalAlignment = 0x8000;

    /// <summary>The console's main memory: 24 MiB. It loads the loader, the executable and the file table whole
    /// into it, so no disc has one of them larger, and none is read here beyond it (the game's: 116,484 bytes,
    /// 1,414,848 and 129,405).</summary>
    public const int ConsoleMemory = 24 << 20;

    private const uint GameCubeMagic = GcmDisc.DiscMagic;
    private const uint WiiMagic = 0x5D1C9EA3;
    private const int InfoSize = 0x2000;
    private const int LoaderAt = HeaderSize + InfoSize;
    private const int DolHeaderSize = 0x100;

    private readonly FileStream _file;
    private readonly byte[] _header;

    private DiscImage(FileStream file, byte[] header)
    {
        _file = file;
        _header = header;
        Length = file.Length;
        GameId = Encoding.ASCII.GetString(header, 0, 6);
        Disc = header[6];
        Revision = header[7];
        var title = header.AsSpan(0x20, 0x3E0);
        var end = title.IndexOf((byte)0);
        Title = Encoding.ASCII.GetString(end < 0 ? title : title[..end]).Trim();

        var loader = new byte[0x20];
        Read(LoaderAt, loader, "sys/apploader.img");
        var loaderSize = 0x20L + BinaryPrimitives.ReadUInt32BigEndian(loader.AsSpan(0x14)) + BinaryPrimitives.ReadUInt32BigEndian(loader.AsSpan(0x18));
        long dolAt = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x420));
        long tableAt = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x424));
        long tableSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x428));
        var dol = new byte[DolHeaderSize];
        Read(dolAt, dol, "sys/main.dol");
        SystemFiles =
        [
            Part("sys/boot.bin", 0, HeaderSize),
            Part("sys/bi2.bin", HeaderSize, InfoSize),
            Part("sys/apploader.img", LoaderAt, loaderSize),
            Part("sys/main.dol", dolAt, DolSize(dol)),
            Part("sys/fst.bin", tableAt, tableSize),
        ];
    }

    /// <summary>The image's length in bytes.</summary>
    public long Length { get; }

    /// <summary>The six-character game id: <c>GFZE01</c> for F-Zero GX (USA).</summary>
    public string GameId { get; }

    /// <summary>The disc's number in a set (0: the first or only one).</summary>
    public int Disc { get; }

    /// <summary>The game's revision (0: the first pressing).</summary>
    public int Revision { get; }

    /// <summary>The title in the disc header.</summary>
    public string Title { get; }

    /// <summary>The system area's files, named as in the prepared folder (<c>sys/boot.bin</c>, <c>sys/bi2.bin</c>,
    /// <c>sys/apploader.img</c>, <c>sys/main.dol</c>, <c>sys/fst.bin</c>), each inside the image.</summary>
    public IReadOnlyList<DiscFile> SystemFiles { get; }

    /// <summary>What kind of file starts with these bytes (the first <see cref="HeaderSize"/> of it, or all it has).</summary>
    public static DiscImageKind Kind(ReadOnlySpan<byte> start)
    {
        if (start.Length >= 4)
        {
            if (start[..4].SequenceEqual("RVZ\u0001"u8))
            {
                return DiscImageKind.Rvz;
            }
            if (start[..4].SequenceEqual("WIA\u0001"u8))
            {
                return DiscImageKind.Wia;
            }
            if (start[..4].SequenceEqual((ReadOnlySpan<byte>)[0x01, 0xC0, 0x0B, 0xB1]))
            {
                return DiscImageKind.Gcz;
            }
            if (start[..4].SequenceEqual("CISO"u8))
            {
                return DiscImageKind.Ciso;
            }
            if (start[..4].SequenceEqual("WBFS"u8))
            {
                return DiscImageKind.Wbfs;
            }
            if (start[..4].SequenceEqual("PK\u0003\u0004"u8) || start[..4].SequenceEqual("Rar!"u8) ||
                start[..4].SequenceEqual((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF]))
            {
                return DiscImageKind.Archive;
            }
        }
        if (start.Length >= 0x20 && BinaryPrimitives.ReadUInt32BigEndian(start[0x18..]) == WiiMagic)
        {
            return DiscImageKind.Wii;
        }
        if (start.Length >= 0x20 && BinaryPrimitives.ReadUInt32BigEndian(start[0x1C..]) == GameCubeMagic)
        {
            return start.Length >= 0x204 && start.Slice(0x200, 4).SequenceEqual("NKIT"u8) ? DiscImageKind.NKit : DiscImageKind.GameCube;
        }
        return DiscImageKind.Unknown;
    }

    /// <summary>What kind of file is at <paramref name="path"/>.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static DiscImageKind Kind(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var start = new byte[HeaderSize];
        return Kind(start.AsSpan(0, file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false)));
    }

    /// <summary>Opens a plain GameCube image (<see cref="Kind(string)"/> says whether a file is one).</summary>
    /// <exception cref="InvalidDataException">The file is not a plain GameCube image, or it ends inside its own
    /// system area.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static DiscImage Open(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        try
        {
            var header = new byte[HeaderSize];
            var read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (Kind(header.AsSpan(0, read)) is not (DiscImageKind.GameCube or DiscImageKind.NKit))
            {
                throw new InvalidDataException("Not a plain GameCube disc image.");
            }
            if (read < HeaderSize)
            {
                throw new InvalidDataException("The image ends inside its header.");
            }
            return new DiscImage(file, header);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Every file of the game with its place in the image (paths from the disc's root with '/', in the
    /// order of their paths), and every folder, the empty ones too.</summary>
    /// <exception cref="DiscLimitException">The file table passes a limit (<see cref="TableOf"/>).</exception>
    /// <exception cref="InvalidDataException">The file table is broken or names a file twice, or a file lies outside
    /// the image (an image that was cut short).</exception>
    public (IReadOnlyList<DiscFile> Files, IReadOnlyList<string> Folders) Table()
    {
        var table = SystemFiles[4];
        if (table.Length > LargestTable)
        {
            throw new DiscLimitException(TooLarge(table.Length));   // before its bytes are allocated
        }
        var bytes = new byte[table.Length];
        Read(table.Offset, bytes, table.Path);
        var (files, folders) = TableOf(bytes, Length);
        // a path twice, or twice but for upper and lower case, which Windows would write into one file
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (files.FirstOrDefault(f => !seen.Add(f.Path)) is { Path: not null } twice)
        {
            throw new InvalidDataException($"The file table names {twice.Path} twice.");
        }
        return ([.. files.OrderBy(f => f.Path, StringComparer.Ordinal)], folders);
    }

    /// <summary>Reads bytes of the image.</summary>
    /// <param name="what">What is being read, for the message when the image ends before it.</param>
    /// <exception cref="InvalidDataException">The image ends inside the range.</exception>
    public void Read(long offset, Span<byte> buffer, string what)
    {
        if (offset < 0 || offset + buffer.Length > Length)
        {
            throw new InvalidDataException($"The image ends inside {what}.");
        }
        _file.Position = offset;
        _file.ReadExactly(buffer);
    }

    public void Dispose() => _file.Dispose();

    private DiscFile Part(string name, long offset, long size)
    {
        if (size > ConsoleMemory)
        {
            throw new DiscLimitException(string.Create(CultureInfo.InvariantCulture,
                $"Its {name} is {size:N0} bytes, and the console loads that file whole into {ConsoleMemory:N0} bytes of memory."));
        }
        if (size <= 0 || offset + size > Length)
        {
            throw new InvalidDataException($"The image ends inside {name}.");
        }
        return new DiscFile(name, offset, (int)size);
    }

    /// <summary>An executable's length: the end of its last section (18 sections: offsets at 0, sizes at 0x90).</summary>
    private static long DolSize(ReadOnlySpan<byte> header)
    {
        long size = DolHeaderSize;
        for (var i = 0; i < 18; i++)
        {
            long at = BinaryPrimitives.ReadUInt32BigEndian(header[(i * 4)..]);
            long length = BinaryPrimitives.ReadUInt32BigEndian(header[(0x90 + (i * 4))..]);
            size = length > 0 ? Math.Max(size, at + length) : size;
        }
        return size;
    }
}
