using System.Buffers.Binary;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

// The launcher's look is read from the player's disc image as soon as the page's first look says it is the game's
// (DiscArt, at the theme's load): before any preparation has read its files, and on an image only the header of
// which was looked at. It reads the image as the preparation does, through the bounded reader.
public sealed partial class DiscPrepTests
{
    private static long Allocated(Action act)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        act();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void TheLaunchersLookIsReadFromTheDisc()
    {
        var disc = new SyntheticDisc();
        disc.Files["bmp_e/carname/bmp_carname_bfalcon.tpl"] = GarageFiles.Tpl(GarageFiles.Cmpr(8, 8, (_, _) => new Rgb(200, 16, 16)));
        disc.Files["bmp_e/bmp_sel.tpl"] = SyntheticDisc.Bytes(21, 500);            // no texture file at all
        var path = disc.Write(_dir, "look.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));

        using var art = DiscArt.Open(path);

        Assert.NotNull(art);
        var plate = art.Texture("bmp_e/carname/bmp_carname_bfalcon.tpl", 0);   // Blue Falcon's name plate
        Assert.NotNull(plate);
        Assert.Equal((8, 8), (plate.Width, plate.Height));
        Assert.True(GarageFiles.Pixel(plate, 3, 3).R > 150);
        Assert.Null(art.Texture("bmp_e/carname/bmp_carname_rgaze.tpl", 0));    // a file the disc does not have
        Assert.Null(art.Title(MenuTitle.Course));                               // a file that is no texture file
        Assert.Null(art.Course(1));
        Assert.Null(art.Cloud);
    }

    /// <summary>The review's first image: its file table names a path twice. The page's first look passes it (the
    /// header is the game's); the library's reader then threw an ArgumentException out of DiscArt.Open, at the
    /// theme's load. The disc has no look, and nothing is raised.</summary>
    [Fact]
    public void ADiscWhoseTableNamesAPathTwiceHasNoLookAndRaisesNothing()
    {
        var path = WithTable(Added(Table(), (0, "opening.bnr", 0x440, 16)));
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));

        Assert.Null(DiscArt.Open(path));
        // and again in upper case, which the folder the game is prepared into would hold as one file
        Assert.Null(DiscArt.Open(WithTable(Added(Table(), (0, "OPENING.BNR", 0x440, 16)))));
    }

    /// <summary>The review's second image: 30,000 folders, each inside the one before. The library's reader made a
    /// path for each (30,000 paths of up to 60,000 characters) and ran out of memory. The bounded reader stops at
    /// the depth no disc has.</summary>
    [Fact]
    public void ADiscOfFoldersInsideFoldersWithoutEndHasNoLookAndCostsLittle()
    {
        const int folders = 30_000;
        var table = new byte[((folders + 1) * 12) + 2];
        for (var i = 0; i <= folders; i++)
        {
            // a folder named "d" (the root's name is not read): its parent the entry before, its end the table's
            Put(table, i * 12, 0x01000000);
            Put(table, (i * 12) + 4, (uint)Math.Max(0, i - 1));
            Put(table, (i * 12) + 8, folders + 1);
        }
        table[^2] = (byte)'d';
        var path = WithTable(table);
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));

        DiscArt? art = null;
        var allocated = Allocated(() => art = DiscArt.Open(path));

        Assert.Null(art);
        Assert.InRange(allocated, 0, 8 << 20);
    }

    /// <summary>A table may give a file any length up to the image's: the launcher's look reads no file longer than
    /// its own longest by far.</summary>
    [Fact]
    public void AFileOfTheLookThatIsLongerThanAnyIsNotRead()
    {
        var disc = new SyntheticDisc();
        disc.Files["init/sel.tpl"] = SyntheticDisc.Bytes(22, 777);
        var path = disc.Write(_dir, "long.iso");
        var image = File.ReadAllBytes(path);
        var (tableAt, tableSize) = ((int)Word(image, 0x424), (int)Word(image, 0x428));
        var count = (int)Word(image, tableAt + 8);
        var entry = Enumerable.Range(1, count - 1).Single(i => image[tableAt + (i * 12)] == 0 && Word(image, tableAt + (i * 12) + 8) == 777);
        Put(image, tableAt + (entry * 12) + 8, DiscArt.LargestFile + 1);
        File.WriteAllBytes(path, image);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(Word(image, tableAt + (entry * 12) + 4) + DiscArt.LargestFile + 0x100L);   // the file lies inside the image
        }
        Assert.True(tableSize > 0);
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));

        using var art = DiscArt.Open(path);

        Assert.NotNull(art);
        Picture? cloud = null;
        var allocated = Allocated(() => cloud = art.Cloud);
        Assert.Null(cloud);
        Assert.InRange(allocated, 0, 1 << 20);
    }

    // ---- a file's own bytes (the second review): the image passes the first look, and the theme reads these files
    // at the launcher's start. Every file here is made up; none holds a byte of the game's.

    private static byte[] GoodTexture() => GarageFiles.Tpl(GarageFiles.Cmpr(8, 8, (_, _) => new Rgb(200, 16, 16)));

    /// <summary>A compressed file's first eight bytes (two words, low byte first: the packed length and the length it
    /// unpacks to) and what follows.</summary>
    private static byte[] Packed(int unpacked, params byte[] body)
    {
        var file = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(file, body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), unpacked);
        body.CopyTo(file, 8);
        return file;
    }

    private string DiscWith(string file, byte[] bytes, string name = "art.iso")
    {
        var disc = new SyntheticDisc();
        disc.Files[file] = bytes;
        var path = disc.Write(_dir, name);
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));                    // the first look sees the header only
        return path;
    }

    /// <summary>Case 1: a compressed file of eight bytes that says it unpacks to 2 GiB. The library's decompressor
    /// makes room for the size named before it reads on: 2^31-1 raised an out-of-memory error that nothing caught
    /// (the launcher closed at every start), and a little less took 2 GiB at each read.</summary>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0x7FFFFFC7)]
    [InlineData(1 << 30)]
    [InlineData(DiscArt.LargestUnpacked + 1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ACompressedFileThatNamesASizeNoFileHasIsNotUnpacked(int unpacked)
    {
        using var art = DiscArt.Open(DiscWith("init/sel.tpl.lz", Packed(unpacked)));
        Assert.NotNull(art);

        Picture? cloud = null;
        var allocated = Allocated(() => cloud = art.Cloud);

        Assert.Null(cloud);
        Assert.InRange(allocated, 0, 1 << 20);
        // nor where a model's or a paint's file is read, and a file cut off before it names a size is none either
        using var models = DiscArt.Open(DiscWith("vehicle/rgaze_p.arc.lz", Packed(unpacked), "models.iso"));
        Assert.InRange(Allocated(() => Assert.Null(models!.MachineModel(0))), 0, 1 << 20);
        using var cut = DiscArt.Open(DiscWith("stage/selst01.gma.lz", [1, 2, 3], "cut.iso"));
        Assert.Null(cut!.Course(1));
    }

    [Fact]
    public void ACompressedFileOfAGamesSizeIsStillUnpacked()
    {
        var good = GoodTexture();
        using var art = DiscArt.Open(DiscWith("bmp_e/carname/bmp_carname_bfalcon.tpl.lz", Fzgx.GameCube.Compression.AvLz.Compress(good)));
        var plate = art!.Texture("bmp_e/carname/bmp_carname_bfalcon.tpl", 0);
        Assert.NotNull(plate);
        Assert.Equal((8, 8), (plate.Width, plate.Height));
    }

    /// <summary>Case 2: one byte changed in a good texture file (its format: CI4, which the library does not
    /// decode). The file was parsed inside a catch and its picture decoded outside it: "Texture format CI4 cannot
    /// be decoded" went up to the theme's load.</summary>
    [Fact]
    public void ATextureInAFormatThatIsNotDecodedIsNoPictureAndRaisesNothing()
    {
        var file = GoodTexture();
        file[7] = 8;                                                           // the format's low byte: CMPR to CI4
        Assert.Throws<NotSupportedException>(() =>
        {
            var t = Fzgx.GameCube.Textures.AvTpl.Parse(file);
            t.DecodeLevel0(t.Textures[0]);
        });
        using var art = DiscArt.Open(DiscWith("init/sel.tpl", [.. file]));

        Assert.Null(art!.Texture("init/sel.tpl", 0));
        Assert.Null(art.Cloud);
        using var plates = DiscArt.Open(DiscWith("bmp_e/bmp_sel.tpl", file, "plates.iso"));
        Assert.Null(plates!.Title(MenuTitle.Course));
        Assert.Null(plates.SettingLabels);
    }

    /// <summary>Case 3: a texture that says it is 65,535 by 65,535 in a format of a byte a pixel: adding up its size
    /// overflowed in the library ("Arithmetic operation resulted in an overflow"), which nothing caught. No texture
    /// larger than the console's own is decoded.</summary>
    [Fact]
    public void ATextureThatNamesASizeNoTextureHasIsNoPictureAndRaisesNothing()
    {
        var file = GoodTexture();
        file[7] = 1;                                                           // a format of one byte a pixel
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(12), 0xFFFF);        // its width
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(14), 0xFFFF);        // its height
        file[16] = 0xFF;
        Assert.Throws<OverflowException>(() =>
        {
            var t = Fzgx.GameCube.Textures.AvTpl.Parse(file);
            t.DecodeLevel0(t.Textures[0]);
        });
        using var art = DiscArt.Open(DiscWith("init/sel.tpl", file));

        Picture? picture = null;
        var allocated = Allocated(() => picture = art!.Texture("init/sel.tpl", 0));

        Assert.Null(picture);
        Assert.InRange(allocated, 0, 1 << 20);
        // and one a pixel past the console's largest, in the made-up file's own format
        var wide = GoodTexture();
        BinaryPrimitives.WriteUInt16BigEndian(wide.AsSpan(12), DiscArt.LargestTexture + 1);
        using var other = DiscArt.Open(DiscWith("init/sel.tpl", wide, "wide.iso"));
        Assert.Null(other!.Texture("init/sel.tpl", 0));
    }

    /// <summary>Case 4: a machine's model whose file names no mesh where the reader takes the first one: "Sequence
    /// contains no elements", an error of a kind the model's reader did not catch.</summary>
    [Fact]
    public void AMachineModelWithoutWhatItsReaderTakesIsNoModelAndRaisesNothing()
    {
        var texture = GoodTexture();
        var model = GarageFiles.Gma(0, 1);
        BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(481), 255);
        BinaryPrimitives.WriteInt32BigEndian(model.AsSpan(259), 1);
        Assert.Throws<InvalidOperationException>(() => MachineModel.Read(model, texture, null));
        var disc = new SyntheticDisc();
        disc.Files["vehicle/rgaze_p.arc"] = GarageFiles.Archive(("rgaze.gma", model), ("rgaze.tpl", texture));
        // (the paint's four files, alike, so that the garage's whole reading of the machine runs)
        foreach (var k in new[] { "", "_cv1", "_cv2", "_cv3" })
        {
            disc.Files[$"vehicle/rgaze/rgaze{k}.tpl"] = texture;
        }
        var path = disc.Write(_dir, "model.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));
        using var art = DiscArt.Open(path);

        Assert.Null(art!.MachineModel(0));
        _ = art.Paint(0);                                                      // whatever it is, it raises nothing
        _ = art.StockSwatches(0);
    }

    /// <summary>No member of the look raises on a disc's bytes: every file the theme reads at the launcher's start,
    /// each made of bytes that are no file of its kind, and each call the theme makes.</summary>
    [Fact]
    public void NoMemberOfTheLookRaisesOnFilesOfNoise()
    {
        var disc = new SyntheticDisc();
        var seed = 100;
        foreach (var file in new[] { "init/sel.tpl", "bmp_e/bmp_sel.tpl.lz", "bmp_e/bmp_com.tpl", "bmp_e/stg/bmp_stg_mut.tpl", "bmp_e/stg/bmp_stg_com.tpl.lz",
                     "bmp_e/carname/bmp_carname_rgaze.tpl", "stage/selst01.gma", "vehicle/rgaze_p.arc", "vehicle/carcmntex.tpl", "vehicle/rgaze/rgaze.tpl",
                     "vehicle/rgaze/rgaze_cv1.tpl", "vehicle/rgaze/rgaze_cv2.tpl", "vehicle/rgaze/rgaze_cv3.tpl", "vehicle/rgaze/rgaze_sv0.tpl",
                     "vehicle/rgaze/rgaze_sv1.tpl", "vehicle/rgaze/rgaze_sv2.tpl", "vehicle/rgaze/rgaze_sv3.tpl" })
        {
            disc.Files[file] = file.EndsWith(".lz", StringComparison.Ordinal) ? Packed(4096, SyntheticDisc.Bytes(seed++, 600)) : SyntheticDisc.Bytes(seed++, 600);
        }
        var path = disc.Write(_dir, "noise.iso");
        Assert.Equal(DiscStatus.Ok, DiscCheck.Check(path));
        using var art = DiscArt.Open(path);
        Assert.NotNull(art);

        _ = art.Cloud;
        _ = art.SettingLabels;
        _ = art.Lettering("PICONET");
        _ = art.Venue(1);
        _ = art.AnyVenue;
        _ = art.Course(1);
        _ = art.MachineModel(0);
        _ = art.Paint(0);
        _ = art.Paint(0, main: false);
        _ = art.StockSwatches(0);
        foreach (var title in Enum.GetValues<MenuTitle>())
        {
            _ = art.Title(title);
        }
    }
}
