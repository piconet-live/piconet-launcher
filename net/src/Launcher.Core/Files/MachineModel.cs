using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Fzgx.GameCube.Models;
using Fzgx.GameCube.Textures;

namespace Launcher.Core;

/// <summary>What a texture shows outside its picture (coordinates under 0 or over 1): its edge, itself again, or
/// itself again turned over each time.</summary>
public enum TextureWrap
{
    Clamp,
    Repeat,
    Mirror,
}

/// <summary>How a mesh is drawn: the colours of its material as the model gives them (diffuse, ambient, specular, the
/// highlight's sharpness, its opacity), its texture (an index into <see cref="MachineModel.Textures"/>, -1 none) and
/// how that texture goes on across and down, and whether it is see-through (drawn after the rest) or seen from both
/// sides. <see cref="Painted"/>: a custom part's mesh that takes the part's colour (its header's byte +0x15 is 1:
/// the game sets its diffuse and ambient to the colour); <see cref="Part"/>: which
/// part of an assembled model it is (0 body, 1 cockpit, 2 booster; -1 a stock machine's).</summary>
public sealed record MachineMaterial(
    Vector3 Diffuse, Vector3 Ambient, Vector3 Specular, float Shininess, float Alpha, int Texture, bool Translucent,
    bool TwoSided, TextureWrap WrapS = TextureWrap.Repeat, TextureWrap WrapT = TextureWrap.Repeat, bool Painted = false,
    int Part = -1);

/// <summary>Triangles sharing one material: positions, unit normals, texture coordinates and vertex colours (RGBA,
/// 0-1), three indices a triangle.</summary>
public sealed record MachineMesh(
    Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, Vector4[] Colors, int[] Indices, MachineMaterial Material);

/// <summary>A texture of the model: decoded, and whether only its alpha counts (the shared textures' shapes for glows
/// and cut-outs are black with an alpha; their colour comes from the material).</summary>
public sealed record MachineTexture(Picture Picture, bool AlphaOnly = false);

/// <summary>
/// A machine's model as the game draws the player's own machine (<c>vehicle/NAME_p.arc</c>: <c>NAME.gma</c> and its
/// textures <c>NAME.tpl</c>, the main stock set's colour 0), for the garage's preview. Its first textures are the
/// machine's own (<see cref="OwnTextures"/>, the ones the paint changes); the rest are the shared machine textures
/// (<c>vehicle/carcmntex.tpl</c>) the meshes use. Objects after the first that move on their own (Fire Stingray's fin,
/// Rainbow Phoenix's wings) are stored around their hinges and go where the attachment file <c>NAME.fmi</c> says
/// (<see cref="Attachments"/>). The steering grips and the empty pilot-position marker are left out: the pilot who
/// holds the grips is not drawn here.
/// <para>A material says which of the two files its texture number is from (<see cref="CommonTexture"/> in its first
/// field). The shared numbers are the game's own list of 76, not the shared file's order of 57
/// (<see cref="SharedSlot"/>).</para>
/// </summary>
public sealed class MachineModel
{
    /// <summary>In a material's first field: its texture number is one of the shared list, not of the machine's own
    /// file.</summary>
    public const int CommonTexture = 0x0020;

    /// <summary>In a material's first field: its texture is put on by the surface's facing, as a reflection, not by
    /// the mesh's coordinates. The studio's own sky stands in for those.</summary>
    public const int ReflectedTexture = 0x0010;

    // the shared file's slots of the number plates' digits 0 to 9, light and dark
    private static readonly int[] LightDigits = [1, 3, 4, 6, 8, 9, 10, 11, 12, 13];
    private static readonly int[] DarkDigits = [2, 47, 5, 7, 42, 43, 44, 45, 46, 14];

    /// <summary>
    /// The shared file's slot of a shared texture number, or -1 for a number the game's table does not list (the
    /// game would take such a number as the slot itself; none of the machines' or parts' models uses one, and the
    /// numbers 21 to 30 are textures the game makes as it runs). The table is the game's own, main.rel .rodata 0x3968
    /// (57 pairs; the stock machines and the custom parts use it alike): 1 to 20 are the number
    /// plates' digits in pairs (2d+1 the light digit d, 2d+2 the dark one); 31 to 42 the twelve highlight shapes; 51
    /// the reflected city; 61 to 75 the fifteen grilles, vents and panels; 76 to 84 nine more pictures. The file keeps
    /// each group's order but not the groups'.
    /// </summary>
    public static int SharedSlot(int number) => number switch
    {
        >= 1 and <= 20 => (number % 2 == 1 ? LightDigits : DarkDigits)[(number - 1) / 2],
        >= 31 and <= 42 => number - 1,
        51 => 0,
        >= 61 and <= 75 => number - 46,
        >= 76 and <= 79 => number - 24,
        80 => 56,
        >= 81 and <= 84 => number - 33,
        _ => -1,
    };

    /// <summary>A model of these meshes and textures, the first <paramref name="own"/> of them the machine's own.</summary>
    public MachineModel(IReadOnlyList<MachineMesh> meshes, IReadOnlyList<MachineTexture?> textures, int own)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(textures);
        Meshes = meshes;
        Textures = textures;
        OwnTextures = own;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in meshes.SelectMany(m => m.Positions))
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Center = meshes.Count > 0 ? (min + max) / 2 : Vector3.Zero;
        Radius = meshes.Count > 0 ? meshes.SelectMany(m => m.Positions).Max(p => Vector3.Distance(p, Center)) : 1;
    }

    public IReadOnlyList<MachineMesh> Meshes { get; }

    /// <summary>The meshes' textures: the machine's own by slot first (null for an empty slot), then the shared ones.</summary>
    public IReadOnlyList<MachineTexture?> Textures { get; }

    /// <summary>How many of <see cref="Textures"/> are the machine's own (its texture file's slots).</summary>
    public int OwnTextures { get; }

    /// <summary>The middle of the model's bounds, and the distance from it to its farthest vertex.</summary>
    public Vector3 Center { get; }

    public float Radius { get; }

    /// <summary>The model of <paramref name="gma"/> with the machine's texture file <paramref name="own"/> and the
    /// shared one <paramref name="shared"/> (when there is one), its objects moved to their places in the attachment
    /// file <paramref name="attachments"/> (when there is one).</summary>
    /// <exception cref="InvalidDataException">The files cannot be read.</exception>
    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",
        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    public static MachineModel Read(byte[] gma, byte[] own, byte[]? shared, byte[]? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(gma);
        ArgumentNullException.ThrowIfNull(own);
        AvTpl? ownTpl, sharedTpl;
        using (LoadTrace.Stage("parse"))
        {
            ownTpl = own.Length == 0 ? null : AvTpl.Parse(own);
        }
        using (LoadTrace.Stage("parse.shared"))
        {
            sharedTpl = shared is null ? null : AvTpl.Parse(shared);
        }
        List<MachineTexture?> textures;
        using (LoadTrace.Stage("decode"))
        {
            textures = [.. (ownTpl?.Textures ?? []).Select(t =>
                ownTpl!.DecodeLevel0(t) is { } rgba ? new MachineTexture(new Picture(t.Width, t.Height, rgba)) : null)];
        }
        var sharedSlots = new Dictionary<int, int>();   // shared file's slot → index in textures

        int TextureOf(int number, bool common)
        {
            if (!common)
            {
                return ownTpl is not null && number >= 0 && number < ownTpl.Textures.Count && textures[number] is not null ? number : -1;
            }
            var slot = SharedSlot(number);
            if (sharedTpl is null || slot < 0 || slot >= sharedTpl.Textures.Count)
            {
                return -1;
            }
            if (!sharedSlots.TryGetValue(slot, out var index))
            {
                var t = sharedTpl.Textures[slot];
                index = -1;
                // the shared intensity textures are highlights the game maps by the surface's facing (its own lights
                // stand in for them here); the rest are pictures or alpha shapes
                using var decode = LoadTrace.Stage("decode.shared");
                if (t.Format is not (GxTextureFormat.I4 or GxTextureFormat.I8) && sharedTpl.DecodeLevel0(t) is { } rgba)
                {
                    var black = true;
                    for (var i = 0; i < rgba.Length && black; i += 4)
                    {
                        black = rgba[i] == 0 && rgba[i + 1] == 0 && rgba[i + 2] == 0;
                    }
                    index = textures.Count;
                    textures.Add(new MachineTexture(new Picture(t.Width, t.Height, rgba), black));
                }
                sharedSlots[slot] = index;
            }
            return index;
        }

        var parse = LoadTrace.Stage("parse.gma");   // (decode.shared happens inside it)
        var models = GmaReader.Read(gma);
        var looks = MeshLooks(gma);
        var layers = TextureLayers(gma);
        var places = Attachments(attachments);
        var meshes = new List<MachineMesh>();
        for (var m = 0; m < models.Count; m++)
        {
            if (models[m] is not { UnsupportedReason: null } model || model.Name.StartsWith("steer", StringComparison.Ordinal)
                || model.Name.StartsWith("pos_", StringComparison.Ordinal))
            {
                continue;
            }
            var place = places.GetValueOrDefault(model.Name);
            var modelLooks = looks.Count > m && looks[m].Count == model.Meshes.Length ? looks[m] : null;
            for (var k = 0; k < model.Meshes.Length; k++)
            {
                var mesh = model.Meshes[k];
                var look = modelLooks?[k] ?? MeshLook.Plain;
                // the mesh's first layer; its further ones (a reflection, a highlight, a second picture) are left to
                // the studio's lights
                var layer = layers.Count > m && mesh.MaterialIndex >= 0 && mesh.MaterialIndex < layers[m].Count
                    ? layers[m][mesh.MaterialIndex] : null;
                var reflected = layer is { Reflected: true };
                var texture = layer is null || reflected ? -1 : TextureOf(layer.Number, layer.Common);
                if (mesh.IsTranslucent && layer is not null && !reflected && texture < 0)
                {
                    continue;   // a glow drawn from a texture the game makes as it runs
                }
                var material = new MachineMaterial(look.Diffuse, look.Ambient, look.Specular, look.Shininess,
                    look.Alpha, texture, mesh.IsTranslucent || look.Alpha < 0.99f, mesh.IsTwoSided,
                    layer?.WrapS ?? TextureWrap.Repeat, layer?.WrapT ?? TextureWrap.Repeat, look.Painted);
                meshes.Add(new MachineMesh(
                    [.. mesh.Vertices.Select(v => v.Position + place)],
                    [.. mesh.Vertices.Select(v => v.Normal == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(v.Normal))],
                    [.. mesh.Vertices.Select(v => v.Uv)],
                    [.. mesh.Vertices.Select(v => Rgba(v.Color))],
                    [.. mesh.Indices],
                    material));
            }
        }
        parse.Dispose();
        return new MachineModel(meshes, textures, ownTpl?.Textures.Count ?? 0);
    }

    /// <summary>
    /// Where an attachment file (<c>.fmi</c>) puts a model's separate objects, by name: a count at byte 2, that many
    /// records of 0x14 bytes from 0x200 (a word, the name's place in the names from 0x2A0, the point as three floats,
    /// high byte first). The game draws each object at its point (main.rel <c>fn_1_87610</c>, and
    /// <c>fn_1_87750</c>.. by machine, the table at .data 0x1F5A0); only a machine in motion turns them there (the fin
    /// by the steering, the wings when boosting), so at rest the point is all there is. A record that does not fit
    /// the file, or whose point is no number, places nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, Vector3> Attachments(byte[]? fmi)
    {
        const int Records = 0x200, Size = 0x14, Names = 0x2A0;
        var places = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        if (fmi is null || fmi.Length < Names)
        {
            return places;
        }
        for (var i = 0; i < fmi[2] && Records + ((i + 1) * Size) <= Names; i++)
        {
            var at = Records + (i * Size);
            var name = (long)Names + BinaryPrimitives.ReadUInt32BigEndian(fmi.AsSpan(at + 4));
            var end = name < fmi.Length ? Array.IndexOf(fmi, (byte)0, (int)name) : -1;
            var point = new Vector3(BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at + 8)),
                BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at + 12)), BinaryPrimitives.ReadSingleBigEndian(fmi.AsSpan(at + 16)));
            if (end >= 0 && float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z))
            {
                places.TryAdd(System.Text.Encoding.ASCII.GetString(fmi, (int)name, end - (int)name), point);
            }
        }
        return places;
    }

    /// <summary>One model of several, each moved by its offset (a custom machine: the body as it is, the cockpit and
    /// the booster at the body's attachment points). The textures of each are kept, numbered on, and each mesh's
    /// material names its part (its place in <paramref name="parts"/>).</summary>
    public static MachineModel Assemble(IReadOnlyList<(MachineModel Model, Vector3 Offset)> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var meshes = new List<MachineMesh>();
        var textures = new List<MachineTexture?>();
        for (var part = 0; part < parts.Count; part++)
        {
            var (model, offset) = parts[part];
            var first = textures.Count;
            textures.AddRange(model.Textures);
            meshes.AddRange(model.Meshes.Select(m => m with
            {
                Positions = [.. m.Positions.Select(p => p + offset)],
                Material = m.Material with { Texture = m.Material.Texture < 0 ? -1 : m.Material.Texture + first, Part = part },
            }));
        }
        return new MachineModel(meshes, textures, 0);
    }

    /// <summary>A mesh's colours from its header, which the model reader leaves out: diffuse at +4, ambient at +8,
    /// specular at +0xC (RGBA each), the highlight's sharpness at +0x10, the opacity at +0x11, and at +0x15 the
    /// colour slot a custom part's paint goes on (1; 0 none).</summary>
    private sealed record MeshLook(Vector3 Diffuse, Vector3 Ambient, Vector3 Specular, float Shininess, float Alpha, bool Painted = false)
    {
        public static MeshLook Plain { get; } = new(new Vector3(0.7f), new Vector3(0.5f), new Vector3(0.3f), 25, 1);
    }

    /// <summary>Every model's meshes' looks, in the model file's order: walks the GCMF blocks as the model reader does
    /// (a 0x40-byte header, 0x20-byte materials, the meshes from the header size on: a 0x60-byte header, two strip
    /// groups, and two more after a 0x20-byte header when the mesh's group flags are 0x0F).</summary>
    private static List<List<MeshLook>> MeshLooks(byte[] gma)
    {
        var all = new List<List<MeshLook>>();
        var count = BinaryPrimitives.ReadInt32BigEndian(gma);
        var modelBase = BinaryPrimitives.ReadInt32BigEndian(gma.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var looks = new List<MeshLook>();
            all.Add(looks);
            var offset = BinaryPrimitives.ReadInt32BigEndian(gma.AsSpan(8 + (i * 8)));
            var start = modelBase + offset;
            if (offset == -1 || start < 0 || start + 0x40 > gma.Length)
            {
                continue;
            }
            int Int(int at) => BinaryPrimitives.ReadInt32BigEndian(gma.AsSpan(at));
            var meshes = BinaryPrimitives.ReadUInt16BigEndian(gma.AsSpan(start + 0x1A)) + BinaryPrimitives.ReadUInt16BigEndian(gma.AsSpan(start + 0x1C));
            var at = start + Int(start + 0x20);
            for (var k = 0; k < meshes && at + 0x60 <= gma.Length; k++)
            {
                Vector3 Color(int o) => new(gma[at + o] / 255f, gma[at + o + 1] / 255f, gma[at + o + 2] / 255f);
                looks.Add(new MeshLook(Color(4), Color(8), Color(0xC), Math.Max(1, (int)gma[at + 0x10]), gma[at + 0x11] / 255f,
                    gma[at + 0x15] == 1));
                var next = at + 0x60 + Int(at + 0x28) + Int(at + 0x2C);
                if ((gma[at + 0x13] & 0x0F) == 0x0F && next + 0x20 <= gma.Length)
                {
                    next += 0x20 + Int(next + 8) + Int(next + 12);
                }
                at = next;
            }
        }
        return all;
    }

    /// <summary>A texture layer of a model (its "material" in the file): the texture's number, which file it is from,
    /// whether it is a reflection, and how it goes on.</summary>
    private sealed record TextureLayer(int Number, bool Common, bool Reflected, TextureWrap WrapS, TextureWrap WrapT);

    /// <summary>Every model's texture layers, in the model file's order (0x20 bytes each from +0x40 of the model): the
    /// kind in the first two bytes, at +3 how it goes on (4 repeats across, 8 mirrors across, 0x10 and 0x20 the same
    /// down; neither: its edge goes on), and its number at +4.</summary>
    private static List<List<TextureLayer>> TextureLayers(byte[] gma)
    {
        var all = new List<List<TextureLayer>>();
        var count = BinaryPrimitives.ReadInt32BigEndian(gma);
        var modelBase = BinaryPrimitives.ReadInt32BigEndian(gma.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var layers = new List<TextureLayer>();
            all.Add(layers);
            var offset = BinaryPrimitives.ReadInt32BigEndian(gma.AsSpan(8 + (i * 8)));
            var start = modelBase + offset;
            if (offset == -1 || start < 0 || start + 0x40 > gma.Length)
            {
                continue;
            }
            var materials = BinaryPrimitives.ReadUInt16BigEndian(gma.AsSpan(start + 0x18));
            for (var k = 0; k < materials && start + 0x40 + ((k + 1) * 0x20) <= gma.Length; k++)
            {
                var at = start + 0x40 + (k * 0x20);
                var kind = BinaryPrimitives.ReadUInt16BigEndian(gma.AsSpan(at));
                var wrap = gma[at + 3];
                layers.Add(new TextureLayer(BinaryPrimitives.ReadUInt16BigEndian(gma.AsSpan(at + 4)),
                    (kind & CommonTexture) != 0, (kind & ReflectedTexture) != 0, Wrap(wrap >> 2), Wrap(wrap >> 4)));
            }
        }
        return all;
    }

    private static TextureWrap Wrap(int bits) =>
        (bits & 2) != 0 ? TextureWrap.Mirror : (bits & 1) != 0 ? TextureWrap.Repeat : TextureWrap.Clamp;

    private static Vector4 Rgba(uint c) => new((c >> 24) / 255f, ((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);
}
