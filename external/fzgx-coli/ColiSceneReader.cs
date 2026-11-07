using System.Collections.Immutable;
using System.Numerics;
using Fzgx.GameCube.Io;

namespace Fzgx.Coli;

/// <summary>
/// Reads the header and the placed objects of an uncompressed F-Zero GX
/// course file (<c>stage/COLI_COURSENN.lz</c> after LZ decompression).
/// </summary>
/// <remarks>
/// <para>Layout (big-endian; pointers are absolute offsets, 0 is null), as
/// described in fzgx-docs research notes and checked on the owner's disc:</para>
/// <list type="bullet">
/// <item>0x20 and 0x24 hold 0xE8 and 0xFC in F-Zero GX files (0xE4 and 0xF8 in F-Zero AX).</item>
/// <item>0x24: pointer to the minimum track height; 0x7C: circuit (0 closed, 0x10000 open); 0x90: pointer to the track length.</item>
/// <item>0x48: dynamic object count; 0x54: pointer to 0x40-byte dynamic objects.</item>
/// <item>0x6C / 0x70: count and pointer of static objects (4-byte pointers to scene objects).</item>
/// </list>
/// <para>A dynamic object holds, at 0x08, a pointer to a scene object; at 0x0C an
/// inline transform (position, rotation as signed 16-bit fractions of 180
/// degrees applied X then Y then Z, scale); at 0x30 an animation clip pointer;
/// at 0x3C an optional 3x4 matrix (row-major, translation in the fourth
/// column), which takes precedence. A scene object has its LOD count and
/// pointer at 0x04 / 0x08; each 0x10-byte LOD has its name pointer at 0x04.</para>
/// </remarks>
public static class ColiSceneReader
{
    private const int DynamicObjectSize = 0x40;

    /// <summary>Reads the header and placed objects.</summary>
    /// <exception cref="NotSupportedException">An F-Zero AX course file.</exception>
    /// <exception cref="InvalidDataException">Not a course file, or pointers outside the file.</exception>
    public static ColiScene Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new BigEndianReader(data);
        if (data.Length < 0xE8)
        {
            throw new InvalidDataException("Too short for a course file header.");
        }
        var formatA = At(reader, 0x20).ReadUInt32();
        var formatB = reader.ReadUInt32();
        if (formatA == 0xE4 && formatB == 0xF8)
        {
            throw new NotSupportedException("This is an F-Zero AX course file; only F-Zero GX is supported.");
        }
        if (formatA != 0xE8 || formatB != 0xFC)
        {
            throw new InvalidDataException($"Not an F-Zero GX course file (header 0x20/0x24 = 0x{formatA:X}/0x{formatB:X}).");
        }

        var minHeight = Pointed(reader, 0x24).ReadSingle();
        var circuit = At(reader, 0x7C).ReadUInt32() == 0x10000 ? ColiCircuit.Open : ColiCircuit.Closed;
        var trackLength = Pointed(reader, 0x90).ReadSingle();

        var objects = ImmutableArray.CreateBuilder<ColiSceneObject>();
        var dynamicCount = At(reader, 0x48).ReadInt32();
        var dynamicStart = (int)At(reader, 0x54).ReadUInt32();
        CheckArray(data, dynamicCount, dynamicStart, DynamicObjectSize, "dynamic objects");
        for (var i = 0; i < dynamicCount; i++)
        {
            objects.Add(ReadDynamic(reader, dynamicStart + (i * DynamicObjectSize)));
        }

        var staticCount = At(reader, 0x6C).ReadInt32();
        var staticStart = (int)reader.ReadUInt32();
        CheckArray(data, staticCount, staticStart, 4, "static objects");
        for (var i = 0; i < staticCount; i++)
        {
            var sceneObject = (int)At(reader, staticStart + (i * 4)).ReadUInt32();
            objects.Add(new ColiSceneObject(ReadName(reader, sceneObject), Matrix4x4.Identity, IsStatic: true, IsAnimated: false));
        }

        return new ColiScene(trackLength, minHeight, circuit, objects.ToImmutable());
    }

    private static ColiSceneObject ReadDynamic(BigEndianReader reader, int start)
    {
        var sceneObject = (int)At(reader, start + 0x08).ReadUInt32();
        var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var rotation = new Vector3(Angle(reader.ReadInt16()), Angle(reader.ReadInt16()), Angle(reader.ReadInt16()));
        reader.Position += 2; // two option bytes
        var scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var animation = At(reader, start + 0x30).ReadUInt32();
        var matrix = At(reader, start + 0x3C).ReadUInt32();

        var world = matrix != 0 ? ReadMatrix(reader, (int)matrix) : ComposeTrs(position, rotation, scale);
        return new ColiSceneObject(ReadName(reader, sceneObject), world, IsStatic: false, IsAnimated: animation != 0);
    }

    /// <summary>
    /// Builds the transform the game applies: scale, then rotation about X,
    /// Y and Z in that order (R = Rz·Ry·Rx for column vectors), then translation.
    /// </summary>
    public static Matrix4x4 ComposeTrs(Vector3 position, Vector3 rotationRadians, Vector3 scale) =>
        Matrix4x4.CreateScale(scale)
        * Matrix4x4.CreateRotationX(rotationRadians.X)
        * Matrix4x4.CreateRotationY(rotationRadians.Y)
        * Matrix4x4.CreateRotationZ(rotationRadians.Z)
        * Matrix4x4.CreateTranslation(position);

    /// <summary>A stored 3x4 matrix is row-major for column vectors; System.Numerics wants its transpose.</summary>
    private static Matrix4x4 ReadMatrix(BigEndianReader reader, int start)
    {
        reader.Position = start;
        Span<float> m = stackalloc float[12];
        for (var i = 0; i < 12; i++)
        {
            m[i] = reader.ReadSingle();
        }
        return new Matrix4x4(
            m[0], m[4], m[8], 0,
            m[1], m[5], m[9], 0,
            m[2], m[6], m[10], 0,
            m[3], m[7], m[11], 1);
    }

    private static string ReadName(BigEndianReader reader, int sceneObject)
    {
        var lodCount = At(reader, sceneObject + 0x04).ReadInt32();
        var lods = (int)reader.ReadUInt32();
        if (lodCount <= 0 || lods == 0)
        {
            return "";
        }
        var name = (int)At(reader, lods + 0x04).ReadUInt32();
        return name == 0 ? "" : At(reader, name).ReadCString();
    }

    /// <summary>Signed 16-bit angle: 32768 is 180 degrees.</summary>
    private static float Angle(short value) => value / 32768f * MathF.PI;

    private static BigEndianReader At(BigEndianReader reader, int offset)
    {
        reader.Position = offset;
        return reader;
    }

    private static BigEndianReader Pointed(BigEndianReader reader, int pointerOffset)
    {
        var target = At(reader, pointerOffset).ReadUInt32();
        if (target == 0 || target >= reader.Length)
        {
            throw new InvalidDataException($"Header pointer at 0x{pointerOffset:X} (0x{target:X}) is null or outside the file.");
        }
        return At(reader, (int)target);
    }

    private static void CheckArray(byte[] data, int count, int start, int size, string what)
    {
        if (count < 0 || (count > 0 && (start <= 0 || start + ((long)count * size) > data.Length)))
        {
            throw new InvalidDataException($"The {what} ({count} at 0x{start:X}) lie outside the file.");
        }
    }
}
