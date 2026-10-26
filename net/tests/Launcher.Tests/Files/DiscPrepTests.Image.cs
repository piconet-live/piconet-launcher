using System.Buffers.Binary;
using System.Text;
using Launcher.Core;

namespace Launcher.Tests;

// The image, its file table, the pins and the digests.
public sealed partial class DiscPrepTests
{
    [Fact]
    public void ImagesOfTheSameFilesLaidOutDifferentlyGiveTheSameFolder()
    {
        // the same files as the original has them and as a rebuilt copy has them: the executable elsewhere, the
        // files in another order and packed closer, the table rewritten and after the files
        var rebuilt = new SyntheticDisc { Layout = SyntheticLayout.Rebuilt };
        var first = _disc.Build();
        var second = rebuilt.Build();
        Assert.NotEqual(first.Length, second.Length);
        Assert.False(first.AsSpan(0x420, 0x1C).SequenceEqual(second.AsSpan(0x420, 0x1C)));
        using (var a = DiscImage.Open(_disc.Write(_dir, "original.iso")))
        using (var b = DiscImage.Open(rebuilt.Write(_dir, "rebuilt.iso")))
        {
            Assert.NotEqual(a.SystemFiles[3].Offset, b.SystemFiles[3].Offset);
            Assert.NotEqual(a.SystemFiles[4].Length, b.SystemFiles[4].Length);
            var (inA, inB) = (a.Table().Files, b.Table().Files);
            static IEnumerable<string> Listed(DiscImage image)
            {
                var table = new byte[image.SystemFiles[4].Length];
                image.Read(image.SystemFiles[4].Offset, table, "the table");
                return DiscImage.TableOf(table).Files.Select(f => f.Path);
            }
            Assert.NotEqual(Listed(a), Listed(b));                                                     // the table's order
            Assert.NotEqual(inA.OrderBy(f => f.Offset).Select(f => f.Path), inB.OrderBy(f => f.Offset).Select(f => f.Path));   // the image's
            Assert.Equal(inA.Select(f => (f.Path, f.Length)).Order(), inB.Select(f => (f.Path, f.Length)).Order());            // the same files
            Assert.All(inA, f => Assert.NotEqual(f.Offset, inB.Single(o => o.Path == f.Path).Offset));
        }

        foreach (var bundle in new[] { null, Bundle("native") })
        {
            var other = Path.Combine(_dir, "data-rebuilt");
            var one = Run(Path.Combine(_dir, "original.iso"), bundle, Options with { Again = true });
            var two = DiscPrep.Prepare(Path.Combine(_dir, "rebuilt.iso"), other, bundle, null, Options with { Again = true }, TestContext.Current.CancellationToken);
            Assert.Equal((DiscPrepOutcome.Prepared, DiscPrepOutcome.Prepared), (one.Outcome, two.Outcome));
            AssertGame(bundle is null ? PinWords : BundleWords());

            // the folders: the same files with the same bytes, the same folders (the empty one too)
            var (a, b) = (Tree(Game), Tree(DiscPrep.GameFolder(other, Pins)));
            Assert.Equal(a.Files.Keys, b.Files.Keys);
            Assert.All(a.Files, f => Assert.True(f.Value.AsSpan().SequenceEqual(b.Files[f.Key]), f.Key));
            Assert.Equal(a.Folders, b.Folders);
            Assert.Contains("files/test_model", a.Folders);

            // the digests: the same, in the result and in the record, and what the folder on the disk gives
            Assert.Equal(64, one.FolderSha256!.Length);
            Assert.Equal(one.FolderSha256, two.FolderSha256);
            Assert.Equal(one.FolderSha256, Recorded(Data, "folder_sha256"));
            Assert.Equal(one.FolderSha256, Recorded(other, "folder_sha256"));
            Assert.Equal(Recorded(Data, "files_sha256"), Recorded(other, "files_sha256"));
            Assert.Equal((one.FolderSha256, Recorded(Data, "files_sha256")), (Hashed(Game).Folder, Hashed(Game).Files));
            Assert.Equal(one.FolderSha256, Hashed(DiscPrep.GameFolder(other, Pins)).Folder);
            Assert.Equal(one.FolderSha256, DiscPrep.Status(Data, bundle, Pins).FolderSha256);
            Assert.Equal(one.FolderSha256, Run(null, bundle).FolderSha256);                 // found ready
            Directory.Delete(other, recursive: true);
        }

        // the header written is the original's, and the table is neither image's own: both in their one form
        Assert.True(first.AsSpan(0, 0x440).SequenceEqual(File.ReadAllBytes(Path.Combine(Game, "sys", "boot.bin"))));
        Assert.True(_disc.CanonicalTable().AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Game, "sys", "fst.bin"))));
        Assert.Equal(_disc.CanonicalTable(), rebuilt.CanonicalTable());
        Assert.Equal(_disc.Pins().Named["sys/fst.bin"], rebuilt.Pins().Named["sys/fst.bin"]);
    }

    [Fact]
    public void TheFileTableIsWrittenInOneForm()
    {
        var files = _disc.Files.Where(f => f.Value is not null).Select(f => (f.Key, f.Value!.Length)).ToList();
        var folders = new[] { "enemy_line", "motion", "movie", "stage", "stage/deep", "test_model" };
        var filesAt = _disc.LayoutWords()[DiscImage.LayoutFilesAt];
        var expected = _disc.CanonicalTable();                                 // the test's own writer
        Assert.Equal(expected, DiscImage.CanonicalTable(files, folders, filesAt));
        // the order given does not matter, and only the empty folder has to be named
        Assert.Equal(expected, DiscImage.CanonicalTable(files.AsEnumerable().Reverse(), ["test_model"], filesAt));
        Assert.Equal(expected, DiscImage.CanonicalTable(files, folders.AsEnumerable().Reverse(), filesAt));

        // read back: the same files; in each folder by name in upper case ("deep" before "ST00.lz"); the first file
        // where the header says the files start and every next one on a 32 KiB step, none inside another
        var (read, readFolders) = DiscImage.TableOf(expected);
        Assert.Equal(files.Order(), read.Select(f => (f.Path, f.Length)).Order());
        Assert.Equal(folders, readFolders);
        Assert.Equal(["enemy_line/line__.bin", "fze.sample.rel", "fze.title.rel", "motion/empty.skl", "movie/big.sfd", "opening.bnr",
            "stage/deep/COLI_COURSE01", "stage/ST00.lz", "stage/st01.gma", "stage/st01.tpl"], read.Select(f => f.Path));
        Assert.Equal((long)filesAt, read[0].Offset);
        Assert.All(read.Skip(1), f => Assert.Equal(0L, f.Offset % DiscImage.CanonicalAlignment));
        Assert.All(read.Zip(read.Skip(1)), p => Assert.True(p.First.Offset + p.First.Length <= p.Second.Offset));

        // a path twice, or a file where a folder is: no table
        Assert.Throws<InvalidDataException>(() => DiscImage.CanonicalTable([.. files, ("opening.bnr", 1)], folders, filesAt));
        Assert.Throws<InvalidDataException>(() => DiscImage.CanonicalTable([.. files, ("stage", 1)], folders, filesAt));
        Assert.Throws<InvalidDataException>(() => DiscImage.CanonicalTable([.. files, ("opening.bnr/x", 1)], folders, filesAt));
    }

    [Fact]
    public void TheListsWordsOfTheExecutableAreFoundByTheirSections()
    {
        var (words, image, version) = DiscPrep.ExecutableWords(Bundle("native"), _disc.Dol);
        Assert.Equal("native", image);
        Assert.Equal(1, version);
        Assert.Equal([0x140, 0x180, 0x310], words.Select(w => w.Offset));
        Assert.All(words, w => Assert.Equal("sys/main.dol", w.File));
        Assert.Equal([0x80003140u, 0x80003180u, 0x80004010u], words.Select(w => w.Address!.Value));
        Assert.Equal(["pin", "linked", "sound_queue"], words.Select(w => w.What));
        Assert.Empty(DiscPrep.ExecutableWords(null, _disc.Dol).Words);
        Assert.Empty(DiscPrep.ExecutableWords(Path.Combine(_dir, "no-bundle"), _disc.Dol).Words);
        // with the pins: the executable's pin once, the sample module's word too
        Assert.Equal(4, DiscPrep.Words(Pins, Bundle("native"), _disc.Dol).Words.Count);
    }

    [Fact]
    public void TheImagesFileTableIsReadTheSameBothWays()
    {
        using var image = DiscImage.Open(_disc.Write(_dir));
        Assert.Equal(("GFZE01", 0, 0, "MADE-UP TEST DISC"), (image.GameId, image.Disc, image.Revision, image.Title));
        Assert.Equal(["sys/boot.bin", "sys/bi2.bin", "sys/apploader.img", "sys/main.dol", "sys/fst.bin"], image.SystemFiles.Select(f => f.Path));
        Assert.Equal((0x420, 8, 5), (DiscImage.LayoutAt, DiscImage.LayoutWords, DiscImage.LayoutFilesAt));
        var (files, folders) = image.Table();                                  // the launcher's disc reader
        var table = new byte[image.SystemFiles[4].Length];
        image.Read(image.SystemFiles[4].Offset, table, "the table");
        var alone = DiscImage.TableOf(table);                                  // the table alone, as a prepared folder has it
        Assert.Equal(files.OrderBy(f => f.Path, StringComparer.Ordinal), alone.Files.OrderBy(f => f.Path, StringComparer.Ordinal));
        Assert.Equal(_disc.Files.Where(f => f.Value is not null).Select(f => f.Key), files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["enemy_line", "motion", "movie", "stage", "stage/deep", "test_model"], folders.Order(StringComparer.Ordinal));
        Assert.Equal(folders, alone.Folders);
        Assert.Throws<InvalidDataException>(() => DiscImage.TableOf(new byte[8]));
        Assert.Throws<InvalidDataException>(() => DiscImage.TableOf(SyntheticDisc.Bytes(30, 600)));

        // a table of folders inside folders without end is not followed: 40 deep, each the only entry of the one before
        var deep = new byte[(41 * 12) + 2];
        BinaryPrimitives.WriteUInt32BigEndian(deep.AsSpan(8), 41);
        for (var i = 1; i <= 40; i++)
        {
            deep[i * 12] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(deep.AsSpan((i * 12) + 8), 41);
        }
        deep[41 * 12] = (byte)'d';
        Assert.Throws<DiscLimitException>(() => DiscImage.TableOf(deep));
    }

    [Fact]
    public void TheDigestOverEveryFileIsModernGekkosForm()
    {
        var files = _disc.Files.Where(f => f.Value is not null).Select(f => (f.Key, (long)f.Value!.Length, SyntheticDisc.Sha(f.Value))).ToList();
        Assert.Equal(_disc.Digest(), DiscPrep.ContentDigest(files));
        files.Reverse();                                                       // the order given does not matter
        Assert.Equal(_disc.Digest(), DiscPrep.ContentDigest(files));
        // one file, by hand: 8 bytes of length, "a", 8 bytes of size, the hash's 64 hex characters
        var sha = SyntheticDisc.Sha([1, 2, 3]);
        byte[] record = [0, 0, 0, 0, 0, 0, 0, 1, (byte)'a', 0, 0, 0, 0, 0, 0, 0, 3, .. Encoding.ASCII.GetBytes(sha)];
        Assert.Equal(SyntheticDisc.Sha(record), DiscPrep.ContentDigest([("a", 3, sha.ToUpperInvariant())]));
    }

    [Fact]
    public void TheGamesOwnPinsAreInTheLauncher()
    {
        // the resource DiscPins.json (written by net/tools/DiscPinsWriter from your disc): its shape, not the game's data
        var pins = DiscPins.Game;
        Assert.Equal(("GFZE01", 0, 0, "F-Zero GX (USA)"), (pins.GameId, pins.Disc, pins.Revision, pins.Name));
        Assert.Equal(64, pins.ContentSha256.Length);
        Assert.Equal(20, pins.Named.Count);
        foreach (var name in (string[])["sys/boot.bin", "sys/bi2.bin", "sys/apploader.img", "sys/main.dol", "sys/fst.bin", "files/fze.sample.rel",
            "files/enemy_line/line__.bin"])
        {
            Assert.Contains(name, pins.Named.Keys);
        }
        Assert.Equal(15, pins.Named.Keys.Count(n => n.StartsWith("files/", StringComparison.Ordinal)));
        Assert.All(pins.Named.Values, f => Assert.Equal(64, f.Sha256.Length));
        // the two pins: the executable's and the sample module's
        Assert.Equal([("sys/main.dol", 0x2940), ("files/fze.sample.rel", 0x104)], pins.Words.Select(w => (w.File, w.Offset)));
        Assert.Equal(0x80005940u, pins.Words[0].Address);
        Assert.All(pins.Words, w => Assert.NotEqual(w.Original, w.New));
        // the header's layout words as they are written, and where the one form of the file table starts the files
        Assert.Equal(DiscImage.LayoutWords, pins.Layout.Count);
        Assert.Equal(pins.Layout[5], pins.FilesAt);
        Assert.InRange(pins.Layout[1] + pins.Layout[2], pins.Layout[0], pins.FilesAt);   // the executable, then the table, then the files
        Assert.Equal(Path.Combine("data", "recomp-game", "GFZE01"), DiscPrep.GameFolder("data"));
    }
}
