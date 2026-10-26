using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Core;

// The digests of a game folder and the record beside it.
public static partial class DiscPrep
{
    /// <summary>One SHA-256 over every file of the game, in the form ModernGekko hashes a game's <c>files</c> folder
    /// (<c>src/runtime/game.cpp</c>, <c>HashDirectorySha256</c>, its <c>assets_sha256</c>): for each file, sorted by
    /// its path under <c>files/</c> as bytes, the path's length, the path, the size, and the file's SHA-256 as 64
    /// lower-case hex characters; the lengths as 8 bytes, big-endian. Lower-case hex.</summary>
    public static string ContentDigest(IEnumerable<(string Path, long Size, string Sha256)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[8];
        foreach (var (path, size, sha) in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var name = Encoding.ASCII.GetBytes(path);
            BinaryPrimitives.WriteInt64BigEndian(number, name.Length);
            whole.AppendData(number);
            whole.AppendData(name);
            BinaryPrimitives.WriteInt64BigEndian(number, size);
            whole.AppendData(number);
            whole.AppendData(Encoding.ASCII.GetBytes(sha.ToLowerInvariant()));
        }
        return Convert.ToHexStringLower(whole.GetHashAndReset());
    }

    /// <summary>One SHA-256 over a prepared game folder as it is written: the form of <see cref="ContentDigest"/>
    /// one level up, over the system files by their names (<c>sys/boot.bin</c>, ...) and one more entry,
    /// <c>files</c>, whose size is the bytes of all the game's files and whose hash is the digest of the folder
    /// <c>files</c> as written (<see cref="ContentDigest"/> of it: what ModernGekko's runner reports as
    /// <c>assets_sha256</c> for this folder). In two levels so that new words in the executable, which every new
    /// game program brings, give the new digest without reading the game's 1.4 GB again. The empty folders are
    /// covered through <c>sys/fst.bin</c>, which names them. Lower-case hex.</summary>
    public static string FolderDigest(IEnumerable<(string Path, long Size, string Sha256)> system, long filesBytes, string filesSha256) =>
        ContentDigest(system.Append(("files", filesBytes, filesSha256)));

    /// <summary>The digests of a game folder (<c>sys</c> and <c>files</c> inside) worked out from what is on the
    /// disk, every file read: <see cref="FolderDigest"/>, the digest of <c>files</c> alone, and the bytes of the
    /// files under it. What the record of a prepared game holds, for whoever wants to check a folder against it or
    /// two folders against each other.</summary>
    /// <exception cref="IOException">A file cannot be read.</exception>
    public static (string Folder, string Files, long FilesBytes) HashFolder(string gameFolder, CancellationToken cancel = default)
    {
        var root = Path.Combine(gameFolder, "files");
        var files = new List<(string Path, long Size, string Sha256)>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancel.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            files.Add((Path.GetRelativePath(root, path).Replace('\\', '/'), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream))));
        }
        var filesSha = ContentDigest(files);
        var bytes = files.Sum(f => f.Size);
        return (FolderDigest(SystemHashes(gameFolder), bytes, filesSha), filesSha, bytes);
    }

    /// <summary>The system files of a game folder as they stand, for <see cref="FolderDigest"/>: every file in
    /// <c>sys</c>, read.</summary>
    private static List<(string Path, long Size, string Sha256)> SystemHashes(string game) =>
        [.. Directory.EnumerateFiles(Path.Combine(game, "sys")).Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ("sys/" + Path.GetFileName(path), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
        })];

    /// <summary>A small file's bytes, its length looked at first: a file from outside (the record, a prepared file
    /// table, a patch list) can be of any size, and none is read beyond <paramref name="limit"/>.</summary>
    /// <exception cref="InvalidDataException">The file is longer.</exception>
    private static byte[] Small(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > limit)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} is {file.Length:N0} bytes, and no such file is longer than {limit:N0}."));
        }
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The record beside the prepared game (<see cref="RecordName"/>): which disc it was made from (the
    /// digest over its files), which words were written (with their stock words, so they can be taken back), the mod
    /// image and list version they belong to, the SHA-256 of each named file as it stands, and the digest of the
    /// game folder as it stands (<see cref="FolderDigest"/>) with the two things it is made from besides the system
    /// files. The digests are the same for every image the launcher accepts; where the image was and when this was
    /// written are the player's own. <paramref name="Ends"/>: for each file no pin names, by its path under
    /// <c>files/</c>, where its last byte that is not zero is (a file of zeros only has no entry), which is what
    /// <see cref="Status"/> looks at to find an emptied file; null in a record from before these were kept.</summary>
    private sealed record Record(int Format, string GameId, int Disc, int Revision, string ContentSha256, int Files, long Bytes,
        string? Image, int? PatchList, IReadOnlyList<PatchWord> Words, Dictionary<string, string> Checked, string FolderSha256,
        string FilesSha256, long FilesBytes, string Source, long SourceBytes, DateTimeOffset Prepared, IReadOnlyDictionary<string, long>? Ends)
    {
        /// <summary>No record is near this size (the game's, with every file's entry: 300 KB).</summary>
        private const int Largest = 4 << 20;

        /// <summary>The record in a file; null when it is not one.</summary>
        public static Record? Read(string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(Small(path, Largest));
                var r = doc.RootElement;
                return new Record(r.GetProperty("format").GetInt32(), r.GetProperty("game").GetString()!, r.GetProperty("disc").GetInt32(),
                    r.GetProperty("revision").GetInt32(), r.GetProperty("content_sha256").GetString()!, r.GetProperty("files").GetInt32(),
                    r.GetProperty("bytes").GetInt64(),
                    r.TryGetProperty("image_sha1", out var image) ? image.GetString() : null,
                    r.TryGetProperty("patch_list", out var list) && list.ValueKind == JsonValueKind.Number ? list.GetInt32() : null,
                    [.. r.GetProperty("words").EnumerateArray().Select(DiscPins.Word)],
                    r.GetProperty("checked").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal),
                    // not in a record of format 1, which is told by its number as another launcher's
                    r.TryGetProperty("folder_sha256", out var folder) ? folder.GetString() ?? "" : "",
                    r.TryGetProperty("files_sha256", out var files) ? files.GetString() ?? "" : "",
                    r.TryGetProperty("files_bytes", out var bytes) ? bytes.GetInt64() : 0,
                    r.GetProperty("source").GetProperty("path").GetString() ?? "", r.GetProperty("source").GetProperty("bytes").GetInt64(),
                    r.GetProperty("prepared").GetDateTimeOffset(),
                    r.TryGetProperty("ends", out var ends) && ends.ValueKind == JsonValueKind.Object
                        ? ends.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal) : null);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException
                or ArgumentNullException or InvalidDataException or ArgumentException)
            {
                return null;
            }
        }

        public void Write(string path)
        {
            var words = new JsonArray();
            foreach (var w in Words)
            {
                var word = new JsonObject
                {
                    ["file"] = w.File, ["offset"] = w.Offset, ["original"] = w.Original.ToString("X8", CultureInfo.InvariantCulture),
                    ["new"] = w.New.ToString("X8", CultureInfo.InvariantCulture), ["what"] = w.What,
                };
                if (w.Address is { } address)
                {
                    word["address"] = address.ToString("X8", CultureInfo.InvariantCulture);
                }
                words.Add(word);
            }
            var hashes = new JsonObject();
            foreach (var (name, sha) in Checked.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                hashes[name] = sha;
            }
            var record = new JsonObject
            {
                ["format"] = Format, ["game"] = GameId, ["disc"] = Disc, ["revision"] = Revision, ["content_sha256"] = ContentSha256,
                ["folder_sha256"] = FolderSha256, ["files_sha256"] = FilesSha256, ["files_bytes"] = FilesBytes,
                ["files"] = Files, ["bytes"] = Bytes, ["image_sha1"] = Image, ["patch_list"] = PatchList, ["words"] = words, ["checked"] = hashes,
                ["source"] = new JsonObject { ["path"] = Source, ["bytes"] = SourceBytes },
                ["prepared"] = Prepared.ToString("O", CultureInfo.InvariantCulture),
            };
            if (Ends is not null)
            {
                var ends = new JsonObject();
                foreach (var (name, end) in Ends.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    ends[name] = end;
                }
                record["ends"] = ends;   // last: thousands of lines
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            Online.Protocol.AtomicFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
        }
    }
}
