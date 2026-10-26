using System.Globalization;
using System.Text.Json;

namespace Launcher.Core;

/// <summary>One word written into a file of the prepared game: where (the file's name in the prepared folder,
/// <c>sys/main.dol</c> or <c>files/...</c>, and the offset in it), the stock word, which is checked before anything is
/// written, the new word, and what it is for. <paramref name="Address"/>: where the word is in the game's memory,
/// when the list says so.</summary>
public sealed record PatchWord(string File, int Offset, uint Original, uint New, string What, uint? Address = null);

/// <summary>A file of the disc the launcher checks by name: its size and its SHA-256 (lower-case hex).</summary>
public sealed record NamedFile(long Size, string Sha256);

/// <summary>
/// Which disc the game's files are prepared from (<see cref="DiscPrep"/>): the game's id, disc number and revision,
/// the files that are checked by name (the system area and the files the native module was made from), one digest
/// over every file of the game (<see cref="DiscPrep.ContentDigest"/>), and the pins: the words that make main.rel
/// load at the address the native module is built for. ModernGekko pins a release the same way
/// (<c>MODERNGEKKO_REQUIRED_DISC_ID</c>, <c>_DOL_SHA256</c>, <c>_REL_SHA256</c>, <c>_ASSETS_SHA256</c>).
/// <para>What is pinned is the game's files, by name and content, and not where an image has them (the same
/// files are the same game): the hashes of <c>sys/boot.bin</c> and <c>sys/fst.bin</c>, the two files that say where
/// things lie, are of the one form the preparation writes them in (<see cref="Layout"/>,
/// <see cref="DiscImage.CanonicalTable"/>), so an image whose files were moved, reordered or packed closer passes,
/// and one with a missing, extra, renamed or changed file does not.</para>
/// </summary>
/// <param name="Named">By name in the prepared folder (<c>sys/main.dol</c>, <c>files/fze.sample.rel</c>), each as it
/// is written there before any word.</param>
/// <param name="Words">The pins. The words that depend on the mod image are not here: they come from the bundle's
/// patch list when the game is prepared (<see cref="DiscPrep.ExecutableWords"/>).</param>
/// <param name="Layout">The header's eight words at <see cref="DiscImage.LayoutAt"/> as the preparation writes them
/// into <c>sys/boot.bin</c>, whatever the image has there: the original pressing's, so the header written is the
/// original's byte for byte. The runner copies the last four into the disc it builds from the folder (the first
/// four it replaces with its own).</param>
public sealed record DiscPins(string GameId, int Disc, int Revision, string Name, int Files, int Folders, long Bytes,
    string ContentSha256, IReadOnlyDictionary<string, NamedFile> Named, IReadOnlyList<PatchWord> Words, IReadOnlyList<uint> Layout)
{
    /// <summary>F-Zero GX (USA), GFZE01 revision 0: the disc every check of the native module ran on. Read from the
    /// resource <c>DiscPins.json</c>, which net/tools/DiscPinsWriter writes from the disc image, the kit's manifest and
    /// the hand-prepared game folder; no word or hash of it is typed by hand.</summary>
    public static DiscPins Game { get; } = FromResource();

    private static DiscPins FromResource()
    {
        using var stream = typeof(DiscPins).Assembly.GetManifestResourceStream("Launcher.Core.DiscPins.json")
            ?? throw new InvalidOperationException("the resource DiscPins.json is missing");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>The pins of a <c>DiscPins.json</c> text.</summary>
    /// <exception cref="JsonException">The text is not one.</exception>
    public static DiscPins Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var named = new Dictionary<string, NamedFile>(StringComparer.Ordinal);
            foreach (var file in root.GetProperty("named").EnumerateObject())
            {
                named[file.Name] = new NamedFile(file.Value.GetProperty("size").GetInt64(), file.Value.GetProperty("sha256").GetString()!);
            }
            return new DiscPins(root.GetProperty("game").GetString()!, root.GetProperty("disc").GetInt32(),
                root.GetProperty("revision").GetInt32(), root.GetProperty("name").GetString()!,
                root.GetProperty("files").GetInt32(), root.GetProperty("folders").GetInt32(), root.GetProperty("bytes").GetInt64(),
                root.GetProperty("content_sha256").GetString()!, named,
                [.. root.GetProperty("words").EnumerateArray().Select(Word)],
                root.GetProperty("header_layout").EnumerateArray().Select(w => Hex(w.GetString()!)).ToList() is { Count: DiscImage.LayoutWords } layout
                    ? layout : throw new FormatException("header_layout is not eight words"));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new JsonException("not a disc pins file", e);
        }
    }

    /// <summary>A word as the pins file and the prepared game's manifest write it: <c>file</c>, <c>offset</c>,
    /// <c>original</c> and <c>new</c> (8 hex digits), <c>what</c>, and <c>address</c> when known.</summary>
    public static PatchWord Word(JsonElement word) =>
        new(word.GetProperty("file").GetString()!, word.GetProperty("offset").GetInt32(),
            Hex(word.GetProperty("original").GetString()!), Hex(word.GetProperty("new").GetString()!),
            word.TryGetProperty("what", out var what) ? what.GetString() ?? "" : "",
            word.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.String ? Hex(address.GetString()!) : null);

    /// <summary>Where the first file lies in the form the file table is written in (<see cref="Layout"/>'s word
    /// <see cref="DiscImage.LayoutFilesAt"/>).</summary>
    public uint FilesAt => Layout[DiscImage.LayoutFilesAt];

    /// <summary>A 32-bit word from its hex digits.</summary>
    public static uint Hex(string digits) => uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}
