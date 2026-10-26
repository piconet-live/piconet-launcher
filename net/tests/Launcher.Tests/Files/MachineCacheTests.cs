using System.Numerics;
using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.GarageFiles;

namespace Launcher.Tests;

/// <summary>Where the machines' models are kept once read from the disc (<see cref="MachineCache"/>): the disc's key,
/// the files' names, a machine read back as it was kept, and a damaged or old file read as none.</summary>
public sealed class MachineCacheTests
{
    [Fact]
    public void AMachineIsKeptUnderTheDataFolderByDiscAndName()
    {
        (string, long, long)[] files = [("vehicle/blue_falcon_p.arc", 0x1000, 300), ("vehicle/carcmntex.tpl", 0x2000, 40)];
        byte[] shared = [1, 2, 3];
        var key = MachineCache.DiscKey(files, shared);
        Assert.Matches("^[0-9a-f]{16}$", key);
        var folder = MachineCache.Folder("data", key);
        Assert.Equal(Path.Combine("data", "cache", "machines", key), folder);
        Assert.Equal(Path.Combine(folder, "m05.bin"), MachineCache.StockFile(folder, 5));
        Assert.Equal(Path.Combine(folder, "m40.bin"), MachineCache.StockFile(folder, 40));
        Assert.Equal(Path.Combine(folder, "c15-44-58.bin"), MachineCache.CustomFile(folder, 15, 44, 58));

        // the same table in any order: the same key
        Assert.Equal(key, MachineCache.DiscKey(files.Reverse(), shared));
        // a file moved, longer, renamed or gone, or other shared textures: another key
        Assert.NotEqual(key, MachineCache.DiscKey([files[0], ("vehicle/carcmntex.tpl", 0x2010, 40)], shared));
        Assert.NotEqual(key, MachineCache.DiscKey([files[0], ("vehicle/carcmntex.tpl", 0x2000, 41)], shared));
        Assert.NotEqual(key, MachineCache.DiscKey([files[0], ("vehicle/carcmntex2.tpl", 0x2000, 40)], shared));
        Assert.NotEqual(key, MachineCache.DiscKey([files[0]], shared));
        Assert.NotEqual(key, MachineCache.DiscKey(files, [1, 2, 4]));
        Assert.NotEqual(key, MachineCache.DiscKey(files, null));
    }

    [Fact]
    public void ACardStillIsKeptPerMachineLookAndSizeAndOnePaintedLookAMachine()
    {
        InTempFolder(folder =>
        {
            var stock = MachineCache.StillFile(folder, 6, 2, null, 176, 104);
            Assert.Equal(Path.Combine(folder, $"still-m06-s2-176x104-r{MachineCache.StillRecipe}.png"), stock);
            var red = MachineCache.StillFile(folder, 6, null, new Rgb(0xE8, 0x43, 0x2E), 176, 104);
            Assert.Equal(Path.Combine(folder, $"still-m06-pE8432E-176x104-r{MachineCache.StillRecipe}.png"), red);
            Assert.NotEqual(stock, MachineCache.StillFile(folder, 6, 2, null, 352, 208));
            Assert.NotEqual(stock, MachineCache.StillFile(folder, 16, 2, null, 176, 104));

            byte[] png = [0x89, 0x50, 0x4E, 0x47, 1];
            MachineCache.KeepStill(stock, png);
            MachineCache.KeepStill(red, png);
            var other = MachineCache.StillFile(folder, 16, null, new Rgb(1, 2, 3), 176, 104);
            MachineCache.KeepStill(other, png);
            Assert.Equal(png, CourseStillCache.Read(red));
            // another colour of machine 6 replaces its red, and leaves its stock look and machine 16's colour
            var blue = MachineCache.StillFile(folder, 6, null, new Rgb(0, 0, 255), 176, 104);
            MachineCache.KeepStill(blue, png);
            Assert.False(File.Exists(red));
            Assert.True(File.Exists(blue) && File.Exists(stock) && File.Exists(other));
        });
    }

    [Fact]
    public void AKeptStockMachineIsReadBackAsItWas()
    {
        InTempFolder(folder =>
        {
            var file = MachineCache.StockFile(folder, 6);
            Assert.Null(MachineCache.Read(file));
            var paint = MachinePaint.Of(StockSet())!;
            var kept = new CachedMachine(Model(), paint, null);
            MachineCache.Keep(file, kept);
            Assert.False(File.Exists(file + Online.Protocol.AtomicFile.Suffix));

            var back = MachineCache.Read(file)!;
            Same(kept.Model, back.Model);
            Assert.Null(back.Anchors);
            // the paint works as before: its painted textures, a stock file, a colour painted
            Assert.Equal(paint.PaintedTextures, back.Paint!.PaintedTextures);
            Assert.Equal(paint.Stock(2), back.Paint.Stock(2));
            Assert.Equal(paint.Painted(new Rgb(10, 200, 90)), back.Paint.Painted(new Rgb(10, 200, 90)));
        });
    }

    [Fact]
    public void AKeptCustomMachineKeepsItsPartsPlaces()
    {
        InTempFolder(folder =>
        {
            var file = MachineCache.CustomFile(folder, 15, 44, 58);
            Vector3[] anchors = [new(0, 1, 2), new(0, 2, -1), new(0.5f, 1, 4)];
            MachineCache.Keep(file, new CachedMachine(Model(), null, anchors));
            var back = MachineCache.Read(file)!;
            Assert.Null(back.Paint);
            Assert.Equal(anchors, back.Anchors);
            Same(Model(), back.Model);
        });
    }

    [Fact]
    public void ADamagedOrOldFileIsNone()
    {
        InTempFolder(folder =>
        {
            var file = MachineCache.StockFile(folder, 1);
            MachineCache.Keep(file, new CachedMachine(Model(), MachinePaint.Of(StockSet()), null));
            var whole = File.ReadAllBytes(file);
            Assert.NotNull(MachineCache.Read(file));

            // cut short anywhere in its data: none (the last four bytes are the packing's own checksum, of data that is
            // whole without them)
            foreach (var length in new[] { 0, 4, 8, 20, whole.Length / 2, whole.Length - 5 })
            {
                File.WriteAllBytes(file, whole[..length]);
                Assert.Null(MachineCache.Read(file));
            }
            // another form of the file (its recipe word): none
            var old = (byte[])whole.Clone();
            old[4]++;
            File.WriteAllBytes(file, old);
            Assert.Null(MachineCache.Read(file));
            // not the cache's file at all: none
            File.WriteAllBytes(file, [.. "not a machine at all, just some bytes"u8]);
            Assert.Null(MachineCache.Read(file));
            // every byte of the packed part changed in turn: none, or a machine (never an error)
            for (var i = 8; i < whole.Length; i += Math.Max(1, whole.Length / 200))
            {
                var bad = (byte[])whole.Clone();
                bad[i] ^= 0x5A;
                File.WriteAllBytes(file, bad);
                _ = MachineCache.Read(file);
            }
        });
    }

    /// <summary>A small machine: two meshes (one see-through, textured), a texture of its own and a shared one.</summary>
    private static MachineModel Model()
    {
        MachineMesh Mesh(float x, int texture, bool translucent) => new(
            [new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0)], [Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1)], [Vector4.One, new(1, 0, 0, 1), new(0, 1, 0, 0.5f)], [0, 1, 2],
            new MachineMaterial(new(0.7f), new(0.5f), new(0.3f), 25, translucent ? 0.5f : 1, texture, translucent, translucent,
                TextureWrap.Mirror, TextureWrap.Clamp, Painted: translucent, Part: translucent ? 1 : -1));
        var own = Decode(StockSet()[0]);
        var shared = new Picture(2, 2, [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 9))]);
        return new MachineModel([Mesh(0, 0, false), Mesh(3, 2, true)], [new MachineTexture(own), null, new MachineTexture(shared, AlphaOnly: true)], 2);
    }

    private static void Same(MachineModel a, MachineModel b)
    {
        Assert.Equal(a.OwnTextures, b.OwnTextures);
        Assert.Equal(a.Center, b.Center);
        Assert.Equal(a.Radius, b.Radius);
        Assert.Equal(a.Textures.Count, b.Textures.Count);
        for (var t = 0; t < a.Textures.Count; t++)
        {
            Assert.Equal(a.Textures[t]?.AlphaOnly, b.Textures[t]?.AlphaOnly);
            Assert.Equal(a.Textures[t]?.Picture.Width, b.Textures[t]?.Picture.Width);
            Assert.Equal(a.Textures[t]?.Picture.Rgba, b.Textures[t]?.Picture.Rgba);
        }
        Assert.Equal(a.Meshes.Count, b.Meshes.Count);
        for (var m = 0; m < a.Meshes.Count; m++)
        {
            Assert.Equal(a.Meshes[m].Material, b.Meshes[m].Material);
            Assert.Equal(a.Meshes[m].Positions, b.Meshes[m].Positions);
            Assert.Equal(a.Meshes[m].Normals, b.Meshes[m].Normals);
            Assert.Equal(a.Meshes[m].Uvs, b.Meshes[m].Uvs);
            Assert.Equal(a.Meshes[m].Colors, b.Meshes[m].Colors);
            Assert.Equal(a.Meshes[m].Indices, b.Meshes[m].Indices);
        }
        // and the packing the views take is the same
        Assert.Equal(PackedModel.Of(a).Vertices, PackedModel.Of(b).Vertices);
        Assert.Equal(PackedModel.Of(a).Indices, PackedModel.Of(b).Indices);
    }

    private static List<byte[]> StockSet()
    {
        Rgb[] paint = [new(200, 30, 30), new(30, 60, 200), new(40, 180, 50), new(220, 190, 30)];
        return [.. paint.Select(c => Tpl(Cmpr(8, 8, (x, y) => x >= 4 ? Grey : c)))];
    }

    private static void InTempFolder(Action<string> test)
    {
        var data = Directory.CreateTempSubdirectory("launcher-tests").FullName;
        try
        {
            test(MachineCache.Folder(data, "0123456789abcdef"));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
