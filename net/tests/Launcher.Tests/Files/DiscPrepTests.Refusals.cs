using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

// What is refused, each with its sentence.
public sealed partial class DiscPrepTests
{
    [Theory]
    [InlineData("GFZJ01", "MADE-UP TEST DISC", "This is the game's disc for Japan (GFZJ01). The launcher needs the made-up game, GFZE01.")]
    [InlineData("GFZP01", "MADE-UP TEST DISC", "This is the game's disc for Europe (GFZP01). The launcher needs the made-up game, GFZE01.")]
    [InlineData("GFZJ8P", "MADE-UP ARCADE", "This is another game: MADE-UP ARCADE (GFZJ8P). The launcher needs the made-up game, GFZE01.")]
    [InlineData("GALE01", "ANOTHER GAME", "This is another game: ANOTHER GAME (GALE01). The launcher needs the made-up game, GFZE01.")]
    public void AnotherGameOrRegionIsRefusedByName(string id, string title, string message)
    {
        var result = Run(new SyntheticDisc { Id = id, Title = title }.Write(_dir), null);
        Assert.Equal(DiscPrepOutcome.WrongGame, result.Outcome);
        Assert.Equal(message, result.Message);
        Assert.False(result.Ready);
        Assert.Null(result.GameFolder);
        AssertNothingKept();
    }

    [Fact]
    public void AnotherRevisionOrDiscNumberIsRefusedByName()
    {
        var revised = Run(new SyntheticDisc { Revision = 1 }.Write(_dir), null);
        Assert.Equal(DiscPrepOutcome.WrongVersion, revised.Outcome);
        Assert.Equal("This is the made-up game, disc 1, revision 1. The launcher needs disc 1, revision 0: the game program was made from that one.", revised.Message);
        Assert.Equal(DiscPrepOutcome.WrongVersion, Run(new SyntheticDisc { Disc = 1 }.Write(_dir), null).Outcome);
        AssertNothingKept();
    }

    [Fact]
    public void AChangedFileIsRefusedAndNamedWhenItIsOneCheckedByName()
    {
        var pins = Pins;                                   // of the unchanged disc
        var options = new DiscPrepOptions { Pins = pins };

        _disc.Dol[0x200] ^= 1;                             // the executable
        var executable = Run(_disc.Write(_dir), null, options);
        Assert.Equal(DiscPrepOutcome.ModifiedFile, executable.Outcome);
        Assert.Equal("sys/main.dol", executable.File);
        Assert.Equal("This disc image is not an unchanged the made-up game: its file sys/main.dol is modified or damaged.", executable.Message);
        AssertNothingKept();
        _disc.Dol[0x200] ^= 1;

        _disc.Files["enemy_line/line__.bin"]![77] ^= 0x80;  // the file main.rel is decrypted from
        var module = Run(_disc.Write(_dir), null, options);
        Assert.Equal(DiscPrepOutcome.ModifiedFile, module.Outcome);
        Assert.Equal("files/enemy_line/line__.bin", module.File);
        AssertNothingKept();
        _disc.Files["enemy_line/line__.bin"]![77] ^= 0x80;

        _disc.Files["stage/st01.gma"]![60_000] ^= 1;        // a course: same size, one bit, not checked by name
        var course = Run(_disc.Write(_dir), null, options);
        Assert.Equal(DiscPrepOutcome.ModifiedFile, course.Outcome);
        Assert.Null(course.File);
        Assert.StartsWith("This disc image is not an unchanged the made-up game: one of its game files is modified or damaged. The 8 files the launcher " +
            "checks by name are intact; which of the other 7 differs it cannot say.", course.Message, StringComparison.Ordinal);
        AssertNothingKept();
        _disc.Files["stage/st01.gma"]![60_000] ^= 1;

        _disc.Files["stage/st01.tpl"] = new byte[2];        // another size: these are no longer the game's files
        var size = Run(_disc.Write(_dir), null, options);
        Assert.Equal(DiscPrepOutcome.ModifiedFile, size.Outcome);
        Assert.Equal("sys/fst.bin", size.File);
        Assert.Equal("This disc image is not an unchanged the made-up game: one of its files has another name or another size than the game's. " +
            "Dump the disc again, or check the image in Dolphin (Properties, Verify).", size.Message);
        AssertNothingKept();
        _disc.Files["stage/st01.tpl"] = SyntheticDisc.Bytes(5, 1);

        _disc.Files["fze.title.rel"] = SyntheticDisc.Bytes(2, 0x304);   // another size of a file checked by name
        var named = Run(_disc.Write(_dir), null, options);
        Assert.Equal((DiscPrepOutcome.ModifiedFile, "files/fze.title.rel"), (named.Outcome, named.File));
        AssertNothingKept();
    }

    [Fact]
    public void InAnyLayoutAChangedMissingExtraOrRenamedFileIsRefused()
    {
        var options = new DiscPrepOptions { Pins = Pins };  // of the unchanged game
        const string Start = "This disc image is not an unchanged the made-up game: ";
        const string Advice = ". Dump the disc again, or check the image in Dolphin (Properties, Verify).";
        foreach (var layout in new[] { SyntheticLayout.Original, SyntheticLayout.Rebuilt })
        {
            DiscPrepResult Refused(Action<SyntheticDisc> change)
            {
                var disc = new SyntheticDisc { Layout = layout };
                change(disc);
                var result = Run(disc.Write(_dir), null, options);
                Assert.Equal(DiscPrepOutcome.ModifiedFile, result.Outcome);
                AssertNothingKept();
                return result;
            }

            // one byte of one file: every name and size is the game's, the digest over every file is not
            var changed = Refused(d => d.Files["movie/big.sfd"]![1_234_567] ^= 0x10);
            Assert.Null(changed.File);
            Assert.StartsWith(Start + "one of its game files is modified or damaged.", changed.Message, StringComparison.Ordinal);

            // a missing file, an extra file, an extra empty folder: said with the counts
            var missing = Refused(d => d.Files.Remove("stage/st01.tpl"));
            Assert.Equal("sys/fst.bin", missing.File);
            Assert.Equal(Start + "it holds 9 files in 6 folders, and the game has 10 in 6: a file was added or removed" + Advice, missing.Message);
            var extra = Refused(d => d.Files["stage/mine.bin"] = SyntheticDisc.Bytes(40, 16));
            Assert.Equal(Start + "it holds 11 files in 6 folders, and the game has 10 in 6: a file was added or removed" + Advice, extra.Message);
            Assert.Equal(Start + "it holds 10 files in 7 folders, and the game has 10 in 6: a file was added or removed" + Advice,
                Refused(d => d.Files["movie/more"] = null).Message);

            // a renamed file, a file moved to another folder, the empty folder under another name
            void Rename(SyntheticDisc d, string from, string to)
            {
                d.Files[to] = d.Files[from];
                d.Files.Remove(from);
            }
            Assert.Equal(Start + "one of its files has another name or another size than the game's" + Advice,
                Refused(d => Rename(d, "opening.bnr", "opening.bnR")).Message);
            Assert.Equal("sys/fst.bin", Refused(d => Rename(d, "stage/st01.gma", "movie/st01.gma")).File);
            Assert.Equal("sys/fst.bin", Refused(d => Rename(d, "test_model", "test_models")).File);

            // a file the launcher checks by name: missing, and changed, by its name
            var gone = Refused(d => d.Files.Remove("fze.title.rel"));
            Assert.Equal(("files/fze.title.rel", Start + "its file files/fze.title.rel is missing."), (gone.File, gone.Message));
            Assert.Equal("files/fze.sample.rel", Refused(d => d.Files["fze.sample.rel"]![0x200] ^= 1).File);
            Assert.Equal("sys/main.dol", Refused(d => d.Dol[0x3FF] ^= 1).File);
        }

        // the header outside its layout words is checked: another title is another header
        var titled = Run(new SyntheticDisc { Title = "MADE-UP TEST DISC 2" }.Write(_dir), null, options);
        Assert.Equal((DiscPrepOutcome.ModifiedFile, "sys/boot.bin"), (titled.Outcome, titled.File));
        AssertNothingKept();
    }

    [Fact]
    public void ATruncatedImageIsRefused()
    {
        var whole = _disc.Build();
        foreach (var length in new[] { whole.Length / 2, 0x4800, 0x3100, 0x2450, 0x300 })
        {
            var path = Path.Combine(_dir, "cut.iso");
            File.WriteAllBytes(path, whole[..length]);
            var result = Run(path, null);
            Assert.Equal(DiscPrepOutcome.DamagedImage, result.Outcome);
            Assert.StartsWith("This disc image is incomplete: ", result.Message, StringComparison.Ordinal);
            Assert.EndsWith($"It is {length:N0} bytes.", result.Message, StringComparison.Ordinal);
            AssertNothingKept();
        }
    }

    [Theory]
    [InlineData("RVZ\u0001", "This is a compressed disc image (RVZ). The launcher reads plain ISO images only. In Dolphin, right-click the game, choose Convert File, pick the format ISO, and choose the new file here.")]
    [InlineData("WIA\u0001", "This is a compressed disc image (WIA). The launcher reads plain ISO images only. In Dolphin, right-click the game, choose Convert File, pick the format ISO, and choose the new file here.")]
    [InlineData("\u0001\u00C0\u000B\u00B1", "This is a compressed disc image (GCZ). The launcher reads plain ISO images only. In Dolphin, right-click the game, choose Convert File, pick the format ISO, and choose the new file here.")]
    [InlineData("CISO", "This is a compact disc image (CISO). The launcher reads plain ISO images only. In Dolphin, right-click the game, choose Convert File, pick the format ISO, and choose the new file here.")]
    [InlineData("PK\u0003\u0004", "This is an archive (.zip, .7z or .rar). Unpack it and choose the disc image inside.")]
    [InlineData("7z\u00BC\u00AF", "This is an archive (.zip, .7z or .rar). Unpack it and choose the disc image inside.")]
    public void OtherKindsOfDumpAreRefusedWithWhatToDo(string start, string message)
    {
        var path = Path.Combine(_dir, "dump.bin");
        File.WriteAllBytes(path, [.. Encoding.Latin1.GetBytes(start), .. SyntheticDisc.Bytes(20, 5000)]);
        var result = Run(path, null);
        Assert.Equal(DiscPrepOutcome.UnsupportedFormat, result.Outcome);
        Assert.Equal(message, result.Message);
        AssertNothingKept();
    }

    [Fact]
    public void WhatIsNoGameCubeImageIsRefused()
    {
        var path = Path.Combine(_dir, "file.iso");
        File.WriteAllBytes(path, SyntheticDisc.Bytes(21, 70_000));
        Assert.Equal((DiscPrepOutcome.NotADisc, "This file is not a GameCube disc image."),
            (Run(path, null).Outcome, Run(path, null).Message));
        File.WriteAllBytes(path, []);
        Assert.Equal(DiscPrepOutcome.NotADisc, Run(path, null).Outcome);

        var wii = SyntheticDisc.Bytes(22, 70_000);
        BinaryPrimitives.WriteUInt32BigEndian(wii.AsSpan(0x18), 0x5D1C9EA3);
        File.WriteAllBytes(path, wii);
        var result = Run(path, null);
        Assert.Equal((DiscPrepOutcome.WrongGame, "This is a Wii disc image. The launcher needs the made-up game for the GameCube (GFZE01)."), (result.Outcome, result.Message));

        // an NKit image is a GameCube image with NKit's own data in its header (and its files moved)
        var nkit = _disc.Build();
        "NKIT v01"u8.CopyTo(nkit.AsSpan(0x200));
        File.WriteAllBytes(path, nkit);
        var shrunk = Run(path, null);
        Assert.Equal(DiscPrepOutcome.UnsupportedFormat, shrunk.Outcome);
        Assert.StartsWith("This is an NKit image", shrunk.Message, StringComparison.Ordinal);

        var missing = Run(Path.Combine(_dir, "none.iso"), null);
        Assert.Equal((DiscPrepOutcome.ImageMissing, $"There is no disc image at {Path.Combine(_dir, "none.iso")}."), (missing.Outcome, missing.Message));
        Assert.Equal(DiscPrepOutcome.ImageMissing, Run(null, null).Outcome);
        AssertNothingKept();
    }

    [Fact]
    public void APatchListThatDoesNotFitIsTheProgramsFaultAndNothingIsWritten()
    {
        var image = _disc.Write(_dir);
        var incomplete = Run(image, Bundle("incomplete", complete: false));
        Assert.Equal((DiscPrepOutcome.Failed, "The game program's patch list is incomplete (it was built without the game's files)."),
            (incomplete.Outcome, incomplete.Message));
        AssertNothingKept();

        // a list made for other game files: the stock word is checked before anything is written
        var other = Bundle("other");
        var list = Path.Combine(other, DiscPrep.PatchListName);
        var stock = SyntheticDisc.Word(_disc.Dol, 0x180).ToString("X8", CultureInfo.InvariantCulture);
        File.WriteAllText(list, File.ReadAllText(list).Replace($"\"original\": \"{stock}\"", "\"original\": \"DEADBEEF\"", StringComparison.Ordinal));
        var misfit = Run(image, other);
        Assert.Equal(DiscPrepOutcome.Failed, misfit.Outcome);
        Assert.Equal($"sys/main.dol holds {stock} at 0x180 (linked), neither DEADBEEF nor 497FD784: the game program's patch list does not fit these game files.", misfit.Message);
        AssertNothingKept();

        // the list and the pins both name the executable's pin: they must say the same
        var disagrees = Run(image, Bundle("disagrees", pinNew: 0x38600001));
        Assert.Equal((DiscPrepOutcome.Failed, "The game program's patch list and the launcher's pins disagree about sys/main.dol at 0x140."),
            (disagrees.Outcome, disagrees.Message));

        var nowhere = Bundle("nowhere");
        File.WriteAllText(Path.Combine(nowhere, DiscPrep.PatchListName),
            "{\"version\": 1, \"complete\": true, \"main_rel\": \"801BA660\", \"patches\": [{\"address\": \"80100000\", \"original\": \"00000000\", \"new\": \"00000001\", \"what\": \"lost\"}]}");
        Assert.Equal("The game program's patch list names 80100000 (lost), which is in no section of the executable.",
            Run(image, nowhere).Message);
        File.WriteAllText(Path.Combine(nowhere, DiscPrep.PatchListName), "[]");
        Assert.StartsWith("The game program's patch list cannot be read: ", Run(image, nowhere).Message, StringComparison.Ordinal);
        AssertNothingKept();
    }

    [Fact]
    public void TheDiscPageAndThePreparationJudgeTheDiscByTheSameCode()
    {
        // what the Disc page shows (DiscCheck) is the preparation's own first look: for every kind of file, the
        // refusal the page has is the one the preparation gives
        var cut = Path.Combine(_dir, "cut.iso");
        File.WriteAllBytes(cut, _disc.Build()[..0x2450]);
        var compressed = Path.Combine(_dir, "compressed.iso");
        File.WriteAllBytes(compressed, [.. "RVZ\u0001"u8, .. SyntheticDisc.Bytes(20, 5000)]);
        var doctored = _disc.Build();                              // a loader larger than the console's memory
        BinaryPrimitives.WriteUInt32BigEndian(doctored.AsSpan(0x2440 + 0x14), 32 << 20);
        File.WriteAllBytes(Path.Combine(_dir, "doctored.iso"), doctored);
        (string? Image, DiscStatus Page, DiscPrepOutcome Preparation)[] cases =
        [
            (Path.Combine(_dir, "doctored.iso"), DiscStatus.Unsupported, DiscPrepOutcome.DamagedImage),
            (new SyntheticDisc { Id = "GFZJ01" }.Write(_dir, "japan.iso"), DiscStatus.WrongGame, DiscPrepOutcome.WrongGame),
            (new SyntheticDisc { Revision = 1 }.Write(_dir, "revised.iso"), DiscStatus.WrongGame, DiscPrepOutcome.WrongVersion),
            (compressed, DiscStatus.Unsupported, DiscPrepOutcome.UnsupportedFormat),
            (cut, DiscStatus.Unsupported, DiscPrepOutcome.DamagedImage),
            (Path.Combine(_dir, "none.iso"), DiscStatus.Missing, DiscPrepOutcome.ImageMissing),
            (null, DiscStatus.Missing, DiscPrepOutcome.ImageMissing),
        ];
        foreach (var (image, page, preparation) in cases)
        {
            var result = Run(image, null);
            Assert.Equal((page, preparation), (DiscCheck.Check(image), result.Outcome));
            Assert.Equal(result.Message, DiscPrep.Refusal(image, Pins)!.Message);
            Assert.NotNull(DiscCheck.Problem(image));
        }
        // and a disc the page takes is one the preparation starts on
        var good = _disc.Write(_dir);
        Assert.Equal((DiscStatus.Ok, null, null), (DiscCheck.Check(good), DiscCheck.Problem(good), DiscPrep.Refusal(good, Pins)));
        Assert.Equal(DiscPrepOutcome.Prepared, Run(good, null).Outcome);
    }
}
