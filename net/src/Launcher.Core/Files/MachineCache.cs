using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core;

/// <summary>A machine as the garage loads it, in the form the cache keeps: its model with its textures decoded, its
/// paint (a stock machine's; null for a custom machine) and where a custom machine's parts stand (null for a stock
/// machine).</summary>
public sealed record CachedMachine(MachineModel Model, MachinePaint? Paint, IReadOnlyList<Vector3>? Anchors);

/// <summary>
/// Where the machines' models are kept once read from the disc: <c>cache/machines/DISC/NAME.bin</c> in the data
/// folder, DISC the disc's key (<see cref="DiscKey"/>), NAME <c>m05</c> for stock machine 5 or <c>c15-44-58</c> for a
/// custom machine's parts. A later load reads the file and skips the disc, its LZ, the files' parsing and the textures'
/// decoding (loading from the disc is slow, and the disc can be on a network share). The
/// file is the model as the garage takes it, textures as RGBA, compressed (zlib, fastest); another form of it
/// (<see cref="Recipe"/>) is another key, so an old file is never read as a new form. A file that cannot be read whole
/// and sound is no file: the machine is read from the disc again and the file written anew.
/// </summary>
public static class MachineCache
{
    /// <summary>The kept form's version: raised whenever <see cref="MachineModel"/>, its reading or this file's
    /// layout changes.</summary>
    public const int Recipe = 1;

    private const uint Magic = 0x434D5A46;   // "FZMC"

    // what a sound file never exceeds (a machine's is a few megabytes unpacked)
    private const int LargestFile = 32 << 20;
    private const int LargestUnpacked = 96 << 20;
    private const int MostTextures = 512;
    private const int MostMeshes = 8192;
    private const int MostVertices = 1 << 21;

    /// <summary>The folder for the disc's machines.</summary>
    public static string Folder(string dataFolder, string discKey) => Path.Combine(dataFolder, "cache", "machines", discKey);

    /// <summary>Stock machine <paramref name="machine"/>'s file in that folder.</summary>
    public static string StockFile(string folder, int machine) =>
        Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"m{machine:00}.bin"));

    /// <summary>A custom machine's file in that folder, by its three parts.</summary>
    public static string CustomFile(string folder, int body, int cockpit, int booster) =>
        Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"c{body}-{cockpit}-{booster}.bin"));

    /// <summary>The Garage's card stills' look: raised whenever their camera, lighting or drawing changes.</summary>
    public const int StillRecipe = 1;

    /// <summary>A card still of stock machine <paramref name="machine"/> in a look (stock colour <paramref name="stock"/>,
    /// else the colour <paramref name="paint"/>) of this size, in that folder:
    /// <c>still-m06-s0-176x104-r1.png</c>, <c>still-m06-pE8432E-176x104-r1.png</c>.</summary>
    public static string StillFile(string folder, int machine, int? stock, Online.Protocol.Rgb? paint, int width, int height)
    {
        var look = stock is { } k ? $"s{k}" : paint is { } c ? $"p{c.R:X2}{c.G:X2}{c.B:X2}" : "none";
        return Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"still-m{machine:00}-{look}-{width}x{height}-r{StillRecipe}.png"));
    }

    /// <summary>A card still of a custom machine (its three parts) in its parts' colours (body, cockpit, booster; null a
    /// part's own colour) of this size, in that folder: <c>still-c2-25-50-E8432E.own.own-176x104-r1.png</c>. (Never a
    /// painted stock still's name: <see cref="KeepStill"/> keeps every colour of a custom machine.)</summary>
    public static string CustomStillFile(string folder, int body, int cockpit, int booster, IReadOnlyList<Online.Protocol.Rgb?> colours,
        int width, int height)
    {
        ArgumentNullException.ThrowIfNull(colours);
        var look = string.Join(".", Enumerable.Range(0, 3).Select(k => k < colours.Count && colours[k] is { } c ? $"{c.R:X2}{c.G:X2}{c.B:X2}" : "own"));
        return Path.Combine(folder, string.Create(CultureInfo.InvariantCulture,
            $"still-c{body}-{cockpit}-{booster}-{look}-{width}x{height}-r{StillRecipe}.png"));
    }

    /// <summary>Keeps a card still's PNG bytes (whole or not there, as <see cref="CourseStillCache.Keep"/>). A painted
    /// look's still replaces the machine's other painted stills: one colour a machine is kept, not every colour the
    /// wheel passed through.</summary>
    public static void KeepStill(string file, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(file);
        var name = Path.GetFileName(file);
        if (name.Length > 11 && name.StartsWith("still-m", StringComparison.Ordinal) && name[10] == 'p' && Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder))
        {
            try
            {
                foreach (var other in Directory.EnumerateFiles(folder, name[..11] + "*.png").Where(f => Path.GetFileName(f) != name))
                {
                    File.Delete(other);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // kept beside it
            }
        }
        CourseStillCache.Keep(file, png);
    }

    /// <summary>The disc's key: 16 hexadecimal characters of a SHA-256 over the machines' files as the disc's file
    /// table names them (<paramref name="files"/>: name, place and length, in any order), the shared machine
    /// textures' bytes (<paramref name="shared"/>) and the <see cref="Recipe"/>. The table is read when the disc is
    /// opened, so the key costs one small read; a disc whose machine files changed in place without moving or changing
    /// size (no rebuilt image does that) would keep its key.</summary>
    public static string DiscKey(IEnumerable<(string Name, long Offset, long Length)> files, byte[]? shared)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes($"machines {Recipe}\n"));
        foreach (var (name, offset, length) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{name} {offset} {length}\n")));
        }
        hash.AppendData(shared ?? []);
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    /// <summary>A kept machine; null when there is none, or it cannot be read whole and sound (then it is read from
    /// the disc again).</summary>
    public static CachedMachine? Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length is < 8 or > LargestFile)
            {
                return null;
            }
            using var stream = File.OpenRead(file);
            using var header = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (header.ReadUInt32() != Magic || header.ReadInt32() != Recipe)
            {
                return null;
            }
            using var unpacked = new MemoryStream();
            using (var zlib = new ZLibStream(stream, CompressionMode.Decompress))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = zlib.Read(buffer)) > 0)
                {
                    if (unpacked.Length + n > LargestUnpacked)
                    {
                        return null;
                    }
                    unpacked.Write(buffer, 0, n);
                }
            }
            unpacked.Position = 0;
            using var reader = new BinaryReader(unpacked);
            var machine = ReadMachine(reader);
            return reader.ReadUInt32() == Magic && unpacked.Position == unpacked.Length ? machine : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
            or EndOfStreamException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Keeps a machine: written beside its file, then moved in, so that a file is whole or not there. One
    /// that cannot be written is read from the disc again next time.</summary>
    public static void Keep(string file, CachedMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Online.Protocol.AtomicFile.Write(file, part =>
            {
                using var stream = File.Create(part);
                using (var header = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
                {
                    header.Write(Magic);
                    header.Write(Recipe);
                }
                using var zlib = new ZLibStream(stream, CompressionLevel.Fastest);
                using var writer = new BinaryWriter(zlib);
                WriteMachine(writer, machine);
                writer.Write(Magic);   // the end: a file cut inside its last block lacks it
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // read from the disc again next time
        }
    }

    private static void WriteMachine(BinaryWriter w, CachedMachine machine)
    {
        var model = machine.Model;
        w.Write(model.OwnTextures);
        w.Write(model.Textures.Count);
        foreach (var texture in model.Textures)
        {
            w.Write(texture is not null);
            if (texture is { Picture: var p })
            {
                w.Write(texture.AlphaOnly);
                w.Write(p.Width);
                w.Write(p.Height);
                w.Write(p.Rgba.Length);
                w.Write(p.Rgba);
            }
        }
        w.Write(model.Meshes.Count);
        foreach (var mesh in model.Meshes)
        {
            var m = mesh.Material;
            Write(w, m.Diffuse);
            Write(w, m.Ambient);
            Write(w, m.Specular);
            w.Write(m.Shininess);
            w.Write(m.Alpha);
            w.Write(m.Texture);
            w.Write(m.Translucent);
            w.Write(m.TwoSided);
            w.Write((byte)m.WrapS);
            w.Write((byte)m.WrapT);
            w.Write(m.Painted);
            w.Write(m.Part);
            var vertices = mesh.Positions.Length;
            if (mesh.Normals.Length != vertices || mesh.Uvs.Length != vertices || mesh.Colors.Length != vertices)
            {
                throw new IOException("a mesh whose vertex lists differ in length is not kept");
            }
            w.Write(vertices);
            Bulk(w, mesh.Positions);
            Bulk(w, mesh.Normals);
            Bulk(w, mesh.Uvs);
            Bulk(w, mesh.Colors);
            w.Write(mesh.Indices.Length);
            Bulk(w, mesh.Indices);
        }
        var paint = machine.Paint?.Kept;
        w.Write(paint is not null);
        if (paint is { } kept)
        {
            foreach (var file in kept.Files)
            {
                w.Write(file.Length);
                w.Write(file);
            }
            Ints(w, kept.Blocks);
            Ints(w, kept.Texels);
            Ints(w, kept.Painted);
        }
        w.Write(machine.Anchors?.Count ?? -1);
        foreach (var a in machine.Anchors ?? [])
        {
            Write(w, a);
        }
    }

    private static CachedMachine ReadMachine(BinaryReader r)
    {
        var own = r.ReadInt32();
        var textures = new MachineTexture?[Count(r, MostTextures)];
        if (own < 0 || own > textures.Length)
        {
            throw new InvalidDataException("own textures");
        }
        for (var t = 0; t < textures.Length; t++)
        {
            if (!r.ReadBoolean())
            {
                continue;
            }
            var alphaOnly = r.ReadBoolean();
            var (width, height) = (r.ReadInt32(), r.ReadInt32());
            var length = r.ReadInt32();
            if (width is <= 0 or > DiscArt.LargestTexture || height is <= 0 or > DiscArt.LargestTexture || length != width * height * 4)
            {
                throw new InvalidDataException("texture size");
            }
            textures[t] = new MachineTexture(new Picture(width, height, Exactly(r, length)), alphaOnly);
        }
        var meshes = new MachineMesh[Count(r, MostMeshes)];
        for (var m = 0; m < meshes.Length; m++)
        {
            var (diffuse, ambient, specular) = (Vector3Of(r), Vector3Of(r), Vector3Of(r));
            var (shininess, alpha, texture) = (r.ReadSingle(), r.ReadSingle(), r.ReadInt32());
            var (translucent, twoSided) = (r.ReadBoolean(), r.ReadBoolean());
            var (wrapS, wrapT) = (Wrap(r.ReadByte()), Wrap(r.ReadByte()));
            var (painted, part) = (r.ReadBoolean(), r.ReadInt32());
            if (texture < -1 || texture >= textures.Length || part is < -1 or > 2)
            {
                throw new InvalidDataException("material");
            }
            var vertices = Count(r, MostVertices);
            var positions = Bulk<Vector3>(r, vertices);
            var normals = Bulk<Vector3>(r, vertices);
            var uvs = Bulk<Vector2>(r, vertices);
            var colors = Bulk<Vector4>(r, vertices);
            var indices = Bulk<int>(r, Count(r, MostVertices * 3));
            if (indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= vertices))
            {
                throw new InvalidDataException("indices");
            }
            meshes[m] = new MachineMesh(positions, normals, uvs, colors, indices,
                new MachineMaterial(diffuse, ambient, specular, shininess, alpha, texture, translucent, twoSided, wrapS, wrapT, painted, part));
        }
        MachinePaint? paint = null;
        if (r.ReadBoolean())
        {
            var files = new byte[4][];
            for (var f = 0; f < 4; f++)
            {
                files[f] = Exactly(r, Count(r, DiscArt.LargestFile));
            }
            paint = MachinePaint.FromKept(new MachinePaint.KeptPaint(files, Ints(r), Ints(r), Ints(r)))
                ?? throw new InvalidDataException("paint");
        }
        var anchorCount = r.ReadInt32();
        IReadOnlyList<Vector3>? anchors = anchorCount is -1 ? null
            : anchorCount is >= 0 and <= 3 ? Array.ConvertAll(new int[anchorCount], _ => Vector3Of(r))
            : throw new InvalidDataException("anchors");
        return new CachedMachine(new MachineModel(meshes, textures, own), paint, anchors);
    }

    // (little-endian numbers as the machine holds them: the hosts the launcher runs on, x86-64 and ARM64, are all
    // little-endian, which the file's header words already assume)
    private static T[] Bulk<T>(BinaryReader r, int count) where T : unmanaged
    {
        if ((long)count * System.Runtime.CompilerServices.Unsafe.SizeOf<T>() > r.BaseStream.Length - r.BaseStream.Position)
        {
            throw new InvalidDataException("count");
        }
        var values = new T[count];
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan());
        if (r.Read(bytes) != bytes.Length)
        {
            throw new EndOfStreamException();
        }
        return values;
    }

    private static void Bulk<T>(BinaryWriter w, T[] values) where T : unmanaged =>
        w.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));

    private static int Count(BinaryReader r, int most) =>
        r.ReadInt32() is var n && n >= 0 && n <= most && n <= r.BaseStream.Length - r.BaseStream.Position ? n : throw new InvalidDataException("count");

    private static byte[] Exactly(BinaryReader r, int length) =>
        r.ReadBytes(length) is { } bytes && bytes.Length == length ? bytes : throw new EndOfStreamException();

    private static int[] Ints(BinaryReader r) => Array.ConvertAll(new int[Count(r, LargestFile)], _ => r.ReadInt32());

    private static void Ints(BinaryWriter w, int[] values)
    {
        w.Write(values.Length);
        foreach (var v in values)
        {
            w.Write(v);
        }
    }

    private static TextureWrap Wrap(byte b) => b <= (byte)TextureWrap.Mirror ? (TextureWrap)b : throw new InvalidDataException("wrap");

    private static Vector3 Vector3Of(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static void Write(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }
}
