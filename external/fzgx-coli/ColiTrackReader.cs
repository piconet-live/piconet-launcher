using System.Collections.Immutable;
using System.Numerics;
using Fzgx.GameCube.Io;

namespace Fzgx.Coli;

/// <summary>
/// Reads the track of an uncompressed F-Zero GX course file: the track
/// nodes with their checkpoints, and the segment trees they refer to.
/// </summary>
/// <remarks>
/// <para>Layout (big-endian; pointers are absolute offsets, 0 is null), as
/// described by the Manifold project (reference only, ADR-0005) and checked
/// on the owner's disc:</para>
/// <list type="bullet">
/// <item>0x08 / 0x0C: count and pointer of 12-byte track nodes: checkpoint count,
/// checkpoint pointer, root segment pointer.</item>
/// <item>Checkpoint, 0x50 bytes: start and end curve time; start and end plane
/// (distance term, normal, origin; 0x1C bytes each); start and end distance;
/// width; connects-in and connects-out bytes; two zero bytes.</item>
/// <item>Segment, 0x50 bytes: kind, property, perimeter and pipe bytes; curve
/// block pointer; corner pointer; child count and pointer (children are
/// consecutive); fallback scale, rotation and position; two 16-bit unknowns;
/// right and left rail heights; two zero words; branch index.</item>
/// <item>Curve block, 0x48 bytes: nine key counts, then nine key pointers, in
/// the order scale, rotation, position (X, Y, Z each). Key, 0x14 bytes: mode,
/// time, value, tangent in, tangent out.</item>
/// </list>
/// </remarks>
public static class ColiTrackReader
{
    private const int NodeSize = 0x0C;
    private const int CheckpointSize = 0x50;
    private const int SegmentSize = 0x50;
    private const int KeySize = 0x14;
    private const int CurveCount = 9;

    /// <summary>Reads the track nodes and every segment tree they use.</summary>
    /// <exception cref="InvalidDataException">Pointers or counts outside the file, or a segment inside itself.</exception>
    public static ColiTrack Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ColiSceneReader.Read(data); // checks the header identifies an F-Zero GX course file
        var reader = new BigEndianReader(data);

        reader.Position = 0x08;
        var nodeCount = reader.ReadInt32();
        var nodeStart = (int)reader.ReadUInt32();
        CheckArray(data, nodeCount, nodeStart, NodeSize, "track nodes");

        var roots = ImmutableArray.CreateBuilder<ColiTrackSegment>();
        var rootIndex = new Dictionary<int, int>();
        var nodes = ImmutableArray.CreateBuilder<ColiTrackNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            reader.Position = nodeStart + (i * NodeSize);
            var checkpointCount = reader.ReadInt32();
            var checkpointStart = (int)reader.ReadUInt32();
            var segment = (int)reader.ReadUInt32();
            CheckArray(data, checkpointCount, checkpointStart, CheckpointSize, $"checkpoints of track node {i}");
            if (!rootIndex.TryGetValue(segment, out var root))
            {
                root = roots.Count;
                rootIndex[segment] = root;
                roots.Add(ReadSegment(reader, segment, []));
            }
            var checkpoints = ImmutableArray.CreateBuilder<ColiCheckpoint>(checkpointCount);
            for (var c = 0; c < checkpointCount; c++)
            {
                checkpoints.Add(ReadCheckpoint(reader, checkpointStart + (c * CheckpointSize)));
            }
            nodes.Add(new ColiTrackNode(root, checkpoints.MoveToImmutable()));
        }
        return new ColiTrack(roots.ToImmutable(), nodes.MoveToImmutable());
    }

    private static ColiCheckpoint ReadCheckpoint(BigEndianReader reader, int start)
    {
        reader.Position = start;
        var timeStart = reader.ReadSingle();
        var timeEnd = reader.ReadSingle();
        var startPlane = ReadPlane(reader);
        var endPlane = ReadPlane(reader);
        var distanceStart = reader.ReadSingle();
        var distanceEnd = reader.ReadSingle();
        var width = reader.ReadSingle();
        var connectsIn = reader.ReadByte() != 0;
        var connectsOut = reader.ReadByte() != 0;
        return new ColiCheckpoint(timeStart, timeEnd, startPlane, endPlane, distanceStart, distanceEnd, width, connectsIn, connectsOut);
    }

    private static ColiPlane ReadPlane(BigEndianReader reader)
    {
        var distance = reader.ReadSingle();
        var normal = ReadVector(reader);
        var origin = ReadVector(reader);
        return new ColiPlane(distance, normal, origin);
    }

    private static ColiTrackSegment ReadSegment(BigEndianReader reader, int start, HashSet<int> ancestors)
    {
        if (start <= 0 || start > reader.Length - SegmentSize)
        {
            throw new InvalidDataException($"Track segment at 0x{start:X} is outside the file.");
        }
        if (!ancestors.Add(start))
        {
            throw new InvalidDataException($"Track segment at 0x{start:X} contains itself.");
        }

        reader.Position = start;
        var kind = (ColiSegmentKind)reader.ReadByte();
        var property = (ColiEmbeddedProperty)reader.ReadByte();
        var perimeter = (ColiPerimeter)reader.ReadByte();
        var pipe = (ColiPipeShape)reader.ReadByte();
        var curveBlock = (int)reader.ReadUInt32();
        var corner = reader.ReadUInt32();
        var childCount = reader.ReadInt32();
        var childStart = (int)reader.ReadUInt32();
        var fallbackScale = ReadVector(reader);
        var fallbackRotation = ReadVector(reader);
        var fallbackPosition = ReadVector(reader);
        var unknown38 = reader.ReadUInt16();
        var unknown3A = reader.ReadUInt16();
        var railRight = reader.ReadSingle();
        var railLeft = reader.ReadSingle();
        reader.Position += 8; // two zero words
        var branchIndex = reader.ReadInt32();

        var curves = curveBlock == 0 ? Enumerable.Repeat(ColiCurve.Empty, CurveCount).ToImmutableArray() : ReadCurves(reader, curveBlock);
        CheckArray(reader.Length, childCount, childStart, SegmentSize, $"children of track segment 0x{start:X}");
        var children = ImmutableArray.CreateBuilder<ColiTrackSegment>(childCount);
        for (var i = 0; i < childCount; i++)
        {
            children.Add(ReadSegment(reader, childStart + (i * SegmentSize), ancestors));
        }
        ancestors.Remove(start);

        return new ColiTrackSegment(
            start, kind, property, perimeter, pipe, curves, fallbackScale, fallbackRotation, fallbackPosition,
            unknown38, unknown3A, railLeft, railRight, branchIndex, corner != 0, children.MoveToImmutable());
    }

    private static ImmutableArray<ColiCurve> ReadCurves(BigEndianReader reader, int start)
    {
        CheckArray(reader.Length, 1, start, CurveCount * 8, "curve block");
        Span<int> counts = stackalloc int[CurveCount];
        Span<int> pointers = stackalloc int[CurveCount];
        reader.Position = start;
        for (var i = 0; i < CurveCount; i++)
        {
            counts[i] = reader.ReadInt32();
        }
        for (var i = 0; i < CurveCount; i++)
        {
            pointers[i] = (int)reader.ReadUInt32();
        }

        var curves = ImmutableArray.CreateBuilder<ColiCurve>(CurveCount);
        for (var i = 0; i < CurveCount; i++)
        {
            if (counts[i] == 0)
            {
                curves.Add(ColiCurve.Empty);
                continue;
            }
            CheckArray(reader.Length, counts[i], pointers[i], KeySize, "curve keys");
            var keys = ImmutableArray.CreateBuilder<ColiKey>(counts[i]);
            reader.Position = pointers[i];
            for (var k = 0; k < counts[i]; k++)
            {
                keys.Add(new ColiKey(reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
            }
            curves.Add(new ColiCurve(keys.MoveToImmutable()));
        }
        return curves.MoveToImmutable();
    }

    private static Vector3 ReadVector(BigEndianReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void CheckArray(byte[] data, int count, int start, int size, string what) =>
        CheckArray(data.Length, count, start, size, what);

    private static void CheckArray(int length, int count, int start, int size, string what)
    {
        if (count < 0 || (count > 0 && (start <= 0 || (long)start + ((long)count * size) > length)))
        {
            throw new InvalidDataException($"The {what} ({count} at 0x{start:X}) are outside the file.");
        }
    }
}
