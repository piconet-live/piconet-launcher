using System.Diagnostics;
using Fzgx.GameCube.Disc;

namespace Launcher.Core;

// What the data folder holds of the game, and the words rewritten without the disc.
public static partial class DiscPrep
{
    /// <summary>The prepared game folder when the game can start from it, else null (the caller then looks where a
    /// development copy has the game). A prepared folder whose files are intact and whose words are another game
    /// program's (the launcher was updated) is brought up to date first: two small files are rewritten, in
    /// milliseconds, without the disc.</summary>
    /// <param name="bundle">The mod bundle in use (its <c>patches.json</c> names the executable's words); null or a
    /// bundle without a list: only the pins are wanted.</param>
    public static string? ReadyFolder(string dataFolder, string? bundle, DiscPins? pins = null)
    {
        var status = Status(dataFolder, bundle, pins);
        if (status is { State: PreparedState.Stale, NeedsDisc: false })
        {
            return Refresh(dataFolder, bundle, pins).GameFolder;   // null while a preparation is at work
        }
        return status.State == PreparedState.Ready ? GameFolder(dataFolder, pins) : null;
    }

    /// <summary>What the data folder holds, found out cheaply: the record, the named files' sizes and hashes as the
    /// record has them (the 20 named files are the only ones read whole: 4 MB), every file of the game's own file
    /// table present with its size and no other file beside them, one byte of every other file (the last that is
    /// not zero, where the record says it is: a file that was emptied, or cut short and filled up with zeros as a
    /// power cut leaves it, has a zero there), and the patched words against the ones the bundle in use wants.
    /// A byte changed elsewhere in a file no pin names is not seen: that takes <see cref="Verify"/>, which reads
    /// everything. No file is read before its size is known to be the expected one. Never throws.</summary>
    public static PreparedStatus Status(string dataFolder, string? bundle, DiscPins? pins = null)
    {
        pins ??= DiscPins.Game;
        try
        {
            if (!File.Exists(RecordFile(dataFolder)))
            {
                return new PreparedStatus(PreparedState.NotPrepared, NotPreparedMessage, NeedsDisc: true);
            }
            if (Record.Read(RecordFile(dataFolder)) is not { } record)
            {
                return Damaged("the record of what was prepared cannot be read", RecordName);
            }
            if (record.Format != RecordFormat || record.GameId != pins.GameId || record.Disc != pins.Disc || record.Revision != pins.Revision ||
                !record.ContentSha256.Equals(pins.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new PreparedStatus(PreparedState.Stale,
                    "Game files from another launcher version · prepare again", NeedsDisc: true);
            }
            var game = GameFolder(dataFolder, pins);
            foreach (var name in pins.Named.Keys)
            {
                var path = Local(game, name);
                if (!record.Checked.TryGetValue(name, out var expected) || !File.Exists(path))
                {
                    return Damaged($"{name} is missing", name);
                }
                if (new FileInfo(path).Length != pins.Named[name].Size)
                {
                    return Damaged($"{name} has another size", name);   // and is not read: it could be of any size
                }
                if (!Sha(File.ReadAllBytes(path)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return Damaged($"{name} is changed", name);
                }
            }
            var (files, folders) = DiscImage.TableOf(Small(Local(game, Table), DiscImage.LargestTable));
            if (Unexpected(game, files, folders) is { } other)
            {
                return Damaged(other.Problem, other.File);
            }
            if (Emptied(game, files, record.Ends) is { } emptied)
            {
                return Damaged($"{emptied} was emptied or cut short", emptied);
            }
            var wanted = Words(pins, bundle, File.ReadAllBytes(Local(game, Executable)));
            return Same(wanted.Words, record.Words)
                ? new PreparedStatus(PreparedState.Ready, "Game files ready", NeedsDisc: false) { FolderSha256 = record.FolderSha256 }
                : new PreparedStatus(PreparedState.Stale, "Game files for another game program version", NeedsDisc: false);
        }
        catch (InvalidDataException e)
        {
            // the bundle's list cannot be used, or the prepared file table is no file table
            return new PreparedStatus(PreparedState.Stale, e.Message, NeedsDisc: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Damaged("they cannot be read: " + e.Message, null);
        }
    }

    /// <summary>The whole check of a prepared game: <see cref="Status"/>, and when that says ready, every file read
    /// and hashed and the folder's digest held against the record's (<see cref="HashFolder"/>). It finds what the
    /// cheap check cannot, a changed byte anywhere, at the price of reading the game's 1.43 GB: 1.5 s on one
    /// PC with the files still in the system's cache (the cheap check there: 0.08 s); from a disk that has to be
    /// read first it is the time the disk takes for 1.43 GB, which was not measured. Never throws but for a
    /// cancellation.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
    public static PreparedStatus Verify(string dataFolder, string? bundle, DiscPins? pins = null, CancellationToken cancel = default)
    {
        pins ??= DiscPins.Game;
        var status = Status(dataFolder, bundle, pins);
        if (status.State != PreparedState.Ready)
        {
            return status;
        }
        try
        {
            return HashFolder(GameFolder(dataFolder, pins), cancel).Folder.Equals(status.FolderSha256, StringComparison.OrdinalIgnoreCase)
                ? status : Damaged("a file's bytes are not the ones that were prepared", null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Damaged("they cannot be read: " + e.Message, null);
        }
    }

    /// <summary>Rewrites the patched words of a prepared game for the bundle in use, without the disc: every word
    /// the record names is put back to its stock value (it must hold the recorded word), then the wanted words are
    /// written (each over its stock word), and the record is written anew. Only for a state that is
    /// <see cref="PreparedState.Stale"/> without <see cref="PreparedStatus.NeedsDisc"/>; anything else is told as it
    /// is, and while a preparation is at work in the folder the answer is <see cref="DiscPrepOutcome.Busy"/>.
    /// Never throws.</summary>
    public static DiscPrepResult Refresh(string dataFolder, string? bundle, DiscPins? pins = null)
    {
        // nothing to rewrite: said without the lock, and without making a folder for it
        if (Status(dataFolder, bundle, pins) is not { State: PreparedState.Stale, NeedsDisc: false })
        {
            return Refreshed(dataFolder, bundle, pins);
        }
        try
        {
            using var only = Only(dataFolder);
            return only is null ? new DiscPrepResult(DiscPrepOutcome.Busy, BusyMessage) : Refreshed(dataFolder, bundle, pins);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DiscPrepResult(DiscPrepOutcome.Failed, "The game's files could not be written: " + e.Message);
        }
    }

    /// <summary><see cref="Refresh"/> for whoever holds the lock.</summary>
    private static DiscPrepResult Refreshed(string dataFolder, string? bundle, DiscPins? pins)
    {
        pins ??= DiscPins.Game;
        var clock = Stopwatch.StartNew();
        var status = Status(dataFolder, bundle, pins);
        var game = GameFolder(dataFolder, pins);
        if (status.State == PreparedState.Ready)
        {
            return new DiscPrepResult(DiscPrepOutcome.AlreadyReady, status.Message) { GameFolder = game, FolderSha256 = status.FolderSha256, Elapsed = clock.Elapsed };
        }
        if (status is not { State: PreparedState.Stale, NeedsDisc: false })
        {
            return new DiscPrepResult(DiscPrepOutcome.Failed, status.Message) { File = status.File, Elapsed = clock.Elapsed };
        }
        try
        {
            var record = Record.Read(RecordFile(dataFolder)) ?? throw new InvalidDataException("The record of what was prepared cannot be read.");
            var wanted = Words(pins, bundle, File.ReadAllBytes(Local(game, Executable)));
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] Bytes(string name) => files.TryGetValue(name, out var b) ? b : files[name] = File.ReadAllBytes(Local(game, name));
            foreach (var word in record.Words)
            {
                Write(Bytes(word.File), word with { Original = word.New, New = word.Original });
            }
            foreach (var word in wanted.Words)
            {
                Write(Bytes(word.File), word);
            }
            // from here on the folder is not ready until the new record is written
            File.Delete(RecordFile(dataFolder));
            var hashes = new Dictionary<string, string>(record.Checked, StringComparer.Ordinal);
            var gameFileChanged = false;
            foreach (var (name, bytes) in files)
            {
                Online.Protocol.AtomicFile.WriteAllBytes(Local(game, name), bytes);
                gameFileChanged |= !name.StartsWith("sys/", StringComparison.Ordinal) && !Sha(bytes).Equals(hashes.GetValueOrDefault(name), StringComparison.OrdinalIgnoreCase);
                hashes[name] = Sha(bytes);
            }
            // the folder's digest follows the words. The executable's words are the ones that change with a game
            // program: the five system files are read again (1.7 MB) and the game files' digest stands. A word in a
            // game file that changed (other pins: another launcher) costs a pass over all of them.
            var (folderSha, filesSha, filesBytes) = gameFileChanged ? HashFolder(game)
                : (FolderDigest(SystemHashes(game), record.FilesBytes, record.FilesSha256), record.FilesSha256, record.FilesBytes);
            (record with
            {
                Words = wanted.Words, Image = wanted.Image, PatchList = wanted.Version, Checked = hashes, FolderSha256 = folderSha, FilesSha256 = filesSha,
                FilesBytes = filesBytes, Prepared = DateTimeOffset.UtcNow,
            }).Write(RecordFile(dataFolder));
            return new DiscPrepResult(DiscPrepOutcome.Refreshed, "Game files ready") { GameFolder = game, FolderSha256 = folderSha, Elapsed = clock.Elapsed };
        }
        catch (InvalidDataException e)
        {
            return new DiscPrepResult(DiscPrepOutcome.Failed, e.Message) { Elapsed = clock.Elapsed };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DiscPrepResult(DiscPrepOutcome.Failed, "The game's files could not be written: " + e.Message) { Elapsed = clock.Elapsed };
        }
    }

    /// <summary>What a prepared game folder holds that its own file table does not name, or lacks of it; null when
    /// the two agree. Names and sizes only.</summary>
    private static (string Problem, string File)? Unexpected(string game, IReadOnlyList<DiscFile> files, IReadOnlyList<string> folders)
    {
        var root = Path.Combine(game, "files");
        if (!Directory.Exists(root))
        {
            return ("the folder files is missing", "files");
        }
        var found = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            found[Path.GetRelativePath(root, file.FullName).Replace('\\', '/')] = file.Length;
        }
        foreach (var file in files)
        {
            if (!found.Remove(file.Path, out var size))
            {
                return ($"files/{file.Path} is missing", "files/" + file.Path);
            }
            if (size != file.Length)
            {
                return ($"files/{file.Path} has another size", "files/" + file.Path);
            }
        }
        if (found.Count > 0)
        {
            var extra = found.Keys.Order(StringComparer.Ordinal).First();
            return ($"files/{extra} does not belong to the game", "files/" + extra);
        }
        return folders.FirstOrDefault(f => !Directory.Exists(Local(root, f))) is { } folder ? ($"the folder files/{folder} is missing", "files/" + folder) : null;
    }

    /// <summary>The first of the game's files that has a zero where the record says its last byte that is not zero
    /// is: one byte of each file is read. Null: none, or a record from before these were kept.</summary>
    private static string? Emptied(string game, IReadOnlyList<DiscFile> files, IReadOnlyDictionary<string, long>? ends)
    {
        if (ends is null)
        {
            return null;
        }
        Span<byte> one = stackalloc byte[1];
        foreach (var file in files)
        {
            if (!ends.TryGetValue(file.Path, out var end))
            {
                continue;
            }
            using var stream = new FileStream(Local(game, "files/" + file.Path), FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            if (end < 0 || end >= stream.Length || stream.Seek(end, SeekOrigin.Begin) != end || stream.Read(one) != 1 || one[0] == 0)
            {
                return "files/" + file.Path;
            }
        }
        return null;
    }

    private static PreparedStatus Damaged(string what, string? file) =>
        new(PreparedState.Damaged, $"Game files damaged · {what} · prepare again", NeedsDisc: true, file);

    private static bool Same(IReadOnlyList<PatchWord> a, IReadOnlyList<PatchWord> b)
    {
        static IEnumerable<(string, int, uint, uint)> Key(IReadOnlyList<PatchWord> words) =>
            words.Select(w => (w.File, w.Offset, w.Original, w.New)).Order();
        return Key(a).SequenceEqual(Key(b));
    }
}
