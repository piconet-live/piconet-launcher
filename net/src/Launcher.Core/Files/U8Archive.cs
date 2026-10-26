using System.Buffers.Binary;
using System.Text;

namespace Launcher.Core;

/// <summary>
/// A U8 archive (<c>.arc</c>, on the disc as <c>.arc.lz</c>): the game keeps each machine's full model and its
/// textures in one (<c>vehicle/NAME_p.arc</c>, the player's machine; <c>NAME_e.arc</c>, the opponents'). The header
/// (magic 0x55AA382D) points at a table of 12-byte nodes: type (1 a folder), the name's offset in the names after the
/// table (3 bytes), and for a file its data's offset and size. Only file names are kept; the folders are not needed to
/// find a machine's files, which have names of their own.
/// </summary>
public sealed class U8Archive
{
    private const uint Magic = 0x55AA382D;
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Size)> _files = new(StringComparer.Ordinal);

    private U8Archive(byte[] data) => _data = data;

    /// <summary>The archive's file names.</summary>
    public IEnumerable<string> Names => _files.Keys;

    /// <exception cref="InvalidDataException">Not a U8 archive, or its table does not fit.</exception>
    public static U8Archive Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 0x20 || BinaryPrimitives.ReadUInt32BigEndian(data) != Magic)
        {
            throw new InvalidDataException("Not a U8 archive.");
        }
        var root = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
        if (root < 0 || root + 12 > data.Length)
        {
            throw new InvalidDataException("The archive's node table does not fit.");
        }
        // the first node is the root folder; its size is the number of nodes
        var count = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(root + 8));
        var names = root + (count * 12L);
        if (count < 1 || names > data.Length)
        {
            throw new InvalidDataException("The archive's node table does not fit.");
        }
        var archive = new U8Archive(data);
        for (var i = 1; i < count; i++)
        {
            var node = data.AsSpan(root + (i * 12), 12);
            if (node[0] != 0)
            {
                continue;
            }
            var name = (int)(BinaryPrimitives.ReadUInt32BigEndian(node) & 0xFFFFFF) + (int)names;
            var end = name < data.Length ? Array.IndexOf(data, (byte)0, name) : -1;
            var offset = BinaryPrimitives.ReadInt32BigEndian(node[4..]);
            var size = BinaryPrimitives.ReadInt32BigEndian(node[8..]);
            if (end < 0 || offset < 0 || size < 0 || (long)offset + size > data.Length)
            {
                throw new InvalidDataException($"Node {i} does not fit the archive.");
            }
            archive._files[Encoding.ASCII.GetString(data, name, end - name)] = (offset, size);
        }
        return archive;
    }

    /// <summary>The file's bytes, or null when the archive has no file of that name.</summary>
    public byte[]? File(string name) =>
        _files.TryGetValue(name, out var f) ? _data.AsSpan(f.Offset, f.Size).ToArray() : null;
}
