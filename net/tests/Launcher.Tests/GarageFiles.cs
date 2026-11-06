using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Fzgx.GameCube.Textures;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>Made-up game files for the garage's tests: no disc needed.</summary>
internal static class GarageFiles
{
    public static Rgb Grey { get; } = new(128, 128, 128);

    /// <summary>An attachment file (<c>.fmi</c>): a count, records from 0x200, names from 0x2A0.</summary>
    public static byte[] Fmi(params (string Name, Vector3 Point)[] records)
    {
        var names = new List<byte>();
        var file = new byte[0x2A0];
        file[2] = (byte)records.Length;
        for (var i = 0; i < records.Length; i++)
        {
            var at = 0x200 + (i * 0x14);
            BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(at + 4), names.Count);
            BinaryPrimitives.WriteSingleBigEndian(file.AsSpan(at + 8), records[i].Point.X);
            BinaryPrimitives.WriteSingleBigEndian(file.AsSpan(at + 12), records[i].Point.Y);
            BinaryPrimitives.WriteSingleBigEndian(file.AsSpan(at + 16), records[i].Point.Z);
            names.AddRange(Encoding.ASCII.GetBytes(records[i].Name));
            names.Add(0);
        }
        return [.. file, .. names];
    }

    /// <summary>An 8x8 (or larger) CMPR texture of the colours given per texel.</summary>
    public static byte[] Cmpr(int width, int height, Func<int, int, Rgb> color)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = color(x, y);
                var o = ((y * width) + x) * 4;
                (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (c.R, c.G, c.B, 255);
            }
        }
        return GxTextureEncoder.Encode(GxTextureFormat.CMPR, width, height, rgba);
    }

    /// <summary>A texture file of one 8x8 CMPR texture (count, a 16-byte entry, the data from 32).</summary>
    public static byte[] Tpl(byte[] cmpr)
    {
        var file = new byte[32 + cmpr.Length];
        BinaryPrimitives.WriteInt32BigEndian(file, 1);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(4), (int)GxTextureFormat.CMPR);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(8), 32);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(12), 8);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(18), 0x1234);
        cmpr.CopyTo(file, 32);
        return file;
    }

    /// <summary>A U8 archive of a folder "vehicle" holding the files.</summary>
    public static byte[] Archive(params (string Name, byte[] Data)[] files)
    {
        var names = new List<byte> { 0 };
        int Name(string n)
        {
            var at = names.Count;
            names.AddRange(Encoding.ASCII.GetBytes(n));
            names.Add(0);
            return at;
        }
        var nodes = new List<(int Type, int Name, int Offset, int Size)> { (1, 0, 0, files.Length + 2), (1, Name("vehicle"), 0, files.Length + 2) };
        var fileNames = files.Select(f => Name(f.Name)).ToList();
        var dataStart = 0x20 + ((nodes.Count + files.Length) * 12) + names.Count;
        var data = new List<byte>();
        for (var i = 0; i < files.Length; i++)
        {
            nodes.Add((0, fileNames[i], dataStart + data.Count, files[i].Data.Length));
            data.AddRange(files[i].Data);
        }
        var archive = new byte[dataStart + data.Count];
        BinaryPrimitives.WriteUInt32BigEndian(archive, 0x55AA382D);
        BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(4), 0x20);
        for (var i = 0; i < nodes.Count; i++)
        {
            var o = 0x20 + (i * 12);
            BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(o), ((uint)nodes[i].Type << 24) | (uint)nodes[i].Name);
            BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(o + 4), nodes[i].Offset);
            BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(o + 8), nodes[i].Size);
        }
        names.CopyTo(archive, 0x20 + (nodes.Count * 12));
        data.CopyTo(archive, dataStart);
        return archive;
    }

    /// <summary>A model file of one model with two materials (texture numbers given) and two meshes, one triangle
    /// each (float positions, normals of length 2 as the game's are, texture coordinates), their headers' colours
    /// diffuse (204, 153, 102), ambient grey, sharpness 42.</summary>
    /// <param name="kind1">The second material's first field (<see cref="MachineModel.CommonTexture"/>: its number
    /// is one of the shared list).</param>
    /// <param name="wrap1">How the second material's texture goes on (the byte at +3).</param>
    public static byte[] Gma(int texture0, int texture1, int kind1 = 0, byte wrap1 = 0)
    {
        const int Flags = 0x200 | 0x400 | 0x2000;
        var strip = new List<byte> { 0, 0x98, 0, 3 };
        foreach (var (x, y) in new[] { (0f, 0f), (1f, 0f), (0f, 1f) })
        {
            foreach (var f in new[] { x, y, 0f, 0f, 0f, 2f, x, y })
            {
                var b = new byte[4];
                BinaryPrimitives.WriteSingleBigEndian(b, f);
                strip.AddRange(b);
            }
        }
        strip.Add(0);
        while (strip.Count % 32 != 0)
        {
            strip.Add(0);
        }
        var model = new byte[0x40 + (2 * 0x20) + (2 * (0x60 + strip.Count))];
        BinaryPrimitives.WriteUInt32BigEndian(model, 0x47434D46);
        BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(0x18), 2);   // materials
        BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(0x1A), 2);   // solid meshes
        BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(0x20), 0x80); // header size
        BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(0x44), (ushort)texture0);
        BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(0x64), (ushort)texture1);
        BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(0x60), (ushort)kind1);
        model[0x63] = wrap1;
        for (var m = 0; m < 2; m++)
        {
            var at = 0x80 + (m * (0x60 + strip.Count));
            (model[at + 4], model[at + 5], model[at + 6], model[at + 7]) = (204, 153, 102, 255);
            (model[at + 8], model[at + 9], model[at + 10], model[at + 11]) = (127, 127, 127, 255);
            model[at + 0x10] = 42;
            model[at + 0x11] = 255;
            model[at + 0x13] = 1;
            BinaryPrimitives.WriteUInt16BigEndian(model.AsSpan(at + 0x16), (ushort)m);
            BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(at + 0x1C), Flags);
            BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(at + 0x28), strip.Count);
            strip.CopyTo(model, at + 0x60);
        }
        // the file: one entry (model at 0, name at 0), the name, the model
        var file = new byte[0x20 + model.Length];
        BinaryPrimitives.WriteInt32BigEndian(file, 1);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(4), 0x20);
        Encoding.ASCII.GetBytes("body").CopyTo(file, 16);
        model.CopyTo(file, 0x20);
        return file;
    }

    public static Picture Decode(byte[] tpl)
    {
        var file = AvTpl.Parse(tpl);
        return new Picture(8, 8, file.DecodeLevel0(file.Textures[0])!);
    }

    public static Rgb Pixel(Picture p, int x, int y)
    {
        var o = ((y * p.Width) + x) * 4;
        return new Rgb(p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }
}
