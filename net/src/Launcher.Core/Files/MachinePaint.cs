using System.Buffers.Binary;
using Fzgx.GameCube.Textures;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// A machine's paint, from its four stock texture files: the game swaps whole pre-painted
/// files for the four stock colours, so the paint is wherever the four differ (CMPR blocks, RGB5A3 texels); the
/// cockpit, engines and decals are alike in all four. Any other colour is painted the way the game paints it in a race
/// (the core's <c>Recolor</c>, port/core/src/paint.cpp; the Dolphin fork's FzgxPaint.cpp before it), so the garage shows what the race will:
/// each paint block's colours take the new colour, shaded darker or lighter by their brightness against the paint's
/// mean. The set is the main one (<c>NAME.tpl</c>, <c>NAME_cv1..3.tpl</c>), the player's own machine as drawn in a
/// race; the low-detail opponent set (<c>NAME_svN</c>) is painted the same way.
/// </summary>
public sealed class MachinePaint
{
    private const int Cmpr = 14;
    private const int Rgb5A3 = 5;

    private readonly byte[][] _stock;
    // the paint: CMPR blocks (8 bytes) and RGB5A3 texels (2 bytes) by offset in the files
    private readonly int[] _blocks;
    private readonly int[] _texels;
    private readonly double _mean;

    private MachinePaint(byte[][] stock, int[] blocks, int[] texels, IReadOnlyList<int> painted)
    {
        _stock = stock;
        _blocks = blocks;
        _texels = texels;
        PaintedTextures = painted;
        // the paint's mean brightness in the first file, from its CMPR blocks' two colours (as the fork's)
        var sum = 0.0;
        foreach (var o in blocks)
        {
            sum += Lum(From565(Be16(stock[0], o))) + Lum(From565(Be16(stock[0], o + 2)));
        }
        _mean = blocks.Length > 0 ? sum / (blocks.Length * 2) : 128;
    }

    /// <summary>The texture slots (indices in the file) that hold paint; the others are alike in every colour.</summary>
    public IReadOnlyList<int> PaintedTextures { get; }

    /// <summary>How much of the files is paint: CMPR blocks plus RGB5A3 texels.</summary>
    public int PaintUnits => _blocks.Length + _texels.Length;

    /// <summary>The paint of a set of four stock files (stock colour 0 first), or null when they are not four files of
    /// one size.</summary>
    public static MachinePaint? Of(IReadOnlyList<byte[]> stock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        if (stock.Count != 4 || stock.Any(f => f is null || f.Length != stock[0].Length))
        {
            return null;
        }
        var files = stock.ToArray();
        var blocks = new List<int>();
        var texels = new List<int>();
        var painted = new SortedSet<int>();
        foreach (var (index, format, start, end) in Slots(files[0]))
        {
            var unit = format switch { Cmpr => 8, Rgb5A3 => 2, _ => 0 };
            if (unit == 0)
            {
                continue;
            }
            for (var o = start; o + unit <= end; o += unit)
            {
                if (files.Skip(1).Any(f => !f.AsSpan(o, unit).SequenceEqual(files[0].AsSpan(o, unit))))
                {
                    (unit == 8 ? blocks : texels).Add(o);
                    painted.Add(index);
                }
            }
        }
        return new MachinePaint(files, [.. blocks], [.. texels], [.. painted]);
    }

    /// <summary>What <see cref="MachineCache"/> keeps of a paint: the four files and where their paint is.</summary>
    public sealed record KeptPaint(byte[][] Files, int[] Blocks, int[] Texels, int[] Painted);

    /// <summary>This paint as the cache keeps it.</summary>
    public KeptPaint Kept => new(_stock, _blocks, _texels, [.. PaintedTextures]);

    /// <summary>A paint the cache kept; null when it is not sound (four files of one size, the paint inside them, the
    /// painted textures among the first file's).</summary>
    public static MachinePaint? FromKept(KeptPaint kept)
    {
        ArgumentNullException.ThrowIfNull(kept);
        var files = kept.Files;
        if (files.Length != 4 || files.Any(f => f is null || f.Length != files[0].Length)
            || kept.Blocks.Any(o => o < 0 || o > files[0].Length - 8) || kept.Texels.Any(o => o < 0 || o > files[0].Length - 2))
        {
            return null;
        }
        try
        {
            var count = AvTpl.Parse(files[0]).Textures.Count;
            return kept.Painted.All(i => i >= 0 && i < count) ? new MachinePaint(files, kept.Blocks, kept.Texels, kept.Painted) : null;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Stock colour <paramref name="k"/> (0-3): the game's own file for it.</summary>
    public byte[] Stock(int k) => _stock[Math.Clamp(k, 0, 3)];

    /// <summary>The first stock file with its paint in <paramref name="color"/>, shading kept (the fork's
    /// <c>Recolor</c>).</summary>
    public byte[] Painted(Rgb color)
    {
        var stock = _stock[0];
        var output = (byte[])stock.Clone();
        var target = (color.R, color.G, color.B);
        foreach (var o in _blocks)
        {
            // CMPR: two RGB565 colours and 2-bit indices; the colours' order picks the block's mode, kept
            var c0 = Be16(stock, o);
            var c1 = Be16(stock, o + 2);
            var idx = BinaryPrimitives.ReadUInt32BigEndian(stock.AsSpan(o + 4));
            var n0 = To565(Shade(From565(c0), target));
            var n1 = To565(Shade(From565(c1), target));
            if (c0 > c1)
            {
                // four opaque colours: needs n0 > n1
                if (n0 < n1)
                {
                    (n0, n1) = (n1, n0);
                    idx ^= 0x55555555;
                }
                else if (n0 == n1)
                {
                    idx = 0;
                }
            }
            else if (n0 > n1)
            {
                // three colours and transparent: needs n0 <= n1
                (n0, n1) = (n1, n0);
                uint remapped = 0;
                for (var b = 0; b < 32; b += 2)
                {
                    var v = (idx >> b) & 3;
                    remapped |= (v < 2 ? v ^ 1 : v) << b;
                }
                idx = remapped;
            }
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(o), n0);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(o + 2), n1);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(o + 4), idx);
        }
        foreach (var o in _texels)
        {
            var v = Be16(stock, o);
            if ((v & 0x8000) != 0)
            {
                // opaque RGB555
                var c = Shade((((v >> 10) & 31) * 255.0 / 31, ((v >> 5) & 31) * 255.0 / 31, (v & 31) * 255.0 / 31), target);
                v = (ushort)(0x8000 | (Round(c.R * 31 / 255) << 10) | (Round(c.G * 31 / 255) << 5) | Round(c.B * 31 / 255));
            }
            else
            {
                // ARGB3444: alpha kept
                var a = (v >> 12) & 7;
                var c = Shade((((v >> 8) & 15) * 17.0, ((v >> 4) & 15) * 17.0, (v & 15) * 17.0), target);
                v = (ushort)((a << 12) | (Round(c.R / 17) << 8) | (Round(c.G / 17) << 4) | Round(c.B / 17));
            }
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(o), v);
        }
        return output;
    }

    /// <summary>The painted textures of a file of this set (<see cref="Stock"/> or <see cref="Painted"/>) decoded, by
    /// slot: what changes on screen when the colour does.</summary>
    public IReadOnlyList<(int Texture, Picture Picture)> PaintedPictures(byte[] file)
    {
        using var timed = LoadTrace.Stage("paint.decode");
        var tpl = AvTpl.Parse(file);
        return [.. PaintedTextures.Select(i =>
        {
            var t = tpl.Textures[i];
            return (i, new Picture(t.Width, t.Height, tpl.DecodeLevel0(t) ?? []));
        })];
    }

    /// <summary>The four stock colours as swatches (0 first), from the painted textures of each file decoded: each
    /// file's paint is the texels that differ between the four, counted over every texture of the set; its swatch is
    /// the colour most of that paint has (paint can be two-tone, a body colour and stripes), lit as the paint shows in
    /// light. Null when the set has no paint.</summary>
    public IReadOnlyList<Rgb>? Swatches()
    {
        return StockSwatches([.. _stock.Select(f => (IReadOnlyList<Picture>)[.. PaintedPictures(f).Select(p => p.Picture)])]);
    }

    /// <summary>Swatches from decoded pictures: <paramref name="sets"/>[k] is stock colour k's textures, alike in
    /// number and size across the four (see <see cref="Swatches()"/>). Null without paint.</summary>
    public static IReadOnlyList<Rgb>? StockSwatches(IReadOnlyList<IReadOnlyList<Picture>> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        if (sets.Count != 4 || sets.Any(s => s.Count != sets[0].Count))
        {
            return null;
        }
        var paint = Enumerable.Range(0, 4).Select(_ => new List<(int R, int G, int B)>()).ToList();
        for (var t = 0; t < sets[0].Count; t++)
        {
            var px = sets.Select(s => s[t].Rgba).ToList();
            if (px.Any(p => p.Length != px[0].Length))
            {
                return null;
            }
            for (var i = 0; i + 3 < px[0].Length; i += 4)
            {
                // see-through texels show nothing; paint is where any of the four differs
                if (px[0][i + 3] < 128 || px.Skip(1).All(p => p[i] == px[0][i] && p[i + 1] == px[0][i + 1] && p[i + 2] == px[0][i + 2]))
                {
                    continue;
                }
                for (var k = 0; k < 4; k++)
                {
                    paint[k].Add((px[k][i], px[k][i + 1], px[k][i + 2]));
                }
            }
        }
        return paint[0].Count == 0 ? null : [.. paint.Select(Dominant)];
    }

    /// <summary>The colour most of the texels have: the texels fall in bins by hue (12) and saturation and
    /// brightness (three steps each, greys by brightness alone); the fullest bin, with its neighbours' share, wins
    /// and its texels' average is the colour, lit as the paint shows in light (its strongest channel near 220).</summary>
    internal static Rgb Dominant(IReadOnlyList<(int R, int G, int B)> texels)
    {
        static int Bin(int r, int g, int b)
        {
            var (h, s, v) = ColorWheel.ToHsv(new Rgb((byte)r, (byte)g, (byte)b));
            var value = Math.Min(2, (int)(v * 3));
            return s < 0.2 ? 200 + value : (int)(h / 30) % 12 * 9 + (Math.Min(2, (int)((s - 0.2) / 0.8 * 3)) * 3) + value;
        }
        var bins = new Dictionary<int, (int Count, double R, double G, double B)>();
        foreach (var (r, g, b) in texels)
        {
            var key = Bin(r, g, b);
            var (n, sr, sg, sb) = bins.GetValueOrDefault(key);
            bins[key] = (n + 1, sr + r, sg + g, sb + b);
        }
        // a bin's weight with half of its neighbours' (the same hue a step brighter or duller), so one colour split
        // over a bin edge still counts as one
        double Weight(int key)
        {
            var w = (double)bins[key].Count;
            if (key < 200)
            {
                foreach (var d in (int[])[-1, 1, -3, 3])
                {
                    var n = key + d;
                    if (n / 9 == key / 9 && bins.TryGetValue(n, out var near))
                    {
                        w += near.Count / 2.0;
                    }
                }
            }
            return w;
        }
        var best = bins.Keys.MaxBy(Weight);
        var (count, tr, tg, tb) = bins[best];
        var (red, green, blue) = (tr / count, tg / count, tb / count);
        var gain = Math.Clamp(220 / Math.Max(1, Math.Max(red, Math.Max(green, blue))), 1, 3);
        return new Rgb((byte)Math.Min(255, red * gain), (byte)Math.Min(255, green * gain), (byte)Math.Min(255, blue * gain));
    }

    /// <summary>The texture slots of a file (format, byte range with every mip level): a count, then 16-byte entries
    /// (format, offset, width, height, levels, check); slots without levels are empty. Each slot runs to the next.</summary>
    private static List<(int Index, int Format, int Start, int End)> Slots(byte[] data)
    {
        var entries = new List<(int Offset, int Format, int Index)>();
        var n = data.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(data) : 0;
        for (var i = 0; i < n && 4 + (i * 16) + 16 <= data.Length; i++)
        {
            var e = data.AsSpan(4 + (i * 16));
            if (BinaryPrimitives.ReadUInt16BigEndian(e[12..]) != 0)
            {
                entries.Add((BinaryPrimitives.ReadInt32BigEndian(e[4..]), BinaryPrimitives.ReadInt32BigEndian(e), i));
            }
        }
        entries.Sort();
        var slots = new List<(int, int, int, int)>();
        for (var k = 0; k < entries.Count; k++)
        {
            var end = k + 1 < entries.Count ? entries[k + 1].Offset : data.Length;
            if (entries[k].Offset >= 0 && entries[k].Offset < end && end <= data.Length)
            {
                slots.Add((entries[k].Index, entries[k].Format, entries[k].Offset, end));
            }
        }
        return slots;
    }

    /// <summary>The paint's shade of the new colour: brightness against the paint's mean, 0.5 the colour itself,
    /// darker towards black and lighter towards white.</summary>
    private (double R, double G, double B) Shade((double R, double G, double B) c, (byte R, byte G, byte B) target)
    {
        var t = Math.Clamp(0.5 * Lum(c) / Math.Max(_mean, 1.0), 0.0, 1.0);
        double One(byte x) => t < 0.5 ? x * 2 * t : x + ((255 - x) * ((2 * t) - 1));
        return (One(target.R), One(target.G), One(target.B));
    }

    private static double Lum((double R, double G, double B) c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);

    private static ushort Be16(byte[] data, int o) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o));

    private static (double R, double G, double B) From565(ushort c) =>
        (((c >> 11) & 31) * 255.0 / 31, ((c >> 5) & 63) * 255.0 / 63, (c & 31) * 255.0 / 31);

    private static ushort To565((double R, double G, double B) c) =>
        (ushort)((Round(c.R * 31 / 255) << 11) | (Round(c.G * 63 / 255) << 5) | Round(c.B * 31 / 255));

    // C's lround: halves away from zero
    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
