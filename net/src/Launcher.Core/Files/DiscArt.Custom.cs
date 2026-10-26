using System.Buffers.Binary;
using System.Numerics;

namespace Launcher.Core;

/// <summary>A custom machine's model for the builder's preview, and where each part stands in it (the middle of its
/// bounds: body, cockpit, booster), for the labels on the model.</summary>
public sealed record CustomModel(MachineModel Model, IReadOnlyList<Vector3> Anchors);

// A custom machine's three part models, read from the disc as a stock machine's are (the bounded reader, nothing
// raised on its bytes). How the game puts them together (read in fn_1_14E2DC, the parts' draw): the body
// as it is; the cockpit and the booster moved by two points of the body's attachment file, no turn and no scale.
public sealed partial class DiscArt
{
    /// <summary>In a body's attachment file (<c>.fmi</c>): its booster's point (three floats, high byte first) and
    /// its cockpit's: records of 0x14 bytes from 0x200, each a word, a name's place and the point.</summary>
    private const int BoosterPoint = 0x208, CockpitPoint = 0x21C;

    private static readonly string[] PartCodes = ["bdy", "cpt", "bst"];

    /// <summary>The machine of these three parts (ids of their kinds, unchecked otherwise: none of another kind is
    /// read) as the game draws it at full detail, with the shared machine textures; null when a file is missing or
    /// cannot be read.</summary>
    public CustomModel? CustomModel(int body, int cockpit, int booster)
    {
        if (CustomPartTables.KindOf(body) != PartKind.Body || CustomPartTables.KindOf(cockpit) != PartKind.Cockpit
            || CustomPartTables.KindOf(booster) != PartKind.Booster)
        {
            return null;
        }
        try
        {
            var shared = Read("vehicle/carcmntex.tpl");
            var fmi = Read(PartFile(body, "fmi"));
            if (fmi is null || fmi.Length < CockpitPoint + 12 || Part(body) is not { } b || Part(cockpit) is not { } c
                || Part(booster) is not { } s)
            {
                return null;
            }
            Vector3 Point(int at) => new(BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at)),
                BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at + 4)), BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at + 8)));
            var (toCockpit, toBooster) = (Point(CockpitPoint), Point(BoosterPoint));
            if (!Finite(toCockpit) || !Finite(toBooster))
            {
                return null;
            }
            var model = Core.MachineModel.Assemble([(b, Vector3.Zero), (c, toCockpit), (s, toBooster)]);
            var anchors = new[] { Middle(b, Vector3.Zero), Middle(c, toCockpit), Middle(s, toBooster) };
            return model.Meshes.Count == 0 ? null : new CustomModel(model, anchors);

            MachineModel? Part(int id) => Read(PartFile(id, "gma")) is { } gma ? Core.MachineModel.Read(gma, [], shared) : null;
        }
        catch (Exception e) when (Unreadable(e))
        {
            return null;
        }
    }

    /// <summary>A part's file: <c>vehicle_parts/parts_bdy01.gma</c> for body 0.</summary>
    private static string PartFile(int part, string extension)
    {
        var kind = (int)CustomPartTables.KindOf(part)!;
        return $"vehicle_parts/parts_{PartCodes[kind]}{(part % CustomPartTables.PerKind) + 1:00}.{extension}";
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.Length() < 100;

    private static Vector3 Middle(MachineModel model, Vector3 offset)
    {
        var points = model.Meshes.SelectMany(m => m.Positions).ToList();
        if (points.Count == 0)
        {
            return offset;
        }
        var (min, max) = (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
        return ((min + max) / 2) + offset;
    }
}
