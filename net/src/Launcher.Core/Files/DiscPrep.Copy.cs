using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Fzgx.GameCube.Disc;

namespace Launcher.Core;

// The preparation itself: what is checked before anything is written, the copy, its check, and the game folder under its name.
public static partial class DiscPrep
{
    /// <summary>The preparation in its steps, in this order: everything checked before anything is written (the
    /// system area, the table, the files a pin names, the words), room made, the copy with every file hashed as it
    /// goes by, its digest, and the checked copy under the game folder's name with its record last.</summary>
    private static DiscPrepResult Extract(string? imagePath, string dataFolder, string? bundle, string staging,
        IProgress<DiscPrepProgress>? progress, DiscPrepOptions options, bool keep, CancellationToken cancel)
    {
        var pins = options.Pins;
        progress?.Report(new DiscPrepProgress(DiscPrepStage.Checking, 0, pins.Bytes, 0, pins.Files, null));
        using var image = Open(imagePath, pins);

        var system = SystemArea(image, pins);
        var (files, folders) = Listed(image);
        var named = NamedFiles(files, pins);
        system[Table] = OneFormTable(files, folders, pins);
        var stock = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, file) in named)
        {
            stock[name] = Pinned(Bytes(image, file, name, pins), name, pins);
        }
        var (wanted, patched) = WordsTried(pins, bundle, system, stock);

        var game = GameFolder(dataFolder, pins);
        var filesBytes = files.Sum(f => (long)f.Length);
        var total = filesBytes + system.Values.Sum(f => (long)f.Length);
        MakeRoom(dataFolder, staging, game, keep, total, options);

        var done = WriteSystem(staging, folders, system, patched);
        (done, var count, var hashes, var ends, var hashing) = CopyFiles(image, staging, files, pins, done, total, progress, cancel);
        progress?.Report(new DiscPrepProgress(DiscPrepStage.Finishing, done, total, count, files.Count, null));
        if (!ContentDigest(hashes).Equals(pins.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.ModifiedFile,
                $"This disc image is not an unchanged {pins.Name}: one of its game files is modified or damaged. The {pins.Named.Count} files the launcher " +
                $"checks by name are intact; which of the other {files.Count + image.SystemFiles.Count - pins.Named.Count} differs it cannot say. " +
                "Dump the disc again, or check the image in Dolphin (Properties, Verify)."));
        }

        // the words into the copy, the copy to its name, the record last
        foreach (var (name, bytes) in patched.Where(p => !system.ContainsKey(p.Key)))
        {
            File.WriteAllBytes(Local(staging, name), bytes);
        }
        var checkedFiles = pins.Named.Keys.ToDictionary(n => n, n => patched.TryGetValue(n, out var p) ? Sha(p)
            : system.TryGetValue(n, out var s) ? Sha(s) : Sha(stock[n]), StringComparer.Ordinal);
        // the digest of the folder as it is written, from the hashes taken on the way: no file is read again
        var filesSha = ContentDigest(hashes.Select(h => patched.TryGetValue("files/" + h.Path, out var p) ? (h.Path, h.Size, Sha(p)) : h));
        var folderSha = FolderDigest(system.Select(f => (f.Key, (long)f.Value.Length, Sha(patched.GetValueOrDefault(f.Key, f.Value)))), filesBytes, filesSha);
        Swap(staging, game, new Record(RecordFormat, pins.GameId, pins.Disc, pins.Revision, pins.ContentSha256, files.Count, total, wanted.Image, wanted.Version,
            wanted.Words, checkedFiles, folderSha, filesSha, filesBytes, Path.GetFullPath(imagePath!), image.Length, DateTimeOffset.UtcNow, ends), RecordFile(dataFolder));
        return new DiscPrepResult(DiscPrepOutcome.Prepared, "Game files ready")
        {
            GameFolder = game, FolderSha256 = folderSha, BytesWritten = total, Hashing = Stopwatch.GetElapsedTime(0, hashing),
        };
    }

    /// <summary>The system area's files but the table, read and held against their pins.</summary>
    private static Dictionary<string, byte[]> SystemArea(DiscImage image, DiscPins pins)
    {
        // the system area, before anything is written. The header's words that say where things lie in this image
        // are replaced by the pinned ones first: what is checked, and written, is the header in its one form
        var system = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var part in image.SystemFiles.Where(p => p.Path != Table))
        {
            var bytes = Bytes(image, part, part.Path, pins);
            for (var i = 0; part.Path == Header && i < pins.Layout.Count; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(DiscImage.LayoutAt + (i * 4)), pins.Layout[i]);
            }
            system[part.Path] = Pinned(bytes, part.Path, pins);
        }
        return system;
    }

    /// <summary>The image's files and folders by its table; a name the launcher will not write is a refusal.</summary>
    private static (IReadOnlyList<DiscFile> Files, IReadOnlyList<string> Folders) Listed(DiscImage image)
    {
        IReadOnlyList<DiscFile> files;
        IReadOnlyList<string> folders;
        try
        {
            (files, folders) = image.Table();
        }
        catch (DiscLimitException e)
        {
            throw NoDisc(e.Message);
        }
        catch (InvalidDataException e)
        {
            throw Incomplete(image, e.Message);
        }
        foreach (var path in files.Select(f => f.Path).Concat(folders))
        {
            if (path.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.AsSpan().ContainsAny(NotInAName) || part.Any(char.IsControl)))
            {
                throw new Stop(new DiscPrepResult(DiscPrepOutcome.ModifiedFile, $"This disc image has a file name the launcher will not write: {path}.") { File = path });
            }
        }
        return (files, folders);
    }

    /// <summary>The files a pin names, each found once and of its pin's size.</summary>
    private static Dictionary<string, DiscFile> NamedFiles(IReadOnlyList<DiscFile> files, DiscPins pins)
    {
        // the game's files by name and size, whatever their places and order: first the ones a pin names, so that
        // a refusal can name them, then all of them at once. The image's own table is any table that names them;
        // written in the one form (names and sizes only go in) it must be the pinned one, which a missing, an
        // extra or a renamed file, another size or another folder makes impossible
        var named = new Dictionary<string, DiscFile>(StringComparer.Ordinal);
        foreach (var (name, pin) in pins.Named.Where(n => !n.Key.StartsWith("sys/", StringComparison.Ordinal)))
        {
            var found = files.Where(f => "files/" + f.Path == name).ToList();
            if (found.Count == 0)
            {
                throw new Stop(new DiscPrepResult(DiscPrepOutcome.ModifiedFile, $"This disc image is not an unchanged {pins.Name}: its file {name} is missing.") { File = name });
            }
            if (found.Count > 1 || found[0].Length != pin.Size)
            {
                throw Modified(name, pins);
            }
            named[name] = found[0];
        }
        return named;
    }

    /// <summary>The table in its one form, when it is the pinned one; else the refusal (see <see cref="NamedFiles"/>).</summary>
    private static byte[] OneFormTable(IReadOnlyList<DiscFile> files, IReadOnlyList<string> folders, DiscPins pins)
    {
        byte[]? table;
        try
        {
            table = DiscImage.CanonicalTable(files.Select(f => (f.Path, f.Length)), folders, pins.FilesAt);
        }
        catch (InvalidDataException)
        {
            table = null;   // a path that is there twice
        }
        if (table is null || (pins.Named.TryGetValue(Table, out var tablePin) && (tablePin.Size != table.Length || !tablePin.Sha256.Equals(Sha(table), StringComparison.OrdinalIgnoreCase))))
        {
            var what = files.Count != pins.Files || folders.Count != pins.Folders
                ? string.Create(CultureInfo.InvariantCulture, $"it holds {files.Count:N0} files in {folders.Count:N0} folders, and the game has {pins.Files:N0} in {pins.Folders:N0}: a file was added or removed")
                : "one of its files has another name or another size than the game's";
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.ModifiedFile,
                $"This disc image is not an unchanged {pins.Name}: {what}. Dump the disc again, or check the image in Dolphin (Properties, Verify).") { File = Table });
        }
        return table;
    }

    /// <summary>The words to write and the files they change, with the words in.</summary>
    private static ((IReadOnlyList<PatchWord> Words, string? Image, int? Version) Wanted, Dictionary<string, byte[]> Patched) WordsTried(
        DiscPins pins, string? bundle, Dictionary<string, byte[]> system, Dictionary<string, byte[]> stock)
    {
        // the words, tried on the stock files before 1.4 GB are written: a list that does not fit is the game
        // program's fault, not the disc's
        (IReadOnlyList<PatchWord> Words, string? Image, int? Version) wanted;
        var patched = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            wanted = Words(pins, bundle, system[Executable]);
            foreach (var word in wanted.Words)
            {
                if (!patched.TryGetValue(word.File, out var bytes))
                {
                    var from = system.TryGetValue(word.File, out var s) ? s : stock.TryGetValue(word.File, out var f) ? f
                        : throw new InvalidDataException($"A patched word is in {word.File}, which is not a file the launcher checks by name.");
                    patched[word.File] = bytes = (byte[])from.Clone();
                }
                Write(bytes, word);
            }
        }
        catch (InvalidDataException e)
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.Failed, e.Message));
        }
        return (wanted, patched);
    }

    /// <summary>Room for the copy, or the refusal that says how much is missing.</summary>
    private static void MakeRoom(string dataFolder, string staging, string game, bool keep, long total, DiscPrepOptions options)
    {
        // room: an unfinished copy goes, and so does a game folder that cannot be used. One whose files are intact
        // stays until the new copy is checked (keep), and for that time the disk holds both
        Directory.CreateDirectory(Path.Combine(dataFolder, FolderName));
        Remove(staging);
        if (!keep)
        {
            File.Delete(RecordFile(dataFolder));
            Remove(game);
            if (Directory.Exists(game))
            {
                throw new Stop(new DiscPrepResult(DiscPrepOutcome.Failed,
                    $"The game's files that are in {game} cannot be used and cannot be removed: another program has one of them open. If the game is running, close it and try again."));
            }
        }
        if (options.FreeSpace(Path.Combine(dataFolder, FolderName)) is { } free && free < total + Margin)
        {
            throw new Stop(new DiscPrepResult(DiscPrepOutcome.NotEnoughSpace,
                $"Not enough free space for the game's files in {dataFolder}: they need {Size(total + Margin)} and {Size(free)} is free. " +
                $"Free {Size(total + Margin - free)} more and try again." + (keep ? " The copy that is there stays until the new one is checked." : ""))
            { BytesNeeded = total + Margin, BytesFree = free });
        }
    }

    /// <summary>The copy's folders and its system files (with their words in).</summary>
    /// <returns>The bytes written.</returns>
    private static long WriteSystem(string staging, IReadOnlyList<string> folders, Dictionary<string, byte[]> system, Dictionary<string, byte[]> patched)
    {
        // the copy, in the image's own order (one pass over it), every file hashed as it goes by
        Directory.CreateDirectory(Path.Combine(staging, "sys"));
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        foreach (var folder in folders)
        {
            Directory.CreateDirectory(Local(staging, "files/" + folder));
        }
        long done = 0;
        foreach (var (name, bytes) in system)
        {
            File.WriteAllBytes(Local(staging, name), patched.TryGetValue(name, out var p) ? p : bytes);
            done += bytes.Length;
        }
        return done;
    }

    /// <summary>Every file of the table copied in the image's own order, hashed as it goes by.</summary>
    /// <param name="done">The bytes written before.</param>
    /// <returns>The bytes written in all, the files copied, each file's hash, where each file's last byte that is
    /// not zero lies, and the time the hashing took.</returns>
    private static (long Done, int Count, List<(string Path, long Size, string Sha256)> Hashes, SortedDictionary<string, long> Ends, long Hashing) CopyFiles(
        DiscImage image, string staging, IReadOnlyList<DiscFile> files, DiscPins pins, long done, long total,
        IProgress<DiscPrepProgress>? progress, CancellationToken cancel)
    {
        long reported = 0, hashing = 0;
        var count = 0;
        var hashes = new List<(string Path, long Size, string Sha256)>(files.Count);
        var ends = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var buffer = new byte[1 << 20];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(f => f.Offset))
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new DiscPrepProgress(DiscPrepStage.Extracting, done, total, count, files.Count, file.Path));
            reported = done;
            long end = -1;
            using (var output = new FileStream(Local(staging, "files/" + file.Path), new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, PreallocationSize = file.Length,
            }))
            {
                for (long at = 0; at < file.Length;)
                {
                    cancel.ThrowIfCancellationRequested();
                    var piece = buffer.AsSpan(0, (int)Math.Min(buffer.Length, file.Length - at));
                    try
                    {
                        image.Read(file.Offset + at, piece, file.Path);
                    }
                    catch (InvalidDataException e)
                    {
                        throw Incomplete(image, e.Message);
                    }
                    catch (IOException e)
                    {
                        throw new Stop(new DiscPrepResult(DiscPrepOutcome.DamagedImage, $"This disc image cannot be read at {file.Path}: {e.Message}") { File = "files/" + file.Path });
                    }
                    var before = Stopwatch.GetTimestamp();
                    hash.AppendData(piece);
                    hashing += Stopwatch.GetTimestamp() - before;
                    output.Write(piece);
                    end = piece.LastIndexOfAnyExcept((byte)0) is >= 0 and var last ? at + last : end;
                    at += piece.Length;
                    done += piece.Length;
                    if (done - reported >= 4L << 20)
                    {
                        progress?.Report(new DiscPrepProgress(DiscPrepStage.Extracting, done, total, count, files.Count, file.Path));
                        reported = done;
                    }
                }
            }
            hashes.Add((file.Path, file.Length, Convert.ToHexStringLower(hash.GetHashAndReset())));
            if (end >= 0 && !pins.Named.ContainsKey("files/" + file.Path))
            {
                ends[file.Path] = end;   // the files a pin names are hashed whole at every look instead
            }
            count++;
        }
        return (done, count, hashes, ends, hashing);
    }

    private static Stop Incomplete(DiscImage image, string detail) =>
        new(new DiscPrepResult(DiscPrepOutcome.DamagedImage, $"This disc image is incomplete: {detail} It is {image.Length:N0} bytes."));

    /// <summary>What an image holds that no GameCube disc holds (<see cref="DiscLimitException"/>), as a refusal.</summary>
    private static Stop NoDisc(string detail) => new(new DiscPrepResult(DiscPrepOutcome.DamagedImage,
        $"This disc image was changed or is damaged. {detail} Dump the disc again, or check the image in Dolphin (Properties, Verify)."));

    /// <summary>A file's bytes, read from the image. The size first: for a file that a pin names and that has
    /// another size than the pin's, nothing is allocated and nothing read.</summary>
    private static byte[] Bytes(DiscImage image, DiscFile file, string name, DiscPins pins)
    {
        if (pins.Named.TryGetValue(name, out var pin) && pin.Size != file.Length)
        {
            throw Modified(name, pins);
        }
        var bytes = new byte[file.Length];
        try
        {
            image.Read(file.Offset, bytes, name);
        }
        catch (InvalidDataException e)
        {
            throw Incomplete(image, e.Message);
        }
        return bytes;
    }

    /// <summary>A file's bytes when they are what its pin says (or no pin names it); else the refusal.</summary>
    private static byte[] Pinned(byte[] bytes, string name, DiscPins pins) =>
        pins.Named.TryGetValue(name, out var pin) && (pin.Size != bytes.Length || !pin.Sha256.Equals(Sha(bytes), StringComparison.OrdinalIgnoreCase))
            ? throw Modified(name, pins) : bytes;

    private static Stop Modified(string name, DiscPins pins) => new(new DiscPrepResult(DiscPrepOutcome.ModifiedFile,
        $"This disc image is not an unchanged {pins.Name}: its file {name} is modified or damaged.") { File = name });

    /// <summary>The checked copy takes the game folder's name, and its record is written last. A copy that is
    /// there (one whose files are intact: any other was removed before the copying) is moved aside first and
    /// removed last. Whatever fails in between, it gets its name back, and its record was never touched: it works
    /// as before.</summary>
    private static void Swap(string staging, string game, Record record, string recordFile)
    {
        var kept = Directory.Exists(game);
        if (kept)
        {
            try
            {
                Remove(game + Old);
                Move(game, game + Old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new Stop(new DiscPrepResult(DiscPrepOutcome.Failed,
                    $"The new copy of the game's files was written and checked, but the copy in {game} could not be replaced by it ({e.Message}). " +
                    "That copy stays as it was. If the game is running, close it and try again."));
            }
        }
        try
        {
            Move(staging, game);
            record.Write(recordFile);
        }
        catch (Exception e) when (kept && e is IOException or UnauthorizedAccessException)
        {
            Remove(game);
            try
            {
                Directory.Move(game + Old, game);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // the next preparation puts it back (Recover)
            }
            throw;
        }
        Remove(game + Old);
    }

    /// <summary>A folder under another name. A virus scanner may still hold a file of it: tried for a few seconds.</summary>
    private static void Move(string from, string to)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // left behind: the next preparation removes it, and nothing reads a folder without its record
        }
    }

    /// <summary>Windows' "disk full" errors (0x70, 0x27) and Unix's ENOSPC (28).</summary>
    private static bool DiskFull(Exception e) => e is IOException && ((e.HResult & 0xFFFF) is 0x70 or 0x27 || e.HResult == 28);
}
